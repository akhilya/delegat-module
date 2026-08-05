using Delegat.Services.Plugins;

namespace Delegat.Tests;

public class PluginLoaderCandidateTests
{
    [Theory]
    [InlineData("Delegat.Marketplace.Ozon.dll", true)]
    [InlineData("Delegat.Marketplace.YandexMarket.dll", true)]
    [InlineData("Delegat.Marketplace.Wildberries.dll", true)]
    [InlineData("Delegat.Marketplace.OpenCart.dll", true)]
    [InlineData("Delegat.Marketplace.SDK.dll", false)]
    [InlineData("delegat.marketplace.sdk.dll", false)]
    [InlineData("Legacy.Something.dll", false)]
    [InlineData("Other.Marketplace.Plugin.dll", false)]
    [InlineData("Delegat.Marketplace.Ozon.exe", false)]
    [InlineData("", false)]
    public void IsCandidatePluginAssemblyFileName_FiltersLegacyAndSdk(string fileName, bool expected)
    {
        Assert.Equal(expected, PluginLoader.IsCandidatePluginAssemblyFileName(fileName));
    }
}
