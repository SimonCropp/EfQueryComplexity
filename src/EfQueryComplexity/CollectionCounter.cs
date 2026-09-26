/// <summary>
/// Counts the collections one SQL query loads: collection Includes, and collections a projection
/// returns.
/// </summary>
/// <remarks>
/// A single query joins every collection it loads, so each multiplies the rows returned for the
/// others, a cartesian explosion. A split query loads each collection in its own query, so counts
/// none. A collection only read by an aggregate, like <c>_.Employees.Count()</c>, is a subquery rather
/// than a join, so is not counted.
/// </remarks>
sealed class CollectionCounter(IModel model) :
    ExpressionVisitor
{
    // Keyed on the full path from the root, so an Include chain that restates a collection, to
    // ThenInclude something else below it, counts that collection once
    HashSet<string> includePaths = [];
    int projected;

    public static int Count(Expression query, IModel model, bool splitByDefault)
    {
        if (IsSplit(query, splitByDefault))
        {
            return 0;
        }

        var counter = new CollectionCounter(model);
        counter.Visit(query);
        return counter.includePaths.Count + counter.projected;
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

        if (declaringType == typeof(EntityFrameworkQueryableExtensions) &&
            method.Name is "Include" or "ThenInclude")
        {
            AddIncludePaths(node);
            return base.VisitMethodCall(node);
        }

        if ((declaringType == typeof(Queryable) || declaringType == typeof(Enumerable)) &&
            method.Name == "Select")
        {
            Visit(node.Arguments[0]);
            new ProjectionCounter(this).Visit(node.Arguments[1]);
            return node;
        }

        return base.VisitMethodCall(node);
    }

    void AddIncludePaths(MethodCallExpression node)
    {
        var path = new List<string>();
        var rootType = IncludeChainRoot(node, path);
        var type = rootType;
        var key = "";

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
    /// Counts the collections a projection returns, including collections nested in them.
    /// </summary>
    sealed class ProjectionCounter(CollectionCounter counter) :
        ExpressionVisitor
    {
        public override Expression? Visit(Expression? node)
        {
            if (node == null)
            {
                return null;
            }

            if (Sequences.IsSequence(node.Type) &&
                LoadsRows(node))
            {
                counter.projected++;

                // A projection inside the collection can return further collections
                VisitSelectors(node);
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
        bool LoadsRows(Expression node)
        {
            var current = node;
            while (true)
            {
                switch (current)
                {
                    case MemberExpression member when counter.IsCollectionNavigation(member.Type):
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
}
