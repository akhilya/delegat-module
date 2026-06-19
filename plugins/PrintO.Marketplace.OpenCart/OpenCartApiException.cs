namespace PrintO.Marketplace.OpenCart;

public sealed class OpenCartApiException : Exception
{
    public int StatusCode { get; }
    public string? ResponseBody { get; }

    public OpenCartApiException(int statusCode, string message, string? responseBody = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
