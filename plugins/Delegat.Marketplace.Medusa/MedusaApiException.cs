namespace Delegat.Marketplace.Medusa;

public sealed class MedusaApiException : Exception
{
    public int StatusCode { get; }
    public string? ResponseBody { get; }

    public MedusaApiException(string message, int statusCode, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
