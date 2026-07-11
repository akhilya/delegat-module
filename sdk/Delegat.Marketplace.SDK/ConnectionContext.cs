using Microsoft.Extensions.Logging;

namespace Delegat.Marketplace.SDK;

/// <summary>
/// Connection context passed to plugin methods.
/// Contains credentials and settings needed to communicate with the marketplace API.
/// </summary>
public class ConnectionContext
{
    /// <summary>
    /// Connection ID in the system
    /// </summary>
    public int ConnectionId { get; set; }

    /// <summary>
    /// Credentials as key-value pairs (apiKey, clientId, etc.)
    /// </summary>
    public Dictionary<string, string> Credentials { get; set; } = new();

    /// <summary>
    /// Additional settings as key-value pairs
    /// </summary>
    public Dictionary<string, string> Settings { get; set; } = new();

    /// <summary>
    /// Logger provided by the host for structured logging.
    /// Plugins should use this instead of Console.WriteLine.
    /// </summary>
    public ILogger? Logger { get; set; }

    /// <summary>
    /// Get credential value by key
    /// </summary>
    public string? GetCredential(string key)
    {
        return Credentials.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Get setting value by key
    /// </summary>
    public string? GetSetting(string key)
    {
        return Settings.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Get setting value by key with default
    /// </summary>
    public string GetSetting(string key, string defaultValue)
    {
        return Settings.TryGetValue(key, out var value) ? value : defaultValue;
    }
}
