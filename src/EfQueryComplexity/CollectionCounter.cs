/// <summary>
/// Counts the collections one SQL query loads side by side: collection Includes, and collections a
/// projection returns.
/// </summary>
/// <remarks>
/// A single query joins every collection it loads. Collections side by side multiply each other's
/// rows, a cartesian explosion. A collection nested in another does not: the query returns a row per
/// item of the inner one, so a chain of nested collections counts as one. A collection holding
/// several side by side counts as many as it holds. A split query loads each collection in its own
/// query, so counts none. A collection only read by an aggregate, like <c>_.Employees.Count()</c>, is
/// a subquery rather than a join, so is not counted. Nor is an Include that Entity Framework ignores,
/// since the query returns no entity for it to load into.
/// </remarks>
sealed class CollectionCounter(IModel model) :
    ExpressionVisitor
{
    // Keyed on the full path from the root, so an Include chain that restates a collection, to
    // ThenInclude something else below it, counts that collection once
    HashSet<string> includePaths = [];
    HashSet<MethodCallExpression> ignoredIncludes = [];

    // The collections a projection returns in place of the entity they belong to, keyed like
    // includePaths, with the count of what the projection loads inside each. An Include of one of
    // them only says what to load under it, so the two are one collection.
    Dictionary<string, int> projectedPaths = [];

    // What projections load that has no path, so cannot be nested under an Include
    int projected;

    public static int Count(Expression query, IModel model, bool splitByDefault)
    {
        if (IsSplit(query, splitByDefault))
        {
            return 0;
        }

        var counter = new CollectionCounter(model);
        counter.Visit(query);
        return counter.CountPaths() + counter.projected;
    }

    // Each collection counts as one, or as the sum of what it holds when that is more. A path is held
    // by the longest other path it starts with, so walking the longest first counts every collection
    // before the one holding it.
    int CountPaths()
    {
        var held = new Dictionary<string, int>(projectedPaths);
        foreach (var path in includePaths)
        {
            held.TryAdd(path, 0);
        }

        var total = 0;
        foreach (var path in held.Keys.OrderByDescending(_ => _.Length).ToList())
        {
            var count = Math.Max(1, held[path]);
            var holder = Holder(path, held);
            if (holder == null)
            {
                total += count;
            }
            else
            {
                held[holder] += count;
            }
        }

        return total;
    }

    static string? Holder(string path, Dictionary<string, int> paths)
    {
        var current = path;
        while (true)
        {
            var index = current.LastIndexOf('.');
            if (index < 0)
            {
                return null;
            }

            current = current[..index];
            if (paths.ContainsKey(current))
            {
                return current;
            }
        }
    }

    // AsSplitQuery and AsSingleQuery apply to the whole query, and override the context default
    static bool IsSplit(Expression query, bool splitByDefault)
    {
        var current = query;
        while (current is MethodCallExpression {Arguments.Count: > 0} call)
        {
            if (call.Method.DeclaringType == typeof(RelationalQueryableExtensions))
            {
                switch (call.Method.Name)
                {
                    case nameof(RelationalQueryableExtensions.AsSplitQuery):
                        return true;
                    case nameof(RelationalQueryableExtensions.AsSingleQuery):
                        return false;
                }
            }

            current = call.Arguments[0];
        }

        return splitByDefault;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        var method = node.Method;
        var declaringType = method.DeclaringType;

        if (IsInclude(method))
        {
            if (!ignoredIncludes.Contains(node))
            {
                AddIncludePaths(node);
            }

            return base.VisitMethodCall(node);
        }

        if (declaringType != typeof(Queryable) &&
            declaringType != typeof(Enumerable))
        {
            return base.VisitMethodCall(node);
        }

        if (method.Name == "Select")
        {
            var selector = node.Arguments[1];
            if (!ReturnsEntities(selector))
            {
                IgnoreIncludes(node.Arguments[0]);
            }

            Visit(node.Arguments[0]);
            var projection = new ProjectionCounter(this, RowNotReturned(selector));
            projection.Visit(selector);
            projected += projection.Count;
            return node;
        }

        // An aggregate, like Count or Any, returns a value rather than entities
        if (node.Arguments is [var source, ..] &&
            !Sequences.IsSequence(node.Type) &&
            !HoldsEntities(node.Type))
        {
            IgnoreIncludes(source);
        }

        return base.VisitMethodCall(node);
    }

    static bool IsInclude(MethodInfo method) =>
        method.DeclaringType == typeof(EntityFrameworkQueryableExtensions) &&
        method.Name is "Include" or "ThenInclude";

    // An Include only loads into the entities a query returns, so Entity Framework ignores the ones
    // before an operator that returns none. Walking stops at an operator that changes the rows, since
    // an Include below it can load into what that operator returns.
    void IgnoreIncludes(Expression source)
    {
        var current = source;
        while (current is MethodCallExpression call &&
               RowOperators.KeepsRows(call.Method))
        {
            if (IsInclude(call.Method))
            {
                ignoredIncludes.Add(call);
            }

            current = call.Arguments[0];
        }
    }

    bool ReturnsEntities(Expression selector)
    {
        // Queryable takes the selector as an expression, and Enumerable, inside a lambda, as a delegate
        if (selector is UnaryExpression {NodeType: ExpressionType.Quote} quote)
        {
            selector = quote.Operand;
        }

        // A method group cannot be looked into, so it could return anything
        if (selector is not LambdaExpression lambda)
        {
            return true;
        }

        var finder = new EntityFinder(this);
        finder.Visit(lambda.Body);
        return finder.Found;
    }

    // The row a selector is given, when the selector only reads members of it. A row that is also
    // returned, or passed to a method, has its Includes loaded into it, as well as whatever the
    // projection loads.
    static ParameterExpression? RowNotReturned(Expression selector)
    {
        if (selector is UnaryExpression {NodeType: ExpressionType.Quote} quote)
        {
            selector = quote.Operand;
        }

        if (selector is not LambdaExpression {Parameters: [var row, ..]} lambda)
        {
            return null;
        }

        var finder = new BareUseFinder(row);
        finder.Visit(lambda.Body);
        if (finder.Found)
        {
            return null;
        }

        return row;
    }

    // Whether a value is, or holds, entities. A shared type counts, since any entity could use it.
    bool HoldsEntities(Type type)
    {
        var element = Sequences.ElementType(type);
        if (element.IsValueType ||
            element == typeof(string))
        {
            return false;
        }

        return model.IsShared(element) ||
               model.FindEntityType(element) != null;
    }

    void AddIncludePaths(MethodCallExpression node)
    {
        var path = new List<string>();
        var rootType = IncludeChainRoot(node, path);
        var type = rootType;
        var key = PathRoot(rootType);

        foreach (var name in path)
        {
            var navigation = FindNavigation(type, name);
            if (navigation == null)
            {
                return;
            }

            key += "." + name;
            if (navigation.IsCollection)
            {
                includePaths.Add(key);
            }

            type = navigation.TargetEntityType.ClrType;
        }
    }

    // Paths start with the entity they are from, so one only matches a path from the same entity
    static string PathRoot(Type type) =>
        type.FullName ?? type.Name;

    // Fills path with the navigation names from the root entity to the end of this Include chain,
    // and returns the root entity type
    static Type IncludeChainRoot(MethodCallExpression node, List<string> path)
    {
        var chain = new List<MethodCallExpression>();
        var current = node;
        chain.Add(current);

        // A ThenInclude can only follow an Include or another ThenInclude
        while (current.Method.Name == "ThenInclude")
        {
            current = (MethodCallExpression) current.Arguments[0];
            chain.Add(current);
        }

        chain.Reverse();
        foreach (var call in chain)
        {
            path.AddRange(Segments(call.Arguments[1]));
        }

        // Include<TEntity, TProperty>, or Include<TEntity> for the string overload
        return current.Method.GetGenericArguments()[0];
    }

    static IEnumerable<string> Segments(Expression path)
    {
        // The string overload takes a dotted path
        if (path is ConstantExpression {Value: string text})
        {
            return text.Split('.');
        }

        var body = ((LambdaExpression) ((UnaryExpression) path).Operand).Body;
        var segments = new List<string>();

        while (true)
        {
            switch (body)
            {
                // A filtered Include wraps the navigation in operators like Where and OrderBy
                case MethodCallExpression call when (call.Object ?? call.Arguments.FirstOrDefault()) is { } source:
                    body = source;
                    continue;
                case UnaryExpression unary:
                    body = unary.Operand;
                    continue;
                case MemberExpression {Expression: { } inner} member:
                    segments.Add(member.Member.Name);
                    body = inner;
                    continue;
                default:
                    segments.Reverse();
                    return segments;
            }
        }
    }

    INavigationBase? FindNavigation(Type type, string name)
    {
        var entityType = model.FindEntityType(type);
        if (entityType == null)
        {
            return null;
        }

        // A derived type can declare the navigation, reached with a cast in the Include
        foreach (var candidate in entityType.GetDerivedTypesInclusive())
        {
            var navigation = (INavigationBase?) candidate.FindNavigation(name) ??
                             candidate.FindSkipNavigation(name);
            if (navigation != null)
            {
                return navigation;
            }
        }

        return null;
    }

    bool IsCollectionNavigation(Type type)
    {
        if (!Sequences.IsSequence(type))
        {
            return false;
        }

        var element = Sequences.ElementType(type);
        if (element.IsValueType ||
            element == typeof(string) ||
            model.IsShared(element))
        {
            return false;
        }

        var entityType = model.FindEntityType(element);

        // An owned collection mapped to JSON is a column of its owner, not a join
        return entityType != null &&
               !entityType.IsMappedToJson();
    }

    /// <summary>
    /// Counts the collections a projection returns side by side, a collection counting as what it
    /// holds when that is more than one.
    /// </summary>
    sealed class ProjectionCounter(CollectionCounter counter, ParameterExpression? row) :
        ExpressionVisitor
    {
        // The collections with no path, which an Include cannot be nested under
        public int Count { get; private set; }

        public override Expression? Visit(Expression? node)
        {
            if (node == null)
            {
                return null;
            }

            if (Sequences.IsSequence(node.Type) &&
                LoadsRows(node, out var navigation))
            {
                // A projection inside the collection can return further collections, which are
                // nested in it
                var inner = new ProjectionCounter(counter, null);
                inner.VisitSelectors(node);
                if (!AddProjectedPath(navigation, inner.Count))
                {
                    Count += Math.Max(1, inner.Count);
                }

                return node;
            }

            return base.Visit(node);
        }

        // An aggregate, like Count or Any, reads a collection without returning it
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.IsStatic &&
                node.Arguments.Count > 0 &&
                Sequences.IsSequence(node.Arguments[0].Type))
            {
                foreach (var argument in node.Arguments.Skip(1))
                {
                    Visit(argument);
                }

                return node;
            }

            return base.VisitMethodCall(node);
        }

        // A member of a collection, like List.Count, reads it without returning it
        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression != null &&
                Sequences.IsSequence(node.Expression.Type))
            {
                return node;
            }

            return base.VisitMember(node);
        }

        // Whether a sequence comes from the database: a collection navigation, or a query
        bool LoadsRows(Expression node, out MemberExpression? navigation)
        {
            navigation = null;
            var current = node;
            while (true)
            {
                switch (current)
                {
                    case MemberExpression member when counter.IsCollectionNavigation(member.Type):
                        navigation = member;
                        return true;
                    case MethodCallExpression {Method.IsStatic: true, Arguments.Count: > 0} call:
                        current = call.Arguments[0];
                        continue;
                    case UnaryExpression unary:
                        current = unary.Operand;
                        continue;
                    case EntityQueryRootExpression:
                        return true;
                    default:
                        return false;
                }
            }
        }

        // Records a collection read from the row the projection is given, by the path an Include
        // would name it with. A collection projected twice is loaded twice, so the second has no path.
        bool AddProjectedPath(MemberExpression? navigation, int inner)
        {
            if (navigation == null ||
                row == null)
            {
                return false;
            }

            var path = "";
            Expression? current = navigation;
            while (true)
            {
                switch (current)
                {
                    case MemberExpression member:
                        path = "." + member.Member.Name + path;
                        current = member.Expression;
                        continue;
                    case UnaryExpression
                    {
                        NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs
                    } cast:
                        current = cast.Operand;
                        continue;
                }

                break;
            }

            if (current != row)
            {
                return false;
            }

            return counter.projectedPaths.TryAdd(PathRoot(row.Type) + path, inner);
        }

        void VisitSelectors(Expression node)
        {
            var current = node;
            while (current is MethodCallExpression {Method.IsStatic: true, Arguments.Count: > 0} call)
            {
                foreach (var argument in call.Arguments.Skip(1))
                {
                    Visit(argument);
                }

                current = call.Arguments[0];
            }
        }
    }

    /// <summary>
    /// Finds a use of a row other than reading a member of it.
    /// </summary>
    sealed class BareUseFinder(ParameterExpression row) :
        ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitMember(MemberExpression node)
        {
            var source = node.Expression;
            while (source is UnaryExpression
                   {
                       NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs
                   } cast)
            {
                source = cast.Operand;
            }

            if (source == row)
            {
                return node;
            }

            return base.VisitMember(node);
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == row)
            {
                Found = true;
            }

            return node;
        }
    }

    /// <summary>
    /// Finds an entity a projection can return, which the Includes before it would load into.
    /// </summary>
    /// <remarks>
    /// An entity is not returned when a member is read from it, when it is compared, or when a LINQ
    /// operator reads it. What those return is checked where it is used. An entity anywhere else,
    /// such as in a constructor or passed to a method, could be returned.
    /// </remarks>
    sealed class EntityFinder(CollectionCounter counter) :
        ExpressionVisitor
    {
        public bool Found { get; private set; }

        public override Expression? Visit(Expression? node)
        {
            if (node != null &&
                counter.HoldsEntities(node.Type))
            {
                Found = true;
                return node;
            }

            return base.Visit(node);
        }

        // The parameters are rows coming in, not what the lambda returns
        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            Visit(node.Body);
            return node;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression != null)
            {
                Read(node.Expression);
            }

            return node;
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType is ExpressionType.Equal or ExpressionType.NotEqual)
            {
                Read(node.Left);
                Read(node.Right);
                return node;
            }

            return base.VisitBinary(node);
        }

        // A LINQ operator, or EF.Property, reads its source rather than returning it
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            var declaringType = node.Method.DeclaringType;
            if (node.Arguments is [var source, ..] &&
                (declaringType == typeof(Queryable) ||
                 declaringType == typeof(Enumerable) ||
                 ShapeAnalyzer.IsProperty(node)))
            {
                Read(source);
                foreach (var argument in node.Arguments.Skip(1))
                {
                    Visit(argument);
                }

                return node;
            }

            return base.VisitMethodCall(node);
        }

        // Looks into a value that is read rather than returned. A cast, such as the one reaching a
        // member of a derived type, is read along with it.
        void Read(Expression value)
        {
            while (value is UnaryExpression
                   {
                       NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs
                   } cast)
            {
                value = cast.Operand;
            }

            base.Visit(value);
        }
    }
}
