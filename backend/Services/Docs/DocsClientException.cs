namespace Delegat.Services.Docs;

public sealed class DocsClientException : Exception
{
    public string Code { get; }
    public int StatusCode { get; }

    public DocsClientException(string code, int statusCode, string message)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }
}
