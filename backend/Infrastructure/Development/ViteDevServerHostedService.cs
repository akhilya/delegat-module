using System.Diagnostics;
using System.Net.Sockets;

namespace Delegat.Infrastructure.Development;

/// <summary>
/// Starts Vite (<c>npm run dev</c> on port 5173) alongside the API in Development only.
/// Set <c>SKIP_VITE_DEV_SERVER=1</c> to disable. Skips if 5173 is already listening.
/// </summary>
public sealed class ViteDevServerHostedService : IHostedService, IDisposable
{
    public const int DefaultPort = 5173;

    private readonly IHostEnvironment _env;
    private readonly ILogger<ViteDevServerHostedService> _logger;
    private Process? _process;
    private bool _disposed;

    public ViteDevServerHostedService(
        IHostEnvironment env,
        ILogger<ViteDevServerHostedService> logger)
    {
        _env = env;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment())
            return Task.CompletedTask;

        if (IsTruthy(Environment.GetEnvironmentVariable("SKIP_VITE_DEV_SERVER")))
        {
            _logger.LogInformation("Vite dev server skipped (SKIP_VITE_DEV_SERVER)");
            return Task.CompletedTask;
        }

        if (IsPortOpen(DefaultPort))
        {
            _logger.LogInformation(
                "Vite already listening on http://localhost:{Port} — not starting another process",
                DefaultPort);
            return Task.CompletedTask;
        }

        var frontendDir = ResolveFrontendDirectory(_env.ContentRootPath);
        if (frontendDir is null)
        {
            _logger.LogWarning(
                "Frontend folder not found next to the API (expected ../frontend). Vite will not start.");
            return Task.CompletedTask;
        }

        var npm = OperatingSystem.IsWindows() ? "npm.cmd" : "npm";
        var startInfo = new ProcessStartInfo
        {
            FileName = npm,
            Arguments = $"run dev -- --host 0.0.0.0 --port {DefaultPort} --strictPort",
            WorkingDirectory = frontendDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    _logger.LogDebug("[vite] {Line}", e.Data);
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    _logger.LogDebug("[vite] {Line}", e.Data);
            };
            _process.Exited += (_, _) =>
            {
                if (_process is { HasExited: true } p && p.ExitCode != 0)
                    _logger.LogWarning("Vite process exited with code {Code}", p.ExitCode);
            };

            if (!_process.Start())
            {
                _logger.LogWarning("Failed to start Vite (npm run dev)");
                _process.Dispose();
                _process = null;
                return Task.CompletedTask;
            }

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            _logger.LogInformation(
                "Started Vite dev server (pid {Pid}) → http://localhost:{Port}",
                _process.Id, DefaultPort);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not start Vite dev server. Run `npm run dev` in frontend/ manually.");
            _process?.Dispose();
            _process = null;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopProcess();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopProcess();
    }

    private void StopProcess()
    {
        var process = _process;
        _process = null;
        if (process is null) return;

        try
        {
            if (!process.HasExited)
            {
                _logger.LogInformation("Stopping Vite dev server (pid {Pid})", process.Id);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error while stopping Vite");
        }
        finally
        {
            process.Dispose();
        }
    }

    private static string? ResolveFrontendDirectory(string contentRoot)
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(contentRoot, "..", "frontend")),
            Path.GetFullPath(Path.Combine(contentRoot, "..", "..", "frontend")),
        };

        foreach (var dir in candidates)
        {
            if (File.Exists(Path.Combine(dir, "package.json")))
                return dir;
        }

        return null;
    }

    private static bool IsPortOpen(int port)
    {
        try
        {
            using var client = new TcpClient();
            var connect = client.BeginConnect("127.0.0.1", port, null, null);
            var ok = connect.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(300));
            if (!ok) return false;
            client.EndConnect(connect);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsTruthy(string? raw) =>
        !string.IsNullOrWhiteSpace(raw)
        && (raw.Equals("1", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("on", StringComparison.OrdinalIgnoreCase));
}
