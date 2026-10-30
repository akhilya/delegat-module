namespace Delegat.Services.PlatformLink;

public sealed class PlatformHostedAiRequestException(string code, int statusCode)
    : Exception(code)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
