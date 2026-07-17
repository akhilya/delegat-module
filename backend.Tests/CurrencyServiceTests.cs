using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Services;

namespace Delegat.Tests;

public class CurrencyServiceTests
{
    [Theory]
    [InlineData("rub", "RUB")]
    [InlineData(" Usd ", "USD")]
    [InlineData("kzt", "KZT")]
    public void NormalizeCode_TrimsAndUppercases(string input, string expected)
    {
        Assert.Equal(expected, CurrencyService.NormalizeCode(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeCode_Blank_ThrowsBadRequest(string? input)
    {
        var ex = Assert.Throws<ApiException>(() => CurrencyService.NormalizeCode(input!));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void NormalizeCodeOrBase_NullOrBlank_UsesBase()
    {
        Assert.Equal("RUB", CurrencyService.NormalizeCodeOrBase(null, "rub"));
        Assert.Equal("RUB", CurrencyService.NormalizeCodeOrBase("  ", "rub"));
    }

    [Fact]
    public void NormalizeCodeOrBase_ExplicitCode_Wins()
    {
        Assert.Equal("USD", CurrencyService.NormalizeCodeOrBase("usd", "rub"));
    }

    [Theory]
    [InlineData("RUB", true)]
    [InlineData("usd", true)]
    [InlineData("US", false)]
    [InlineData("USDD", false)]
    [InlineData("12A", false)]
    [InlineData("RU1", false)]
    public void IsValidCurrencyCode_ChecksLengthAndLetters(string code, bool expected)
    {
        Assert.Equal(expected, CurrencyService.IsValidCurrencyCode(code));
    }

    [Fact]
    public void BuildCurrencyList_IncludesBaseFirstThenSortedExtras()
    {
        var store = new Store
        {
            baseCurrency = "rub",
            currencies = new List<StoreCurrency>
            {
                new() { code = "USD", rateToBase = 90m, isEnabled = true, updatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                new() { code = "EUR", rateToBase = 100m, isEnabled = false, updatedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc) },
            }
        };

        var list = CurrencyService.BuildCurrencyList(store);

        Assert.Equal(3, list.Count);
        Assert.Equal("RUB", list[0].Code);
        Assert.True(list[0].IsBase);
        Assert.Equal(1m, list[0].RateToBase);
        Assert.Equal("EUR", list[1].Code);
        Assert.False(list[1].IsBase);
        Assert.False(list[1].IsEnabled);
        Assert.Equal("USD", list[2].Code);
        Assert.True(list[2].IsEnabled);
    }
}
