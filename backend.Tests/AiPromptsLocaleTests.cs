using Delegat.Services.Ai.Prompts;

namespace Delegat.Tests;

/// <summary>
/// Locale helpers drive X-UI-Locale → prompt language. Wrong defaults flip AI
/// replies between English and Russian for every chat/explain/description call.
/// </summary>
public class AiPromptsLocaleTests
{
    [Theory]
    [InlineData(null, "ru")]
    [InlineData("", "ru")]
    [InlineData("   ", "ru")]
    [InlineData("ru", "ru")]
    [InlineData("ru-RU", "ru")]
    [InlineData("de", "ru")]
    [InlineData("fr-FR", "ru")]
    [InlineData("en", "en")]
    [InlineData("en-US", "en")]
    [InlineData("EN", "en")]
    [InlineData("En-Gb", "en")]
    public void NormalizeLocale_MapsToSupportedCode(string? input, string expected)
    {
        Assert.Equal(expected, AiPrompts.NormalizeLocale(input));
    }

    [Theory]
    [InlineData(null, "Russian")]
    [InlineData("ru", "Russian")]
    [InlineData("de-DE", "Russian")]
    [InlineData("en", "English")]
    [InlineData("en-US", "English")]
    public void LanguageName_MatchesNormalizedLocale(string? input, string expected)
    {
        Assert.Equal(expected, AiPrompts.LanguageName(input));
    }

    [Fact]
    public void ExplainErrorSystem_IncludesResolvedLanguage()
    {
        Assert.Contains("English", AiPrompts.ExplainErrorSystem("en"));
        Assert.Contains("Russian", AiPrompts.ExplainErrorSystem("ru"));
        Assert.Contains("Russian", AiPrompts.ExplainErrorSystem(null));
    }
}
