using System.Reflection;
using Delegat.Controllers;
using Delegat.Infrastructure;
using Delegat.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Delegat.Tests;

public class FeatureFlagsTests
{
    private static IDisposable SetEnv(string name, string? value)
    {
        var previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        return new EnvRestore(name, previous);
    }

    private sealed class EnvRestore(string name, string? previous) : IDisposable
    {
        public void Dispose() => Environment.SetEnvironmentVariable(name, previous);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("no", false)]
    public void IsAiEnabled_ParsesEnv_SelfHost(string? raw, bool expected)
    {
        using var _ai = SetEnv("AI_ENABLED", raw);
        using var _cp = SetEnv("CONTROL_PLANE_URL", null);
        Assert.Equal(expected, FeatureFlags.IsAiEnabled());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void IsAiEnabled_FailClosedWhenControlPlaneLinked(string? raw, bool expected)
    {
        using var _ai = SetEnv("AI_ENABLED", raw);
        using var _cp = SetEnv("CONTROL_PLANE_URL", "https://module.delegat.pro");
        Assert.Equal(expected, FeatureFlags.IsAiEnabled());
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void IsPluginsEnabled_ParsesEnv_SelfHost(string? raw, bool expected)
    {
        using var _plugins = SetEnv("PLUGINS_ENABLED", raw);
        using var _cp = SetEnv("CONTROL_PLANE_URL", null);
        Assert.Equal(expected, FeatureFlags.IsPluginsEnabled());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void IsPluginsEnabled_FailClosedWhenControlPlaneLinked(string? raw, bool expected)
    {
        using var _plugins = SetEnv("PLUGINS_ENABLED", raw);
        using var _cp = SetEnv("CONTROL_PLANE_URL", "https://module.delegat.pro");
        Assert.Equal(expected, FeatureFlags.IsPluginsEnabled());
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void IsApiUsageEnabled_ParsesEnv_SelfHost(string? raw, bool expected)
    {
        using var _api = SetEnv("API_USAGE_ENABLED", raw);
        using var _cp = SetEnv("CONTROL_PLANE_URL", null);
        Assert.Equal(expected, FeatureFlags.IsApiUsageEnabled());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void IsApiUsageEnabled_FailClosedWhenControlPlaneLinked(string? raw, bool expected)
    {
        using var _api = SetEnv("API_USAGE_ENABLED", raw);
        using var _cp = SetEnv("CONTROL_PLANE_URL", "https://module.delegat.pro");
        Assert.Equal(expected, FeatureFlags.IsApiUsageEnabled());
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    public void IsErrorLoggingEnabled_DefaultsTrue(string? raw, bool expected)
    {
        using var _ = SetEnv("LOG_ERRORS_ENABLED", raw);
        Assert.Equal(expected, FeatureFlags.IsErrorLoggingEnabled());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    [InlineData("1", true)]
    public void IsApiRequestLoggingEnabled_DefaultsFalse(string? raw, bool expected)
    {
        using var _ = SetEnv("LOG_API_REQUESTS_ENABLED", raw);
        Assert.Equal(expected, FeatureFlags.IsApiRequestLoggingEnabled());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    [InlineData("on", true)]
    public void IsAdapterHttpLoggingEnabled_DefaultsFalse(string? raw, bool expected)
    {
        using var _ = SetEnv("LOG_ADAPTER_HTTP_ENABLED", raw);
        Assert.Equal(expected, FeatureFlags.IsAdapterHttpLoggingEnabled());
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    public void IsPublicApiEnabled_DefaultsTrue(string? raw, bool expected)
    {
        using var _ = SetEnv("PUBLIC_API_ENABLED", raw);
        Assert.Equal(expected, FeatureFlags.IsPublicApiEnabled());
    }

    [Fact]
    public void MaxBufferedDownloadBytes_Is32MiB()
    {
        Assert.Equal(32L * 1024 * 1024, Delegat.Infrastructure.Storage.MinIOStorageService.MaxBufferedDownloadBytes);
    }

    [Theory]
    [InlineData(null, 7)]
    [InlineData("0", 7)]
    [InlineData("nope", 7)]
    [InlineData("1", 1)]
    [InlineData("30", 30)]
    [InlineData("365", 365)]
    [InlineData("999", 365)] // clamped
    public void GetQueueRetentionDays_ParsesAndClamps(string? raw, int expected)
    {
        using var _ = SetEnv("QUEUE_RETENTION_DAYS", raw);
        Assert.Equal(expected, FeatureFlags.GetQueueRetentionDays());
    }

    [Fact]
    public void AiControllersConvention_RemovesAiControllers_WhenDisabled()
    {
        var application = new ApplicationModel();
        application.Controllers.Add(MakeController(typeof(AiController)));
        application.Controllers.Add(MakeController(typeof(AiConversationController)));
        application.Controllers.Add(MakeController(typeof(AiSettingsController)));
        application.Controllers.Add(MakeController(typeof(AgentController)));
        application.Controllers.Add(MakeController(typeof(AgentMcpController)));
        application.Controllers.Add(MakeController(typeof(ProductController)));

        new AiControllersConvention(aiEnabled: false).Apply(application);

        Assert.Single(application.Controllers);
        Assert.Equal(nameof(ProductController), application.Controllers[0].ControllerType.Name);
    }

    [Fact]
    public void AiControllersConvention_KeepsAiControllers_WhenEnabled()
    {
        var application = new ApplicationModel();
        application.Controllers.Add(MakeController(typeof(AiController)));
        application.Controllers.Add(MakeController(typeof(ProductController)));

        new AiControllersConvention(aiEnabled: true).Apply(application);

        Assert.Equal(2, application.Controllers.Count);
    }

    private static ControllerModel MakeController(Type type) =>
        new(type.GetTypeInfo(), Array.Empty<object>())
        {
            ControllerName = type.Name.EndsWith("Controller", StringComparison.Ordinal)
                ? type.Name[..^"Controller".Length]
                : type.Name,
        };
}
