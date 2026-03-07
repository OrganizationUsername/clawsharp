using System.Collections.Frozen;
using System.Diagnostics;
using System.Text.Json;
using Clawsharp.Security;

using Clawsharp.Tools;
namespace Clawsharp.Tools.Ops;

public sealed class GitTool : Tool
{
    private const int MaxOutputBytes = 100 * 1024; // 100 KB

    private static readonly FrozenSet<string> AllowedOps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "status", "log", "diff", "add", "commit", "branch", "checkout", "stash", "show" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly string _workspace;

    private readonly AuditLogger? _auditLogger;

    public GitTool(string workspace, AuditLogger? auditLogger = null)
    {
        _workspace = Path.GetFullPath(workspace);
        _auditLogger = auditLogger;
    }

    public string? ChannelName => ToolRegistry.CurrentChannelName;

    public override string Name => "git";

    public override string Description =>
        "Run git operations (status, log, diff, add, commit, branch, checkout, show, stash). " +
        "Works in the workspace or a specified repository path.";

    public override string ParametersSchemaJson => """
                                                   {
                                                     "type": "object",
                                                     "properties": {
                                                       "operation": {
                                                         "type": "string",
                                                         "enum": ["status","log","diff","add","commit","branch","checkout","stash","show"],
                                                         "description": "Git operation to run"
                                                       },
                                                       "args": {
                                                         "type": "array", "items": {"type": "string"},
                                                         "description": "Additional git arguments (e.g. [\"--oneline\",\"-10\"] for log)"
                                                       },
                                                       "message": {
                                                         "type": "string",
                                                         "description": "Commit message (required for commit operation)"
                                                       },
                                                       "path": {
                                                         "type": "string",
                                                         "description": "Repository path (default: workspace directory)"
                                                       }
                                                     },
                                                     "required": ["operation"]
                                                   }
                                                   """;

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var operation = args.GetProperty("operation").GetString() ?? "";

        // H8: Server-side allowlist — the JSON schema declares an enum, but the LLM can send anything.
        if (!AllowedOps.Contains(operation))
        {
            return $"Error: operation '{operation}' is not allowed. Allowed: {string.Join(", ", AllowedOps)}";
        }

        var repoPath = args.TryGetProperty("path", out var pathEl)
            ? pathEl.GetString() ?? _workspace
            : _workspace;

        // Expand home dir
        if (repoPath.StartsWith("~/", StringComparison.Ordinal))
        {
            repoPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                repoPath[2..]);
        }

        // Confine repoPath to workspace — prevents LLM from running git in arbitrary directories,
        // including attacker-controlled dirs that could have malicious .git/hooks/ scripts.
        try
        {
            repoPath = PathGuard.SafeResolve(_workspace, repoPath);
        }
        catch (InvalidOperationException ex)
        {
            if (_auditLogger is not null)
            {
                _ = _auditLogger.LogPolicyViolationAsync(
                    $"GitTool path traversal blocked: {repoPath}", ChannelName, ct: ct);
            }

            return $"Error: {ex.Message}";
        }

        if (!Directory.Exists(repoPath))
        {
            return $"Error: directory not found: {repoPath}";
        }

        var gitArgs = new List<string> { operation };

        if (operation.Equals("commit", StringComparison.OrdinalIgnoreCase))
        {
            if (!args.TryGetProperty("message", out var msgEl) || msgEl.GetString() is not { Length: > 0 } msg)
            {
                return "Error: commit requires a 'message' argument.";
            }

            gitArgs.AddRange(["-m", msg]);
        }

        if (args.TryGetProperty("args", out var extraArgs) && extraArgs.ValueKind == JsonValueKind.Array)
        {
            foreach (var arg in extraArgs.EnumerateArray())
            {
                if (arg.GetString() is { } a)
                {
                    gitArgs.Add(a);
                }
            }
        }

        // H8: Block dangerous git flags that could enable arbitrary code execution or global config changes.
        // "--" is blocked to prevent arbitrary pathspec injection (e.g. "-- /etc/passwd").
        string[] blockedArgPrefixes =
        [
            "--force", "-f", "--mirror", "--exec", "--upload-pack", "--receive-pack",
            "--config", "-c", "--global", "--system", "--",
            // HIGH-05: Block directory override flags that bypass workspace confinement
            "--git-dir", "--work-tree", "-C", "--bare", "--namespace"
        ];
        if (gitArgs.Skip(1).Any(a => blockedArgPrefixes.Any(b => a.StartsWith(b, StringComparison.OrdinalIgnoreCase))))
        {
            return "Error: one or more arguments are blocked for security reasons.";
        }

        // Block pathspec arguments that escape the repository via parent-directory traversal.
        if (gitArgs.Skip(1).Any(a => a.StartsWith("..", StringComparison.Ordinal)))
        {
            return "Error: path arguments starting with '..' are not allowed.";
        }

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in gitArgs)
        {
            psi.ArgumentList.Add(a);
        }

        ShellGuard.SanitizeEnvironment(psi);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            using var proc = Process.Start(psi)
                             ?? throw new InvalidOperationException("Failed to start git process.");

            var stdout = await proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = await proc.StandardError.ReadToEndAsync(cts.Token);
            await proc.WaitForExitAsync(cts.Token);

            var combined = (stdout + (stderr.Length > 0 ? $"\n{stderr}" : "")).Trim();
            if (combined.Length > MaxOutputBytes)
            {
                combined = combined[..MaxOutputBytes] + "\n...[truncated]";
            }

            if (_auditLogger is not null)
            {
                _ = _auditLogger.LogCommandAsync(
                    $"git {operation}", ChannelName, userId: null,
                    allowed: true, success: proc.ExitCode == 0, exitCode: proc.ExitCode, ct: ct);
            }

            return combined.Length > 0 ? combined : "(no output)";
        }
        catch (OperationCanceledException)
        {
            return "Error: git command timed out after 30s.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
}