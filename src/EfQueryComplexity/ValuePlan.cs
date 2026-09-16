/// <summary>
/// Where a query takes its Take counts and Contains lists from, so they can be read for every
/// execution.
/// </summary>
sealed class ValuePlan :
    ExpressionVisitor
{
    List<int> takeConstants = [];
    List<string> takeParameters = [];
    List<int> inConstants = [];
    List<string> inParameters = [];

    public static ValuePlan Build(Expression query)
    {
        var plan = new ValuePlan();
        plan.Visit(query);
        return plan;
    }

    public bool IsEmpty =>
        takeConstants.Count == 0 &&
        takeParameters.Count == 0 &&
        inConstants.Count == 0 &&
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
                takeConstants.Add(value);
                break;
        }
    }

    void TrackContains(MethodCallExpression node)
    {
        var declaringType = node.Method.DeclaringType;

        if (declaringType == typeof(Enumerable) &&
            node.Arguments.Count == 2)
        {
            TrackCollection(node.Arguments[0]);
            return;
        }

        // C# 14 binds Contains on an array to MemoryExtensions, through a first class span
        // conversion
        if (declaringType == typeof(MemoryExtensions) &&
            node.Arguments.Count == 2)
        {
            TrackCollection(Unwrap(node.Arguments[0]));
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
                inConstants.Add(Counter.Count(values));
                break;
            case NewArrayExpression array:
                inConstants.Add(array.Expressions.Count);
                break;
            case ListInitExpression list:
                inConstants.Add(list.Initializers.Count);
                break;
        }
    }

    static Expression Unwrap(Expression expression)
    {
        while (true)
        {
            switch (expression)
            {
                case UnaryExpression {NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked} unary:
                    expression = unary.Operand;
                    continue;
                case MethodCallExpression {Method.Name: "op_Implicit" or "AsSpan", Arguments.Count: 1} call:
                    expression = call.Arguments[0];
                    continue;
                default:
                    return expression;
            }
        }
    }

    public List<QueryComplexityViolation> Evaluate(IReadOnlyDictionary<string, object?> parameters, QueryComplexityLimits limits)
    {
        var violations = new List<QueryComplexityViolation>();

        if (limits.MaxTake is { } maxTake)
        {
            var take = Largest(takeConstants);

            foreach (var name in takeParameters)
            {
                if (parameters.TryGetValue(name, out var value) &&
                    value is int parameterTake &&
                    parameterTake > take)
                {
                    take = parameterTake;
                }
            }

            Violations.Add(violations, nameof(QueryComplexityLimits.MaxTake), maxTake, take);
        }

        if (limits.MaxInValues is { } maxInValues)
        {
            var count = Largest(inConstants);

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

            Violations.Add(violations, nameof(QueryComplexityLimits.MaxInValues), maxInValues, count);
        }

        return violations;
    }

    static int Largest(List<int> values)
    {
        var largest = 0;

        foreach (var value in values)
        {
            if (value > largest)
            {
                largest = value;
            }
        }

        return largest;
    }
}
