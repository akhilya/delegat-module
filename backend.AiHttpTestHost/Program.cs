namespace Delegat.AiHttpTestHost;

/// <summary>
/// Test-only CLI host for the browser-to-real-API durable-turn integration
/// spec. It serves a built frontend and the shared test fixture's real AI
/// controllers/worker over one caller-supplied file database. It can never
/// start the production Program and only operates inside a parent-created
/// state directory under the OS temp root.
/// </summary>
internal static class Program
{
    private static readonly HashSet<string> PauseModes =
        new(StringComparer.Ordinal) { "none", "first-done", "first-running", "review-text" };

    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 5)
            return Fail("usage: Delegat.AiHttpTestHost <stateDirectory> <staticRoot> <sessionId> <port> <pauseMode>");

        var stateDirectory = Path.GetFullPath(args[0]);
        var staticRoot = Path.GetFullPath(args[1]);
        var sessionId = args[2];
        var pauseMode = args[4];

        if (string.IsNullOrEmpty(sessionId) || sessionId.Length > 36)
            return Fail("sessionId must be 1..36 characters");
        if (!int.TryParse(args[3], out var port) || port < 0 || port > 65535)
            return Fail("port must be an integer in 0..65535");
        if (!PauseModes.Contains(pauseMode))
            return Fail($"pauseMode must be one of: {string.Join(", ", PauseModes.Order())}");
        if (!File.Exists(Path.Combine(staticRoot, "index.html")))
            return Fail($"staticRoot has no index.html: {staticRoot}");

        // The state directory must be a plain parent-created child of the OS
        // temp root; the host never creates or deletes directories. The name
        // is constrained to the exact fs.mkdtemp shape so a basename cannot
        // smuggle connection-string separators into the SQLite DSN.
        var tempRoot = Path.GetFullPath(Path.GetTempPath().TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var parent = Path.GetFullPath(Path.GetDirectoryName(stateDirectory)
            ?? Path.Combine(tempRoot, ".."));
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var name = Path.GetFileName(stateDirectory);
        const string prefix = "delegat-ai-browser-";
        var validName = name.StartsWith(prefix, StringComparison.Ordinal)
            && name.Length > prefix.Length
            && name[prefix.Length..].All(char.IsAsciiLetterOrDigit);
        if (!string.Equals(parent, tempRoot, pathComparison) || !validName)
            return Fail($"stateDirectory must be a direct delegat-ai-browser-* child of {tempRoot}");
        if (!Directory.Exists(stateDirectory))
            return Fail($"stateDirectory does not exist: {stateDirectory}");
        if ((File.GetAttributes(stateDirectory) & FileAttributes.ReparsePoint) != 0)
            return Fail("stateDirectory must not be a reparse point or symlink");

        var tokenPath = Path.Combine(stateDirectory, "owner.token");
        if (!File.Exists(tokenPath))
            return Fail($"missing owner token: {tokenPath}");
        var token = (await File.ReadAllTextAsync(tokenPath)).Trim();
        if (token.Length < 32)
            return Fail("owner.token must contain at least 32 characters");

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdown.Cancel();
        };

        try
        {
            await Tests.AiDurableTurnHttpTests.RunBrowserHostAsync(
                stateDirectory, staticRoot, sessionId, port, pauseMode, token, shutdown.Token);
            return 0;
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.ToString());
            return 1;
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
