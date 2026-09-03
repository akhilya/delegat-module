using Delegat.Marketplace.SDK.DTOs;

namespace Delegat.Tests;

public class OrderLabelPayloadTests
{
    [Fact]
    public void Decode_Empty_ReturnsEmpty()
    {
        Assert.Empty(OrderLabelPayload.Decode(null));
        Assert.Empty(OrderLabelPayload.Decode(""));
        Assert.Empty(OrderLabelPayload.Decode("   "));
    }

    [Fact]
    public void Decode_RawPdf_KeepsLatin1Bytes()
    {
        var bytes = OrderLabelPayload.Decode("%PDF-1.4");
        Assert.Equal("%PDF-1.4"u8.ToArray(), bytes);
    }

    [Fact]
    public void Decode_Base64_RoundTrips()
    {
        var source = new byte[] { 1, 2, 3, 4 };
        Assert.Equal(source, OrderLabelPayload.Decode(Convert.ToBase64String(source)));
    }
}
