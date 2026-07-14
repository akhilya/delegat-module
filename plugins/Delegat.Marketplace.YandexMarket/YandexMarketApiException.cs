namespace Delegat.Marketplace.YandexMarket;

public class YandexMarketApiException : Exception
{
    public int StatusCode { get; }
    public string? ResponseBody { get; }

    public YandexMarketApiException(string message, int statusCode, string? responseBody, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}