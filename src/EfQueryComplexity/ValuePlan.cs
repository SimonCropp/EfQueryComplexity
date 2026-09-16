/// <summary>
/// Where a query takes its Take counts and Contains lists from, so they can be read for every
/// execution.
/// </summary>
sealed class ValuePlan :
    ExpressionVisitor
{
    // A constant is fixed once the query is compiled, so the largest is folded here rather than
    // measured again for every execution
    int takeConstant;
    int inConstant;
    List<string> takeParameters = [];
    List<string> inParameters = [];

    public static ValuePlan Build(Expression query)
    {
        var plan = new ValuePlan();
        plan.Visit(query);
        return plan;
    }

    public bool IsEmpty =>
        takeConstant == 0 &&
        takeParameters.Count == 0 &&
        inConstant == 0 &&
        inParameters.Count == 0;

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
        else if (method.Name == "Contains")
        {
            TrackContains(node);
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
        }
    }

    void TrackContains(MethodCallExpression node)
    {
        var declaringType = node.Method.DeclaringType;

        var arguments = node.Arguments;
        if (declaringType == typeof(Enumerable) &&
            arguments.Count == 2)
        {
            TrackCollection(arguments[0]);
            return;
        }

        // C# 14 binds Contains on an array to MemoryExtensions, through a first class span
        // conversion
        if (declaringType == typeof(MemoryExtensions) &&
            arguments.Count == 2)
        {
            TrackCollection(Unwrap(arguments[0]));
            return;
        }

        // An instance call on a List, HashSet or similar
        if (node is { Object: { } instance, Arguments.Count: 1 } &&
            instance.Type != typeof(string))
        {
            TrackCollection(instance);
        }
    }

    void TrackCollection(Expression source)
    {
        switch (source)
        {
            case QueryParameterExpression parameter:
                inParameters.Add(parameter.Name);
                break;
            case ConstantExpression {Value: string}:
                break;
            case ConstantExpression {Value: IEnumerable values}:
                inConstant = Math.Max(inConstant, Counter.Count(values));
                break;
            case NewArrayExpression array:
                inConstant = Math.Max(inConstant, array.Expressions.Count);
                break;
            case ListInitExpression list:
                inConstant = Math.Max(inConstant, list.Initializers.Count);
                break;
        }
    }

    static Expression Unwrap(Expression expression)
    {
        while (true)
        {
            switch (expression)
            {
                case UnaryExpression
                {
                    NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked
                } unary:
                    expression = unary.Operand;
                    continue;
                case MethodCallExpression
                {
                    Method.Name: "op_Implicit" or "AsSpan",
                    Arguments.Count: 1
                } call:
                    expression = call.Arguments[0];
                    continue;
                default:
                    return expression;
            }
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
