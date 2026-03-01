namespace PrintO.Marketplace.Wildberries;

public class WildberriesApiException : Exception
{
    public int StatusCode { get; }
    public string? ResponseBody { get; }

    public WildberriesApiException(string message, int statusCode, string? responseBody, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
