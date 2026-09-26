/// <summary>
/// Whether a Where looks its rows up by key, so returns no more rows than the values it compares the
/// key with.
/// </summary>
/// <remarks>
/// A key is the primary key or an alternate key. A unique index is not used, since a filter can make
/// it unique among only some of the rows, and a column that allows null can hold null in many rows.
/// </remarks>
static class KeyLookup
{
    /// <param name="where">A call to Where.</param>
    /// <param name="listsLimited">
    /// Whether the size of a list the query sends is limited. A key looked up in a list returns a row
    /// for each value in it, so only bounds the query when the list is limited.
    /// </param>
    public static bool Bounds(MethodCallExpression where, bool listsLimited)
    {
        // Queryable takes the predicate as an expression, and Enumerable, inside a lambda, as a delegate
        var predicate = where.Arguments[1];
        if (predicate is UnaryExpression {NodeType: ExpressionType.Quote} quote)
        {
            predicate = quote.Operand;
        }

        if (predicate is not LambdaExpression {Parameters: [var row]} lambda)
        {
            return false;
        }

        // A key picks one row only when each row appears once, so between the Where and the DbSet there
        // can only be operators that return some of its rows, unchanged
        var source = where.Arguments[0];
        while (source is MethodCallExpression call &&
               RowOperators.KeepsRows(call.Method))
        {
            source = call.Arguments[0];
        }

        // FromSql, and the other roots derived from this one, can return a key more than once
        if (source is not EntityQueryRootExpression root ||
            root.GetType() != typeof(EntityQueryRootExpression))
        {
            return false;
        }

        return Bounds(lambda.Body, row, root.EntityType, listsLimited);
    }

    static bool Bounds(Expression predicate, ParameterExpression row, IEntityType entityType, bool listsLimited)
    {
        var conditions = new List<Expression>();
        AddConditions(predicate, conditions);

        var compared = new List<IProperty>();
        var listed = new List<IProperty>();
        foreach (var condition in conditions)
        {
            // Each side of an or returns its own rows, so both have to be bounded
            if (condition is BinaryExpression {NodeType: ExpressionType.OrElse} or)
            {
                if (Bounds(or.Left, row, entityType, listsLimited) &&
                    Bounds(or.Right, row, entityType, listsLimited))
                {
                    return true;
                }

                continue;
            }

            if (Compared(condition, row, entityType) is { } comparedProperty)
            {
                compared.Add(comparedProperty);
                continue;
            }

            if (listsLimited &&
                Listed(condition, row, entityType) is { } listedProperty)
            {
                listed.Add(listedProperty);
            }
        }

        foreach (var key in entityType.GetKeys())
        {
            if (Covers(key, compared, listed))
            {
                return true;
            }
        }

        return false;
    }

    // The conditions every row the predicate returns meets
    static void AddConditions(Expression predicate, List<Expression> conditions)
    {
        if (predicate is BinaryExpression {NodeType: ExpressionType.AndAlso} and)
        {
            AddConditions(and.Left, conditions);
            AddConditions(and.Right, conditions);
            return;
        }

        conditions.Add(predicate);
    }

    // Every part of the key is compared with one value, or every part but one is, and that one is
    // looked up in a list
    static bool Covers(IKey key, List<IProperty> compared, List<IProperty> listed)
    {
        IProperty? remaining = null;
        foreach (var property in key.Properties)
        {
            if (compared.Contains(property))
            {
                continue;
            }

            if (remaining != null)
            {
                return false;
            }

            remaining = property;
        }

        return remaining == null ||
               listed.Contains(remaining);
    }

    // The property a condition compares with one value, as in _.Id == id
    static IProperty? Compared(Expression condition, ParameterExpression row, IEntityType entityType)
    {
        if (condition is not BinaryExpression {NodeType: ExpressionType.Equal} equal)
        {
            return null;
        }

        if (IsValue(equal.Right))
        {
            return Property(equal.Left, row, entityType);
        }

        if (IsValue(equal.Left))
        {
            return Property(equal.Right, row, entityType);
        }

        return null;
    }

    // One value for the whole query, rather than one read from each row. Entity Framework has already
    // turned every value it can work out itself into a parameter or a constant.
    static bool IsValue(Expression expression) =>
        Uncast(expression) is QueryParameterExpression or ConstantExpression;

    // The property a condition looks up in a list the query sends, as in ids.Contains(_.Id)
    static IProperty? Listed(Expression condition, ParameterExpression row, IEntityType entityType)
    {
        if (condition is not MethodCallExpression {Method.Name: "Contains"} call)
        {
            return null;
        }

        Expression list;
        Expression item;

        // Enumerable.Contains(ids, _.Id), or the Contains of a list type, such as List<T>
        if (call is {Object: null, Arguments: [var source, var value]})
        {
            list = source;
            item = value;
        }
        else if (call is {Object: { } instance, Arguments: [var only]})
        {
            list = instance;
            item = only;
        }
        else
        {
            return null;
        }

        // Only a list the query sends is limited by MaxInValues. A subquery can return any number of
        // values.
        if (Uncast(list) is not (QueryParameterExpression or ConstantExpression) ||
            !ValuePlan.IsValueList(list.Type))
        {
            return null;
        }

        return Property(item, row, entityType);
    }

    // The property of the row an expression reads, as in _.Id or EF.Property<int>(_, "Id")
    static IProperty? Property(Expression expression, ParameterExpression row, IEntityType entityType)
    {
        switch (Uncast(expression))
        {
            case MemberExpression {Expression: { } instance} member when Uncast(instance) == row:
                return entityType.FindProperty(member.Member.Name);

            case MethodCallExpression {Arguments: [var instance, ConstantExpression {Value: string name}]} call
                when ShapeAnalyzer.IsProperty(call) && Uncast(instance) == row:
                return entityType.FindProperty(name);

            default:
                return null;
        }
    }

    // A cast that keeps different values different, such as the one comparing an int key with an int?
    // value, or an enum key with a number. Casting a row to a type in its hierarchy keeps the row.
    static Expression Uncast(Expression expression)
    {
        while (expression is UnaryExpression
               {
                   NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs,
                   Operand: var operand
               } cast &&
               (!operand.Type.IsValueType ||
                Underlying(operand.Type) == Underlying(cast.Type)))
        {
            expression = operand;
        }

        return expression;
    }

    // The type that holds the values of a nullable or an enum
    static Type Underlying(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsEnum)
        {
            return Enum.GetUnderlyingType(type);
        }

        return type;
    }
}
