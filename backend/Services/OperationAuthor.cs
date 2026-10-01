namespace Delegat.Services;

/// <summary>
/// Cabinet label for who started an operation.
/// </summary>
public static class OperationAuthor
{
    public static string ApiKey(string? name, string? keyPrefix, string? last4)
    {
        var hint = string.IsNullOrEmpty(last4) ? null : $"{keyPrefix}…{last4}";
        if (string.IsNullOrWhiteSpace(name))
            return hint == null ? "API" : $"API ({hint})";
        return hint == null ? $"API · {name.Trim()}" : $"API · {name.Trim()} ({hint})";
    }
}
