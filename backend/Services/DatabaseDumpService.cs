using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Delegat.Infrastructure.Configuration;
using Delegat.Infrastructure.Middleware;

namespace Delegat.Services;

public sealed class DatabaseDumpService : IDatabaseDumpService
{
    private readonly MySqlSettings _settings;
    private readonly ILogger<DatabaseDumpService> _logger;
    private readonly string _executable;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DatabaseDumpService(MySqlSettings settings, ILogger<DatabaseDumpService> logger)
        : this(settings, logger, "mysqldump")
    {
    }

    /// <summary>Test/internal constructor to override the mysqldump executable path.</summary>
    internal DatabaseDumpService(MySqlSettings settings, ILogger<DatabaseDumpService> logger, string executable)
    {
        _settings = settings;
        _logger = logger;
        _executable = executable;
    }

    public async Task<DatabaseDumpHandle> CreateDumpAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
            throw ApiException.Conflict("A database dump is already in progress", code: "dump_in_progress");

        var released = false;
        void ReleaseOnce()
        {
            if (released) return;
            released = true;
            _gate.Release();
        }

        string? cnfPath = null;
        string? dumpPath = null;

        try
        {
            EnsureExecutableAvailable();

            if (string.IsNullOrWhiteSpace(_settings.Database))
                throw ApiException.InternalServerError("MySQL database name is not configured", code: "dump_failed");

            var fileName = $"delegat-{DateTime.UtcNow:yyyyMMdd-HHmmss}.sql";
            cnfPath = Path.Combine(Path.GetTempPath(), $"delegat-mysqldump-{Guid.NewGuid():N}.cnf");
            dumpPath = Path.Combine(Path.GetTempPath(), $"delegat-dump-{Guid.NewGuid():N}.sql");

            await File.WriteAllTextAsync(cnfPath, BuildDefaultsExtraFile(), Encoding.UTF8, cancellationToken);
            RestrictFilePermissions(cnfPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = _executable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add($"--defaults-extra-file={cnfPath}");
            startInfo.ArgumentList.Add("--single-transaction");
            startInfo.ArgumentList.Add("--routines");
            startInfo.ArgumentList.Add("--triggers");
            startInfo.ArgumentList.Add("--events");
            startInfo.ArgumentList.Add("--hex-blob");
            // App DB users lack global PROCESS; without this MySQL 8.0.21+ fails dumping tablespaces.
            startInfo.ArgumentList.Add("--no-tablespaces");
            startInfo.ArgumentList.Add("--default-character-set=utf8mb4");
            startInfo.ArgumentList.Add("--databases");
            startInfo.ArgumentList.Add(_settings.Database);

            using var process = Process.Start(startInfo)
                ?? throw ApiException.InternalServerError("Failed to start mysqldump", code: "dump_failed");

            await using (var dumpStream = new FileStream(dumpPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var stdoutCopy = process.StandardOutput.BaseStream.CopyToAsync(dumpStream, cancellationToken);
                var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
                await stdoutCopy;
                var stderr = await stderrTask;
                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode != 0)
                {
                    _logger.LogError(
                        "mysqldump exited with code {ExitCode} for database {Database}@{Server}:{Port} as {User}. Stderr: {Stderr}",
                        process.ExitCode,
                        _settings.Database,
                        _settings.Server,
                        _settings.Port,
                        _settings.User,
                        SanitizeStderr(stderr, _settings.Password));
                    throw new ApiException(
                        "Database dump failed. Check that MySQL is reachable and credentials are valid.",
                        StatusCodes.Status500InternalServerError,
                        code: "dump_failed");
                }
            }

            // Credentials file no longer needed once dump finished
            TryDelete(cnfPath);
            cnfPath = null;

            var pathForHandle = dumpPath;
            dumpPath = null; // ownership transferred to handle
            return new DatabaseDumpHandle(pathForHandle, fileName, ReleaseOnce);
        }
        catch
        {
            TryDelete(dumpPath);
            ReleaseOnce();
            throw;
        }
        finally
        {
            TryDelete(cnfPath);
        }
    }

    private void EnsureExecutableAvailable()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _executable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("--version");

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                throw CreateUnavailableException();
            }

            if (!process.WaitForExit(10_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                throw CreateUnavailableException();
            }
        }
        catch (ApiException)
        {
            throw;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "mysqldump executable not available at '{Executable}'", _executable);
            throw CreateUnavailableException();
        }
    }

    private static ApiException CreateUnavailableException() =>
        new(
            "mysqldump was not found. Install the MySQL client tools, or use the Docker image which includes them.",
            StatusCodes.Status503ServiceUnavailable,
            code: "mysqldump_unavailable");

    private string BuildDefaultsExtraFile()
    {
        // Password must not appear on the process command line.
        var password = EscapeCnfValue(_settings.Password);
        var sb = new StringBuilder();
        sb.AppendLine("[client]");
        sb.Append("host=").AppendLine(_settings.Server);
        sb.Append("port=").AppendLine(_settings.Port.ToString());
        sb.Append("user=").AppendLine(_settings.User);
        sb.Append("password=").Append('"').Append(password).Append('"').AppendLine();
        return sb.ToString();
    }

    private static string EscapeCnfValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    /// <summary>Redact secrets from mysqldump stderr before logging.</summary>
    internal static string SanitizeStderr(string stderr, string? password)
    {
        if (string.IsNullOrEmpty(stderr)) return string.Empty;
        var sanitized = stderr.Trim();
        if (!string.IsNullOrEmpty(password))
            sanitized = sanitized.Replace(password, "***", StringComparison.Ordinal);
        const int maxLen = 2000;
        if (sanitized.Length > maxLen)
            sanitized = sanitized[..maxLen] + "…";
        return sanitized;
    }

    private static void RestrictFilePermissions(string path)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception)
            {
                // Best-effort on platforms without chmod support
            }
        }
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}
