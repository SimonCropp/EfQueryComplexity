/// <summary>
/// Measures a query in a single pass.
/// </summary>
sealed class ShapeAnalyzer(IModel model) :
    ExpressionVisitor
{
    // Type lookups are not cached beyond one query, so no type is kept alive for the life of the
    // process
    readonly Dictionary<Type, bool> navigations = [];
    int nodes;
    int depth;
    int maxDepth;
    int operators;
    int includes;
    int includeDepth;
    int navigationDepth;

    public static QueryShape Analyze(Expression query, IModel model)
    {
        var analyzer = new ShapeAnalyzer(model);
        analyzer.Visit(query);
        return new(
            analyzer.nodes,
            analyzer.maxDepth,
            analyzer.operators,
            analyzer.navigationDepth,
            analyzer.includes,
            analyzer.includeDepth,
            UnboundedDetector.IsUnbounded(query));
    }

    public override Expression? Visit(Expression? node)
    {
        if (node == null)
        {
            return null;
        }

        nodes++;
        depth++;
        Track(ref maxDepth, depth);
        var result = base.Visit(node);
        depth--;
        return result;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        var method = node.Method;
        var declaringType = method.DeclaringType;

        if (declaringType == typeof(Queryable) ||
            declaringType == typeof(Enumerable) ||
            declaringType == typeof(MemoryExtensions) ||
            declaringType == typeof(EntityFrameworkQueryableExtensions) ||
            declaringType == typeof(RelationalQueryableExtensions))
        {
            operators++;
        }

        if (declaringType == typeof(EntityFrameworkQueryableExtensions))
        {
            var name = method.Name;

            if (name == "Include")
            {
                includes++;
            }

            if (name is "Include" or "ThenInclude")
            {
                Track(ref includeDepth, ChainDepth(node));
            }
        }

        return base.VisitMethodCall(node);
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        Track(ref navigationDepth, NavigationsInChain(node));
        return base.VisitMember(node);
    }

    // The depth of the Include chain ending at this call. An inner call of the same chain measures a
    // shorter depth, and the longest wins.
    static int ChainDepth(MethodCallExpression node)
    {
        var depth = 0;
        var current = node;

        while (true)
        {
            depth += Segments(current.Arguments[1]);

            if (current.Method.Name == "Include")
            {
                return depth;
            }

            if (current.Arguments[0] is MethodCallExpression previous &&
                previous.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions) &&
                previous.Method.Name is "Include" or "ThenInclude")
            {
                current = previous;
                continue;
            }

            return depth;
        }
    }

    // The navigations one Include or ThenInclude names
    static int Segments(Expression path)
    {
        // The string overload takes a dotted path
        if (path is ConstantExpression {Value: string text})
        {
            return text.Split('.').Length;
        }

        if (path is not UnaryExpression {Operand: LambdaExpression lambda})
        {
            return 1;
        }

        var body = lambda.Body;
        var segments = 0;

        while (true)
        {
            switch (body)
            {
                // A filtered Include wraps the navigation in operators like Where and OrderBy
                case MethodCallExpression call:
                    var source = call.Object ?? call.Arguments.FirstOrDefault();
                    if (source == null)
                    {
                        return Math.Max(segments, 1);
                    }

                    body = source;
                    continue;
                case UnaryExpression unary:
                    body = unary.Operand;
                    continue;
                case MemberExpression {Expression: { } inner} member:
                    if (!IsNavigationCandidate(member))
                    {
                        return Math.Max(segments, 1);
                    }

                    segments++;
                    body = inner;
                    continue;
                default:
                    return Math.Max(segments, 1);
            }
        }
    }

    // Include paths only contain navigations, so every member in the path counts
    static bool IsNavigationCandidate(MemberExpression member) =>
        member.Member is PropertyInfo or FieldInfo;

    int NavigationsInChain(MemberExpression node)
    {
        var count = 0;
        Expression? current = node;

        while (current is MemberExpression member)
        {
            if (IsNavigation(member.Member))
            {
                count++;
            }

            current = member.Expression;
        }

        return count;
    }

    bool IsNavigation(MemberInfo member)
    {
        var type = member switch
        {
            PropertyInfo property => property.PropertyType,
            FieldInfo field => field.FieldType,
            _ => null
        };

        if (type == null)
        {
            return false;
        }

        if (navigations.TryGetValue(type, out var isNavigation))
        {
            return isNavigation;
        }

        isNavigation = IsEntity(ElementType(type));
        navigations.Add(type, isNavigation);
        return isNavigation;
    }

    bool IsEntity(Type type)
    {
        if (type.IsValueType ||
            type == typeof(string) ||
            model.IsShared(type))
        {
            return false;
        }

        var entityType = model.FindEntityType(type);

        // An owned type is stored with its owner, so reaching into one is not a join
        return entityType != null &&
               !entityType.IsOwned();
    }

    // A collection navigation is measured by what it holds
    static Type ElementType(Type type)
    {
        if (type == typeof(string) ||
            !typeof(IEnumerable).IsAssignableFrom(type))
        {
            return type;
        }

        foreach (var @interface in type.GetInterfaces())
        {
            if (@interface.IsGenericType &&
                @interface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return @interface.GetGenericArguments()[0];
            }
        }

        return type;
    }

    static void Track(ref int current, int value)
    {
        if (value > current)
        {
            current = value;
        }
    }
}
