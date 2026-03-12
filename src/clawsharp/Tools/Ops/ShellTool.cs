using System.Diagnostics;
using System.Text.Json;
using Clawsharp.Security;

namespace Clawsharp.Tools.Ops;

public sealed class ShellTool : Tool
{
    private readonly bool _enableDenyPatterns;

    private readonly IReadOnlyList<string>? _customDenyPatterns;

    private readonly bool _requireApproval;

    private readonly IReadOnlyList<string>? _requireApprovalPatterns;

    private readonly IReadOnlyList<string>? _autoApprovePatterns;

    private readonly string _workspace;

    private readonly AuditLogger? _auditLogger;

    private readonly SandboxProbe? _sandboxProbe;

    public ShellTool(string workspace, bool requireApproval = false,
                     bool enableDenyPatterns = true, IReadOnlyList<string>? customDenyPatterns = null,
                     AuditLogger? auditLogger = null, SandboxProbe? sandboxProbe = null,
                     IReadOnlyList<string>? requireApprovalPatterns = null,
                     IReadOnlyList<string>? autoApprovePatterns = null)
    {
        _workspace = workspace;
        Directory.CreateDirectory(workspace);
        _requireApproval = requireApproval;
        _enableDenyPatterns = enableDenyPatterns;
        _customDenyPatterns = customDenyPatterns;
        _auditLogger = auditLogger;
        _sandboxProbe = sandboxProbe;
        _requireApprovalPatterns = requireApprovalPatterns;
        _autoApprovePatterns = autoApprovePatterns;
    }

    /// <summary>Originating channel name, read from the per-async-flow AsyncLocal in ToolRegistry.</summary>
    public string? ChannelName => ToolRegistry.CurrentChannelName;

    public override string Name => "shell";

    public override ToolSensitivity Sensitivity => ToolSensitivity.High;

    public override string Description =>
        "Execute a shell command in the workspace directory. Use for running scripts, build commands, and system operations.";

    public override string ParametersSchemaJson => """
                                                   {
                                                     "type": "object",
                                                     "properties": {
                                                       "command": { "type": "string", "description": "The shell command to execute" },
                                                       "timeout": { "type": "integer", "description": "Timeout in seconds (default 30, max 120)" }
                                                     },
                                                     "required": ["command"]
                                                   }
                                                   """;

    public override async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct = default)
    {
        var command = arguments.TryGetProperty("command", out var cmdProp) ? cmdProp.GetString() ?? "" : "";
        var timeout = arguments.TryGetProperty("timeout", out var toProp) && toProp.TryGetInt32(out var toVal)
            ? Math.Min(toVal, 120)
            : 30;

        if (string.IsNullOrWhiteSpace(command))
        {
            return "Error: no command provided.";
        }

        // Safety guard: check command against deny-list patterns.
        // Non-CLI channels also get network egress detection to prevent data exfiltration.
        if (_enableDenyPatterns)
        {
            var blocked = ChannelName is not (null or "cli")
                ? ShellGuard.CheckCommandWithEgress(command, _customDenyPatterns)
                : ShellGuard.CheckCommand(command, _customDenyPatterns);
            if (blocked is not null)
            {
                if (_auditLogger is not null)
                {
                    _ = _auditLogger.LogPolicyViolationAsync($"ShellGuard denied: {blocked}", ChannelName, ct: ct);
                }

                return $"[shell] {blocked}";
            }
        }

        // Command-level approval check: certain commands require explicit approval
        // even when the shell tool itself is auto-approved.
        var matchedPattern = ShellGuard.RequiresApproval(command, _requireApprovalPatterns, _autoApprovePatterns);
        if (matchedPattern is not null && ChannelName is not (null or "cli"))
        {
            if (_auditLogger is not null)
            {
                _ = _auditLogger.LogCommandAsync(command, ChannelName, userId: null, allowed: false, success: false,
                    error: $"Requires approval (pattern: {matchedPattern})", ct: ct);
            }

            return $"[shell] Command requires approval: '{command}' matched approval pattern '{matchedPattern}'. " +
                   "Use the CLI channel to execute this command interactively, or add the pattern to security.autoApprovePatterns to bypass.";
        }

        if (_requireApproval)
        {
            if (ChannelName is not (null or "cli"))
            {
                // Non-interactive channels cannot prompt for approval — auto-deny
                if (_auditLogger is not null)
                {
                    _ = _auditLogger.LogCommandAsync(command, ChannelName, userId: null, allowed: false, success: false,
                        error: "Non-interactive channel auto-deny", ct: ct);
                }

                return
                    "[shell] Shell execution is disabled on non-interactive channels. Set requireShellApproval=false in config to allow.";
            }

            Console.Error.Write($"[shell] Allow command? {command}\n[y/N]: ");
            var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (answer != "y" && answer != "yes")
            {
                if (_auditLogger is not null)
                {
                    _ = _auditLogger.LogCommandAsync(command, ChannelName, userId: null, allowed: false, success: false,
                        error: "User rejected", ct: ct);
                }

                return "[shell] Command rejected by user.";
            }
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeout));

        var (shell, shellArgs) = OperatingSystem.IsWindows()
            ? ("cmd.exe", (IReadOnlyList<string>)["/c", command])
            : BuildPosixShellArgs(command);

        static (string Shell, IReadOnlyList<string> Args) BuildPosixShellArgs(string command)
        {
            // Detect the user's shell; default to /bin/sh.
            var userShell = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/sh";
            var shellName = Path.GetFileName(userShell);

            // Skip shell startup files to avoid user profile side-effects (MED-08).
            return shellName switch
            {
                "bash" => (userShell, (IReadOnlyList<string>)["--norc", "--noprofile", "-c", command]),
                "zsh" => (userShell, ["--no-rcs", "-c", command]),
                _ => (userShell, ["-c", command]),
            };
        }

        // Wrap with sandbox backend if available (Firejail, Docker, or passthrough)
        var (program, wrappedArgs) = _sandboxProbe is not null
            ? _sandboxProbe.Wrap(shell, shellArgs)
            : (shell, shellArgs);
        var sandboxName = _sandboxProbe?.Effective.ToString().ToLowerInvariant();

        var psi = new ProcessStartInfo
        {
            FileName = program,
            WorkingDirectory = _workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in wrappedArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        // Sanitize environment: only pass safe variables (no API keys, secrets, etc.)
        ShellGuard.SanitizeEnvironment(psi);

        var timestamp = Stopwatch.GetTimestamp();
        using var proc = new Process { StartInfo = psi };
        proc.Start();

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);

        try
        {
            await proc.WaitForExitAsync(cts.Token);
            var elapsed = Stopwatch.GetElapsedTime(timestamp);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var output = string.IsNullOrEmpty(stderr) ? stdout : $"{stdout}\n[stderr]\n{stderr}";
            // Truncate to 100 KB (global cap in ToolRegistry is the final safety net)
            const int maxChars = 102_400;
            if (output.Length > maxChars)
            {
                output = output[..maxChars] + "\n... (truncated)";
            }

            if (_auditLogger is not null)
            {
                _ = _auditLogger.LogCommandAsync(command, ChannelName, userId: null, allowed: true,
                    success: proc.ExitCode == 0, exitCode: proc.ExitCode, durationMs: (long)elapsed.TotalMilliseconds,
                    sandboxBackend: sandboxName, ct: ct);
            }

            return output.TrimEnd();
        }
        catch (OperationCanceledException)
        {
            var elapsed = Stopwatch.GetElapsedTime(timestamp);
            try
            {
                proc.Kill(true);
            }
            catch
            {
                /* ignore */
            }

            if (_auditLogger is not null)
            {
                _ = _auditLogger.LogCommandAsync(command, ChannelName, userId: null, allowed: true,
                    success: false, durationMs: (long)elapsed.TotalMilliseconds, error: $"Timed out after {timeout}s",
                    sandboxBackend: sandboxName, ct: CancellationToken.None);
            }

            return $"Error: command timed out after {timeout}s.";
        }
    }
}