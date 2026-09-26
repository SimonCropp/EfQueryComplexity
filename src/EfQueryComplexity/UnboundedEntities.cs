namespace EfQueryComplexity;

/// <summary>
/// The types RejectUnbounded checks.
/// </summary>
/// <remarks>
/// A query fires when it returns rows of a checked type without a Take, or a lookup by key. Naming a
/// type also names the types derived from it, and naming an interface names the types that
/// implement it. Rows that are not entities, such as a list of values, are checked by
/// <see cref="All" /> and <see cref="AllExcept" />, and not by <see cref="Only" />. <c>true</c>
/// converts to <see cref="All" />, and <c>false</c> to <see cref="None" />.
/// </remarks>
public sealed class UnboundedEntities :
    IEquatable<UnboundedEntities>
{
    // Whether the types are the ones skipped, rather than the ones checked
    bool except;

    // Distinct, and sorted so that ToString does not depend on the order they were given in
    Type[] types;

    UnboundedEntities(bool except, Type[] types)
    {
        this.except = except;
        this.types = types;
    }

    /// <summary>
    /// Checks every type.
    /// </summary>
    public static UnboundedEntities All { get; } = new(true, []);

    /// <summary>
    /// Checks no type, which turns RejectUnbounded off.
    /// </summary>
    public static UnboundedEntities None { get; } = new(false, []);

    /// <summary>
    /// Checks every type except these, and the types derived from them. For types known to have few
    /// rows.
    /// </summary>
    /// <param name="types">The types to skip. None is the same as <see cref="All" />.</param>
    public static UnboundedEntities AllExcept(params Type[] types)
    {
        var distinct = Distinct(types);
        if (distinct.Length == 0)
        {
            return All;
        }

        return new(true, distinct);
    }

    /// <summary>
    /// Checks only these types, and the types derived from them. For types known to have many rows.
    /// </summary>
    /// <param name="types">The types to check. None is the same as <see cref="None" />.</param>
    public static UnboundedEntities Only(params Type[] types)
    {
        var distinct = Distinct(types);
        if (distinct.Length == 0)
        {
            return None;
        }

        return new(false, distinct);
    }

    /// <summary>
    /// Converts <c>true</c> to <see cref="All" />, and <c>false</c> to <see cref="None" />.
    /// </summary>
    /// <param name="reject">Whether every type is checked.</param>
    public static implicit operator UnboundedEntities(bool reject)
    {
        if (reject)
        {
            return All;
        }

        return None;
    }

    internal bool IsNone =>
        !except &&
        types.Length == 0;

    // A query on a type returns rows of that type and of the types derived from it
    internal bool Covers(Type type)
    {
        if (except)
        {
            foreach (var skipped in types)
            {
                if (skipped.IsAssignableFrom(type))
                {
                    return false;
                }
            }

            return true;
        }

        foreach (var @checked in types)
        {
            // A query on a base type also returns the rows of a checked type derived from it
            if (@checked.IsAssignableFrom(type) ||
                type.IsAssignableFrom(@checked))
            {
                return true;
            }
        }

        return false;
    }

    static Type[] Distinct(Type[] types)
    {
        ArgumentNullException.ThrowIfNull(types);

        var distinct = new List<Type>(types.Length);
        foreach (var type in types)
        {
            if (type == null)
            {
                throw new ArgumentException("A type cannot be null.", nameof(types));
            }

            // Rows always have a closed type, so an open generic type would never match
            if (type.ContainsGenericParameters)
            {
                throw new ArgumentException($"{type} is an open generic type, and no query returns rows of one.", nameof(types));
            }

            if (!distinct.Contains(type))
            {
                distinct.Add(type);
            }
        }

        distinct.Sort(CompareNames);
        return distinct.ToArray();
    }

    static int CompareNames(Type x, Type y) =>
        string.CompareOrdinal(x.FullName, y.FullName);

    /// <summary>
    /// Whether this checks the same types as <paramref name="other" />.
    /// </summary>
    /// <param name="other">The value to compare with.</param>
    public bool Equals(UnboundedEntities? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        // The types are distinct, so the same count with every type found is the same set
        if (except != other.except ||
            types.Length != other.types.Length)
        {
            return false;
        }

        foreach (var type in types)
        {
            if (Array.IndexOf(other.types, type) < 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether this checks the same types as <paramref name="obj" />.
    /// </summary>
    /// <param name="obj">The value to compare with.</param>
    public override bool Equals(object? obj) =>
        Equals(obj as UnboundedEntities);

    /// <summary>
    /// A hash code that does not depend on the order the types were given in.
    /// </summary>
    public override int GetHashCode()
    {
        var hash = except.GetHashCode();
        foreach (var type in types)
        {
            hash ^= type.GetHashCode();
        }

        return hash;
    }

    /// <summary>
    /// Whether two values check the same types.
    /// </summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    public static bool operator ==(UnboundedEntities? left, UnboundedEntities? right) =>
        Equals(left, right);

    /// <summary>
    /// Whether two values check different types.
    /// </summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    public static bool operator !=(UnboundedEntities? left, UnboundedEntities? right) =>
        !Equals(left, right);

    /// <summary>
    /// Describes the types, for example <c>AllExcept(AccessGroup, User)</c>.
    /// </summary>
    public override string ToString()
    {
        if (types.Length == 0)
        {
            if (except)
            {
                return "All";
            }

            return "None";
        }

        var names = string.Join(", ", types.Select(_ => _.Name));
        if (except)
        {
            return $"AllExcept({names})";
        }

        return $"Only({names})";
    }
}
