namespace Delegat.Services;

/// <summary>
/// Product mention in an operation log. The cabinet turns <c>product #id «name»</c> into a link.
/// </summary>
public static class OperationProductRef
{
    public static string Format(int productId, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return $"product #{productId}";
        var safe = name.Replace("»", "›", StringComparison.Ordinal);
        return $"product #{productId} «{safe}»";
    }
}
