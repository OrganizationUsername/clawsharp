using Clawsharp.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawsharp.Security;

/// <summary>
/// Probes for available sandbox backends at startup and resolves the effective backend.
/// </summary>
public sealed partial class SandboxProbe
{
    private readonly SandboxBackend _effective;

    private readonly string _dockerImage;

    public SandboxProbe(IOptions<AppConfig> options, ILogger<SandboxProbe> logger)
    {
        var toolsConfig = options.Value.Tools;
        var requested = ParseBackend(toolsConfig.Sandbox);
        _dockerImage = toolsConfig.DockerImage;
        _effective = Resolve(requested);

        if (_effective != SandboxBackend.None)
        {
            var image = "N/A (local binary)";
            if (_effective == SandboxBackend.Docker)
            {
                image = _dockerImage;
            }

            LogSandboxBackend(logger, _effective, image);
        }
        else
        {
            LogSandboxNone(logger);
        }
    }

    /// <summary>The resolved sandbox backend to use for shell commands.</summary>
    public SandboxBackend Effective => _effective;

    /// <summary>Docker image to use when backend is Docker.</summary>
    public string DockerImage => _dockerImage;

    /// <summary>
    /// Wrap a shell command for the effective sandbox backend.
    /// Returns the (program, args) tuple to use with ProcessStartInfo.
    /// </summary>
    public (string Program, IReadOnlyList<string> Args) Wrap(string shell, IReadOnlyList<string> shellArgs)
    {
        return _effective switch
        {
            SandboxBackend.Firejail => ("firejail", new[]
            {
                "--private=home", "--private-dev", "--nosound",
                "--no3d", "--novideo", "--nowheel", "--notv",
                "--net=none", // MED-04: Block network access (matches Docker's --network none)
                "--noprofile", "--quiet", "--",
                shell
            }.Concat(shellArgs).ToList()),

            SandboxBackend.Bubblewrap => ("bwrap", new[]
            {
                "--ro-bind", "/usr", "/usr",
                "--ro-bind", "/lib", "/lib",
                "--ro-bind", "/lib64", "/lib64",
                "--ro-bind", "/bin", "/bin",
                "--tmpfs", "/tmp",
                "--proc", "/proc",
                "--dev", "/dev",
                "--unshare-all",
                "--die-with-parent",
                "--",
                shell,
            }.Concat(shellArgs).ToList()),

            SandboxBackend.Docker => ("docker", new[]
            {
                "run", "--rm",
                "--memory", "512m",
                "--cpus", "1.0",
                "--network", "none",
                _dockerImage,
                shell
            }.Concat(shellArgs).ToList()),

            _ => (shell, shellArgs),
        };
    }

    private static SandboxBackend Resolve(SandboxBackend requested) => requested switch
    {
        SandboxBackend.None => SandboxBackend.None,
        SandboxBackend.Bubblewrap when OperatingSystem.IsLinux() && IsBubblewrapAvailable()
            => SandboxBackend.Bubblewrap,
        SandboxBackend.Bubblewrap => SandboxBackend.None,
        SandboxBackend.Firejail when IsFirejailAvailable() => SandboxBackend.Firejail,
        SandboxBackend.Firejail => SandboxBackend.None,
        SandboxBackend.Docker when IsDockerAvailable() => SandboxBackend.Docker,
        SandboxBackend.Docker => SandboxBackend.None,
        SandboxBackend.Auto => ResolveAuto(),
        _ => SandboxBackend.None,
    };

    private static SandboxBackend ResolveAuto()
    {
        if (OperatingSystem.IsLinux())
        {
            if (IsBubblewrapAvailable())
            {
                return SandboxBackend.Bubblewrap;
            }

            if (IsFirejailAvailable())
            {
                return SandboxBackend.Firejail;
            }
        }

        if (IsDockerAvailable())
        {
            return SandboxBackend.Docker;
        }

        return SandboxBackend.None;
    }

    private static bool IsBubblewrapAvailable() => IsToolAvailable("bwrap", "--version", timeoutMs: 3000);

    private static bool IsFirejailAvailable() => IsToolAvailable("firejail", "--version", timeoutMs: 3000);

    private static bool IsDockerAvailable() => IsToolAvailable("docker", "info", timeoutMs: 5000);

    /// <summary>
    /// Probes whether a binary is available by running it with the given arguments
    /// and checking for a zero exit code within the timeout.
    /// </summary>
    private static bool IsToolAvailable(string binary, string args, int timeoutMs)
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = binary,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            p?.WaitForExit(timeoutMs);
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static SandboxBackend ParseBackend(string value) => value.ToLowerInvariant() switch
    {
        "bubblewrap" or "bwrap" => SandboxBackend.Bubblewrap,
        "firejail" => SandboxBackend.Firejail,
        "docker" => SandboxBackend.Docker,
        "none" => SandboxBackend.None,
        _ => SandboxBackend.Auto, // "auto" and anything unrecognized
    };

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Shell sandbox backend: {Backend} (image: {Image})")]
    private static partial void LogSandboxBackend(ILogger logger, SandboxBackend backend, string image);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Shell sandbox: none (direct process execution)")]
    private static partial void LogSandboxNone(ILogger logger);
}