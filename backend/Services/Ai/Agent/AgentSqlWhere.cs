using System.Linq.Expressions;
using System.Reflection;

namespace Delegat.Services.Ai.Agent;

internal static class AgentSqlWhere
{
    public static IQueryable<T> Apply<T>(IQueryable<T> src, IReadOnlyList<AgentSqlPredicate>? where)
    {
        if (where == null || where.Count == 0)
            return src;

        foreach (var pred in where)
        {
            var prop = FindProperty(typeof(T), pred.Column)
                ?? throw new AgentSqlFilterException($"WHERE {pred.Column} is not supported");
            src = src.Where(Build<T>(prop, pred));
        }

        return src;
    }

    public static IQueryable<T> Order<T>(IQueryable<T> src, AgentSqlQuery q)
    {
        if (string.IsNullOrEmpty(q.OrderByColumn))
            return src;

        var prop = FindProperty(typeof(T), q.OrderByColumn);
        if (prop == null)
            return src;

        var param = Expression.Parameter(typeof(T), "e");
        var body = Expression.Property(param, prop);
        var delegateType = typeof(Func<,>).MakeGenericType(typeof(T), prop.PropertyType);
        var lambda = Expression.Lambda(delegateType, body, param);
        var method = q.OrderDescending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy);
        var call = Expression.Call(
            typeof(Queryable),
            method,
            [typeof(T), prop.PropertyType],
            src.Expression,
            Expression.Quote(lambda));
        return src.Provider.CreateQuery<T>(call);
    }

    private static PropertyInfo? FindProperty(Type type, string column)
    {
        if (column.Equals("id", StringComparison.OrdinalIgnoreCase))
            return type.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return type.GetProperty(column, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
    }

    private static Expression<Func<T, bool>> Build<T>(PropertyInfo prop, AgentSqlPredicate pred)
    {
        var param = Expression.Parameter(typeof(T), "e");
        var member = Expression.Property(param, prop);
        var body = pred.Op switch
        {
            AgentSqlOp.IsNull => IsNull(member),
            AgentSqlOp.IsNotNull => Expression.Not(IsNull(member)),
            AgentSqlOp.Like => Like(member, pred.Value ?? "", not: false),
            AgentSqlOp.NotLike => Like(member, pred.Value ?? "", not: true),
            AgentSqlOp.In => In(member, pred, not: false),
            AgentSqlOp.NotIn => In(member, pred, not: true),
            AgentSqlOp.Eq => Compare(member, pred.Value, Expression.Equal),
            AgentSqlOp.Neq => Compare(member, pred.Value, Expression.NotEqual),
            AgentSqlOp.Lt => Compare(member, pred.Value, Expression.LessThan),
            AgentSqlOp.Gt => Compare(member, pred.Value, Expression.GreaterThan),
            AgentSqlOp.Lte => Compare(member, pred.Value, Expression.LessThanOrEqual),
            AgentSqlOp.Gte => Compare(member, pred.Value, Expression.GreaterThanOrEqual),
            _ => throw new AgentSqlFilterException($"WHERE {pred.Column} is not supported"),
        };
        return Expression.Lambda<Func<T, bool>>(body, param);
    }

    private static Expression IsNull(Expression member)
    {
        if (member.Type.IsValueType && Nullable.GetUnderlyingType(member.Type) == null)
            return Expression.Constant(false);
        return Expression.Equal(member, Expression.Constant(null, member.Type));
    }

    private static Expression Like(Expression member, string pattern, bool not)
    {
        if (member.Type != typeof(string))
            throw new AgentSqlFilterException("LIKE is only supported on text columns");
        var needle = pattern.Trim('%');
        var contains = Expression.Call(member, nameof(string.Contains), Type.EmptyTypes, Expression.Constant(needle));
        var notNull = Expression.NotEqual(member, Expression.Constant(null, typeof(string)));
        Expression body = Expression.AndAlso(notNull, contains);
        return not ? Expression.Not(body) : body;
    }

    private static Expression In(Expression member, AgentSqlPredicate pred, bool not)
    {
        var values = pred.Values ?? [];
        var listType = typeof(List<>).MakeGenericType(Unwrap(member.Type));
        var list = Activator.CreateInstance(listType)!;
        var add = listType.GetMethod("Add")!;
        foreach (var raw in values)
        {
            if (!TryCoerce(Unwrap(member.Type), raw, out var boxed) || boxed == null)
                continue;
            add.Invoke(list, [boxed]);
        }

        var contains = Expression.Call(Expression.Constant(list), listType.GetMethod("Contains")!, Widen(member));
        return not ? Expression.Not(contains) : contains;
    }

    private static Expression Compare(Expression member, string? raw, Func<Expression, Expression, Expression> op)
    {
        var type = Unwrap(member.Type);
        if (!TryCoerce(type, raw ?? "", out var boxed))
            throw new AgentSqlFilterException("Unsupported WHERE value");
        var constant = Expression.Constant(boxed, member.Type.IsValueType && Nullable.GetUnderlyingType(member.Type) != null
            ? member.Type
            : type);
        var left = member.Type != constant.Type ? Expression.Convert(member, constant.Type) : member;
        if (member.Type != constant.Type && Nullable.GetUnderlyingType(member.Type) == type)
            left = Expression.Convert(member, type);
        return op(Widen(member), Expression.Constant(boxed, Unwrap(member.Type)));
    }

    private static Type Unwrap(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private static Expression Widen(Expression member)
    {
        var under = Nullable.GetUnderlyingType(member.Type);
        return under != null ? Expression.Convert(member, under) : member;
    }

    private static bool TryCoerce(Type type, string raw, out object? value)
    {
        value = null;
        if (type == typeof(string))
        {
            value = raw;
            return true;
        }

        if (type == typeof(int) && int.TryParse(raw, out var i))
        {
            value = i;
            return true;
        }

        if (type == typeof(long) && long.TryParse(raw, out var l))
        {
            value = l;
            return true;
        }

        if (type == typeof(decimal) && decimal.TryParse(raw, out var d))
        {
            value = d;
            return true;
        }

        if (type == typeof(bool) && bool.TryParse(raw, out var b))
        {
            value = b;
            return true;
        }

        if (type == typeof(DateTime) && DateTime.TryParse(raw, out var dt))
        {
            value = dt;
            return true;
        }

        if (type.IsEnum)
        {
            if (int.TryParse(raw, out var n) && Enum.IsDefined(type, n))
            {
                value = Enum.ToObject(type, n);
                return true;
            }

            try
            {
                value = Enum.Parse(type, raw, ignoreCase: true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }
}
