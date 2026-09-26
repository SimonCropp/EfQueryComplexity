/// <summary>
/// Measures a query in a single pass.
/// </summary>
sealed class ShapeAnalyzer(IModel model) :
    ExpressionVisitor
{
    // Type lookups are not cached beyond one query, so no type is kept alive for the life of the
    // process
    Dictionary<Type, bool> navigations = [];
    int nodes;
    int depth;
    int maxDepth;
    int operators;
    int includes;
    int includeDepth;
    int navigationDepth;

    public static QueryShape Analyze(Expression query, IModel model, bool splitByDefault)
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
            CollectionCounter.Count(query, model, splitByDefault),
            UnboundedDetector.Find(query));
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

        // A chain that ends in EF.Property rather than in a member is measured from the call
        if (IsProperty(node))
        {
            Track(ref navigationDepth, NavigationsInChain(node));
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
        var depth = Segments(node.Arguments[1]);
        var current = node;

        // A ThenInclude can only follow an Include or another ThenInclude
        while (current.Method.Name == "ThenInclude")
        {
            current = (MethodCallExpression) current.Arguments[0];
            depth += Segments(current.Arguments[1]);
        }

        return depth;
    }

    // The navigations one Include or ThenInclude names
    static int Segments(Expression path)
    {
        // The string overload takes a dotted path
        if (path is ConstantExpression {Value: string text})
        {
            return text.Split('.').Length;
        }

        var body = ((LambdaExpression) ((UnaryExpression) path).Operand).Body;
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
                // Include paths only contain navigations, so every member in the path counts
                case MemberExpression {Expression: { } inner}:
                    segments++;
                    body = inner;
                    continue;
                default:
                    return Math.Max(segments, 1);
            }
        }
    }

    int NavigationsInChain(Expression node)
    {
        var count = 0;
        var current = node;

        while (true)
        {
            switch (current)
            {
                case MemberExpression member:
                    if (IsNavigation(member.Member))
                    {
                        count++;
                    }

                    current = member.Expression;
                    continue;

                // A cast is how a chain reaches a navigation on a derived type, so it continues the
                // chain rather than ending it
                case UnaryExpression
                {
                    NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs,
                    Operand: var operand
                }:
                    current = operand;
                    continue;

                // EF.Property names a navigation as a string, and joins like any other
                case MethodCallExpression call when IsProperty(call):
                    if (IsNavigation(call.Type))
                    {
                        count++;
                    }

                    current = call.Arguments[0];
                    continue;

                default:
                    return count;
            }
        }
    }

    static bool IsProperty(MethodCallExpression call) =>
        call.Method.DeclaringType == typeof(EF) &&
        call.Method.Name == nameof(EF.Property);

    bool IsNavigation(MemberInfo member)
    {
        // A member expression only ever reads a property or a field
        var type = member switch
        {
            PropertyInfo property => property.PropertyType,
            _ => ((FieldInfo) member).FieldType
        };

        return IsNavigation(type);
    }

    bool IsNavigation(Type type)
    {
        if (navigations.TryGetValue(type, out var isNavigation))
        {
            return isNavigation;
        }

        // A collection navigation is measured by what it holds
        isNavigation = IsEntity(Sequences.ElementType(type));
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

    static void Track(ref int current, int value)
    {
        if (value > current)
        {
            current = value;
        }
    }
}
