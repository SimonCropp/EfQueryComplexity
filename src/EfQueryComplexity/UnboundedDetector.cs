/// <summary>
/// Finds the types of the rows a query can return without a limit.
/// </summary>
/// <remarks>
/// Whether a query is unbounded for a set of levels is whether those levels check any of these types.
/// So they are found once for each compiled query, rather than once for each set of levels.
/// </remarks>
static class UnboundedDetector
{
    public static IReadOnlyList<Type> Find(Expression query)
    {
        // A query that does not return a sequence returns one row, an aggregate, or a row count
        if (!typeof(IQueryable).IsAssignableFrom(query.Type))
        {
            return [];
        }

        var types = new List<Type>();
        Collect(query, types, takeBounds: true);
        return types;
    }

    // Walks from the outermost operator towards the source, adding the row type of every source that
    // no Take limits. A sequence that is joined in is walked with takeBounds false, since the operator
    // joining it returns more rows than its source whatever limits that sequence.
    static void Collect(Expression expression, List<Type> types, bool takeBounds)
    {
        while (true)
        {
            expression = Uncast(expression);

            // Only a call on a sequence can be walked through. Anything else, such as a query root, a
            // navigation, or EF.Property, is where the rows come from.
            if (expression is not MethodCallExpression
                {
                    Method.IsStatic: true,
                    Arguments: [var source, ..]
                } call ||
                !Sequences.IsSequence(source.Type))
            {
                Add(types, Sequences.ElementType(expression.Type));
                return;
            }

            var method = call.Method;
            var declaringType = method.DeclaringType;
            if (declaringType == typeof(Queryable) ||
                declaringType == typeof(Enumerable))
            {
                switch (method.Name)
                {
                    case "Take":
                        if (takeBounds)
                        {
                            return;
                        }

                        break;

                    // These return more rows than their source, so a Take below one of them bounds the
                    // source rather than the query
                    case "SelectMany":
                        Collect(source, types, takeBounds);
                        CollectSelected(call, types);
                        return;

                    case "Join":
                    case "GroupJoin":
                    case "LeftJoin":
                    case "RightJoin":
                    case "Zip":
                        Collect(source, types, takeBounds);
                        CollectJoined(call, types);
                        return;

                    case "Concat":
                    case "Union":
                    case "UnionBy":
                        Collect(source, types, takeBounds);
                        Collect(call.Arguments[1], types, takeBounds);
                        return;
                }
            }

            // Everything else returns no more rows than its source, so keep walking towards the root
            expression = source;
        }
    }

    // The rows a SelectMany joins in are the ones its collection selector returns
    static void CollectSelected(MethodCallExpression call, List<Type> types)
    {
        var selector = call.Arguments[1];

        // Queryable takes the selector as an expression, and Enumerable, inside a lambda, as a delegate
        if (selector is UnaryExpression {NodeType: ExpressionType.Quote} quote)
        {
            selector = quote.Operand;
        }

        if (selector is LambdaExpression lambda)
        {
            Collect(lambda.Body, types, takeBounds: false);
            return;
        }

        // A method group cannot be looked into. Every overload has the type of the rows it joins in as
        // its second type argument.
        Add(types, call.Method.GetGenericArguments()[1]);
    }

    // Join, GroupJoin, LeftJoin and RightJoin take one other sequence, and Zip one or two. Selectors
    // and comparers are not sequences.
    static void CollectJoined(MethodCallExpression call, List<Type> types)
    {
        var arguments = call.Arguments;
        for (var index = 1; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (Sequences.IsSequence(argument.Type))
            {
                Collect(argument, types, takeBounds: false);
            }
        }
    }

    // A cast is only in an expression tree when written explicitly, and it does not change the rows
    static Expression Uncast(Expression expression)
    {
        while (expression is UnaryExpression
               {
                   NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs,
                   Operand: var operand
               } &&
               Sequences.IsSequence(operand.Type))
        {
            expression = operand;
        }

        return expression;
    }

    static void Add(List<Type> types, Type type)
    {
        if (!types.Contains(type))
        {
            types.Add(type);
        }
    }
}
