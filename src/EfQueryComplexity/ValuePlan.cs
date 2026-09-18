/// <summary>
/// Where a query takes its Take counts and its lists of values from, so they can be read for every
/// execution.
/// </summary>
/// <remarks>
/// Every list a query carries is sent to the database, whether a Contains, a Join, an Any or
/// anything else is what puts it there, so a list is counted wherever it appears rather than only
/// where it is used as an IN list.
/// </remarks>
sealed class ValuePlan :
    ExpressionVisitor
{
    // A constant is fixed once the query is compiled, so the largest is folded here rather than
    // measured again for every execution
    int takeConstant;
    int inConstant;
    List<string> takeParameters = [];
    List<string> inParameters = [];
    List<Func<IReadOnlyDictionary<string, object?>, int>> takeExpressions = [];

    public static ValuePlan Build(Expression query)
    {
        var plan = new ValuePlan();
        plan.Visit(query);
        return plan;
    }

    public bool IsEmpty =>
        takeConstant == 0 &&
        takeParameters.Count == 0 &&
        takeExpressions.Count == 0 &&
        inConstant == 0 &&
        inParameters.Count == 0;

    public override Expression? Visit(Expression? node)
    {
        switch (node)
        {
            case QueryParameterExpression parameter when IsValueList(parameter.Type):
                if (!inParameters.Contains(parameter.Name))
                {
                    inParameters.Add(parameter.Name);
                }

                break;

            // A list Entity Framework has already worked out, such as one written into the query
            case ConstantExpression {Value: IEnumerable values} constant when IsValueList(constant.Type):
                inConstant = Math.Max(inConstant, Counter.Count(values));
                break;
        }

        return base.Visit(node);
    }

    // What a query sends as a list of values. A string and a byte array are each one value, and a
    // subquery is not a list at all.
    public static bool IsValueList(Type type) =>
        type != typeof(byte[]) &&
        !typeof(IQueryable).IsAssignableFrom(type) &&
        Sequences.IsSequence(type);

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        var method = node.Method;
        var declaringType = method.DeclaringType;

        var arguments = node.Arguments;
        if (method.Name == "Take" &&
            arguments.Count == 2 &&
            (declaringType == typeof(Queryable) ||
             declaringType == typeof(Enumerable)))
        {
            TrackTake(arguments[1]);
        }

        return base.VisitMethodCall(node);
    }

    void TrackTake(Expression count)
    {
        switch (count)
        {
            case QueryParameterExpression parameter:
                takeParameters.Add(parameter.Name);
                break;
            case ConstantExpression {Value: int value}:
                takeConstant = Math.Max(takeConstant, value);
                break;
            default:
                TrackComputedTake(count);
                break;
        }
    }

    // A count worked out from parameters, such as Take(size * pages), which Entity Framework leaves
    // as it is inside a compiled query rather than folding it into one parameter. Reading it means
    // running it, so it is compiled once here rather than for every execution.
    void TrackComputedTake(Expression count)
    {
        var values = Expression.Parameter(typeof(IReadOnlyDictionary<string, object?>), "values");
        var reader = new ParameterReader(values);
        var body = reader.Visit(count);

        // A count the database works out, such as Take(department.Quota), is not a value that can be
        // read here at all
        if (!reader.CanRead ||
            body.Type != typeof(int))
        {
            return;
        }

        takeExpressions.Add(Expression.Lambda<Func<IReadOnlyDictionary<string, object?>, int>>(body, values).Compile());
    }

    // A value the query was not executed with cannot be checked, and zero never exceeds a level
    static int Read(IReadOnlyDictionary<string, object?> values, string name)
    {
        if (values.TryGetValue(name, out var value) &&
            value is int number)
        {
            return number;
        }

        return 0;
    }

    // Replaces each parameter of the query with a read of the value it is executed with
    sealed class ParameterReader(ParameterExpression values) :
        ExpressionVisitor
    {
        static MethodInfo read = typeof(ValuePlan).GetMethod(nameof(Read), BindingFlags.NonPublic | BindingFlags.Static)!;

        public bool CanRead { get; private set; } = true;

        public override Expression Visit(Expression? node)
        {
            // A count is worked out from whole numbers, and anything else is left to the database
            if (node is QueryParameterExpression parameter)
            {
                if (parameter.Type != typeof(int))
                {
                    CanRead = false;
                    return parameter;
                }

                return Expression.Call(read, values, Expression.Constant(parameter.Name));
            }

            // A lambda parameter reads a row, and any other node Entity Framework added is a value
            // only it knows how to read
            if (node is ParameterExpression ||
                node is {NodeType: ExpressionType.Extension})
            {
                CanRead = false;
                return node;
            }

            return base.Visit(node)!;
        }
    }

    public int LargestTake(IReadOnlyDictionary<string, object?> parameters)
    {
        var take = takeConstant;

        foreach (var name in takeParameters)
        {
            if (parameters.TryGetValue(name, out var value) &&
                value is int parameterTake &&
                parameterTake > take)
            {
                take = parameterTake;
            }
        }

        foreach (var expression in takeExpressions)
        {
            var computed = expression(parameters);
            if (computed > take)
            {
                take = computed;
            }
        }

        return take;
    }

    public int LargestInValues(IReadOnlyDictionary<string, object?> parameters)
    {
        var count = inConstant;

        foreach (var name in inParameters)
        {
            if (!parameters.TryGetValue(name, out var value))
            {
                continue;
            }

            var parameterCount = Counter.Count(value);
            if (parameterCount > count)
            {
                count = parameterCount;
            }
        }

        return count;
    }
}
