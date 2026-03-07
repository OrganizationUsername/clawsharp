using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Clawsharp.Security;

/// <summary>
///     Shell command safety guard — deny-list patterns and environment variable sanitization.
///     Ported from picoclaw's 42 regex deny patterns + nullclaw's env whitelist.
/// </summary>
public static partial class ShellGuard
{
    /// <summary>
    ///     Environment variables safe to pass to subprocess. All others are stripped
    ///     to prevent leaking API keys, secrets, and credentials.
    /// </summary>
    private static readonly HashSet<string> SafeEnvVars = new(StringComparer.OrdinalIgnoreCase)
    {
        // POSIX
        "PATH", "HOME", "TERM", "LANG", "LC_ALL", "LC_CTYPE", "USER", "SHELL", "TMPDIR",
        // Windows
        "USERPROFILE", "APPDATA", "LOCALAPPDATA", "TEMP", "TMP", "SYSTEMROOT", "WINDIR",
        "COMSPEC", "PATHEXT"
    };

    /// <summary>Maximum command length (16 KB, from nullclaw).</summary>
    private const int MaxCommandLength = 16_384;

    private static readonly TimeSpan CustomPatternTimeout = TimeSpan.FromMilliseconds(100);

    private static volatile Regex[]? _compiledDenyPatterns;

    private static volatile Regex[]? _compiledApprovalPatterns;

    private static volatile Regex[]? _compiledAutoApprovePatterns;

    public static void ConfigureCustomPatterns(
        IReadOnlyList<string>? denyPatterns,
        IReadOnlyList<string>? approvalPatterns,
        IReadOnlyList<string>? autoApprovePatterns,
        ILogger? logger = null)
    {
        _compiledDenyPatterns = CompilePatterns(denyPatterns, logger);
        _compiledApprovalPatterns = CompilePatterns(approvalPatterns, logger);
        _compiledAutoApprovePatterns = CompilePatterns(autoApprovePatterns, logger);
    }

    private static Regex[]? CompilePatterns(IReadOnlyList<string>? patterns, ILogger? logger = null)
    {
        if (patterns is not { Count: > 0 }) return null;
        var compiled = new List<Regex>(patterns.Count);
        foreach (var p in patterns)
        {
            try
            {
                compiled.Add(new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled, CustomPatternTimeout));
            }
            catch (Exception ex)
            {
                LogInvalidCustomPattern(logger, p, ex);
            }
        }

        return compiled.Count > 0 ? compiled.ToArray() : null;
    }

    /// <summary>
    ///     Check a command against the deny-list. Returns an error message if blocked, null if safe.
    /// </summary>
    public static string? CheckCommand(string command, IReadOnlyList<string>? customDenyPatterns = null)
    {
        if (command.Length > MaxCommandLength)
        {
            return $"Command blocked: exceeds maximum length of {MaxCommandLength} characters.";
        }

        // Phase 1: Reject commands with newline/control characters (injection prevention)
        if (ContainsNewlineOrControlChar(command))
        {
            return "Command blocked: contains newline or control characters.";
        }

        // Phase 2: Reject null bytes (truncation attacks)
        if (command.Contains('\0'))
        {
            return "Command blocked: contains null byte.";
        }

        // Phase 3: Run deny patterns against raw command
        var rawResult = RunDenyPatterns(command);
        if (rawResult is not null) return rawResult;

        var rawCustomResult = RunCustomDenyPatterns(command, customDenyPatterns);
        if (rawCustomResult is not null) return rawCustomResult;

        // Phase 4: Normalize and re-check if normalization changed anything
        var normalized = NormalizeCommand(command);
        if (!string.Equals(normalized, command, StringComparison.OrdinalIgnoreCase))
        {
            var normResult = RunDenyPatterns(normalized);
            if (normResult is not null) return normResult;

            var normCustomResult = RunCustomDenyPatterns(normalized, customDenyPatterns);
            if (normCustomResult is not null) return normCustomResult;
        }

        return null;
    }

    private static bool ContainsNewlineOrControlChar(string command)
    {
        foreach (var c in command)
        {
            if (c is '\n' or '\r' or '\v' or '\f' or '\x85' or '\u2028' or '\u2029')
                return true;
        }

        return false;
    }

    private static string NormalizeCommand(string command)
    {
        var stripped = StripShellQuotes().Replace(command, "");
        stripped = CollapseBackslashEscapes().Replace(stripped, "$1");
        stripped = StripBinaryPaths().Replace(stripped, "");
        return stripped;
    }

    private static string? RunDenyPatterns(string command)
    {
        for (var i = 0; i < DenyPatterns.Length; i++)
        {
            if (DenyPatterns[i].IsMatch(command))
            {
                return $"Command blocked by safety guard (pattern {i + 1}: {DenyCategories[i]}).";
            }
        }

        return null;
    }

    private static string? RunCustomDenyPatterns(string command, IReadOnlyList<string>? customDenyPatterns)
    {
        // Prefer pre-compiled patterns if ConfigureCustomPatterns was called.
        var compiled = _compiledDenyPatterns;
        if (compiled is not null)
        {
            foreach (var regex in compiled)
            {
                try
                {
                    if (regex.IsMatch(command))
                    {
                        return $"Command blocked by custom deny pattern: {regex}";
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    // LOW-01: Block on timeout rather than allowing through — an attacker
                    // could craft ReDoS input to disable a specific deny rule.
                    return $"Command blocked: custom deny pattern timed out (potential ReDoS): {regex}";
                }
            }

            return null;
        }

        // Fall back to ad-hoc pattern list (legacy path, before ConfigureCustomPatterns).
        if (customDenyPatterns is not { Count: > 0 })
        {
            return null;
        }

        foreach (var pattern in customDenyPatterns)
        {
            try
            {
                if (Regex.IsMatch(command, pattern, RegexOptions.IgnoreCase, CustomPatternTimeout))
                {
                    return $"Command blocked by custom deny pattern: {pattern}";
                }
            }
            catch
            {
                // Invalid custom regex — skip silently
            }
        }

        return null;
    }

    [GeneratedRegex(@"['""]", RegexOptions.IgnoreCase)]
    private static partial Regex StripShellQuotes();

    [GeneratedRegex(@"\\(\S)", RegexOptions.IgnoreCase)]
    private static partial Regex CollapseBackslashEscapes();

    [GeneratedRegex(@"(?:/usr/local/|/usr/|/)s?bin/", RegexOptions.IgnoreCase)]
    private static partial Regex StripBinaryPaths();

    // ── Built-in approval patterns (always checked) ────────────────────────
    private static readonly Regex[] BuiltInApprovalPatterns =
    [
        ApprovalGitPush(),
        ApprovalRm(),
        ApprovalGitReset(),
        ApprovalDocker()
    ];

    private static readonly string[] ApprovalPatternDescriptions =
    [
        @"\bgit\s+push\b",
        @"\brm\s+",
        @"\bgit\s+reset\b",
        @"\bdocker\s+"
    ];

    /// <summary>
    ///     Check if a command requires interactive approval. Returns the matched pattern or null.
    ///     Built-in patterns are always checked; additional custom patterns from config are appended.
    ///     If the command matches an auto-approve pattern, the approval check is skipped.
    /// </summary>
    public static string? RequiresApproval(
        string command,
        IReadOnlyList<string>? approvalPatterns,
        IReadOnlyList<string>? autoApprovePatterns)
    {
        var compiledAutoApprove = _compiledAutoApprovePatterns;
        if (compiledAutoApprove is not null)
        {
            foreach (var regex in compiledAutoApprove)
            {
                try
                {
                    if (regex.IsMatch(command)) return null;
                }
                catch
                {
                    /* Timeout */
                }
            }
        }
        else if (autoApprovePatterns is { Count: > 0 })
        {
            foreach (var pattern in autoApprovePatterns)
            {
                try
                {
                    if (Regex.IsMatch(command, pattern, RegexOptions.IgnoreCase, CustomPatternTimeout))
                        return null;
                }
                catch
                {
                    /* Invalid regex */
                }
            }
        }

        for (var i = 0; i < BuiltInApprovalPatterns.Length; i++)
        {
            if (BuiltInApprovalPatterns[i].IsMatch(command))
            {
                return ApprovalPatternDescriptions[i];
            }
        }

        var compiledApproval = _compiledApprovalPatterns;
        if (compiledApproval is not null)
        {
            foreach (var regex in compiledApproval)
            {
                try
                {
                    if (regex.IsMatch(command)) return regex.ToString();
                }
                catch
                {
                    /* Timeout */
                }
            }
        }
        else if (approvalPatterns is { Count: > 0 })
        {
            foreach (var pattern in approvalPatterns)
            {
                try
                {
                    if (Regex.IsMatch(command, pattern, RegexOptions.IgnoreCase, CustomPatternTimeout))
                        return pattern;
                }
                catch
                {
                    /* Invalid regex */
                }
            }
        }

        return null;
    }

    [GeneratedRegex(@"\bgit\s+push\b", RegexOptions.IgnoreCase)]
    private static partial Regex ApprovalGitPush();

    [GeneratedRegex(@"\brm\s+", RegexOptions.IgnoreCase)]
    private static partial Regex ApprovalRm();

    [GeneratedRegex(@"\bgit\s+reset\b", RegexOptions.IgnoreCase)]
    private static partial Regex ApprovalGitReset();

    [GeneratedRegex(@"\bdocker\s+", RegexOptions.IgnoreCase)]
    private static partial Regex ApprovalDocker();

    /// <summary>
    ///     Populate a ProcessStartInfo's Environment with only safe variables.
    ///     Call this after creating the PSI and before starting the process.
    /// </summary>
    public static void SanitizeEnvironment(System.Diagnostics.ProcessStartInfo psi)
    {
        // Capture safe values before clearing
        var safeValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in SafeEnvVars)
        {
            var val = Environment.GetEnvironmentVariable(key);
            if (val is not null)
            {
                safeValues[key] = val;
            }
        }

        psi.Environment.Clear();
        foreach (var (key, val) in safeValues)
        {
            psi.Environment[key] = val;
        }
    }

    // ── Deny-list categories (for error messages) ────────────────────────────
    private static readonly string[] DenyCategories =
    [
        "destructive rm", // 1
        "windows del", // 2
        "windows rmdir", // 3
        "disk format", // 4
        "disk imaging", // 5
        "block device write", // 6
        "system control", // 7
        "fork bomb", // 8
        "command substitution", // 9
        "variable expansion", // 10
        "backtick substitution", // 11
        "pipe to sh", // 12
        "pipe to bash", // 13
        "pipe to python", // 13a
        "pipe to perl", // 13b
        "pipe to ruby", // 13c
        "pipe to node", // 13d
        "pipe to zsh", // 13e
        "pipe to pwsh", // 13f
        "pipe to fish", // 13g
        "chained rm", // 14
        "chained rm", // 15
        "chained rm", // 16
        "here-doc", // 17
        "cmd sub + cat", // 18
        "cmd sub + curl", // 19
        "cmd sub + wget", // 20
        "cmd sub + which", // 21
        "privilege escalation", // 22
        "permission change", // 23
        "ownership change", // 24
        "process kill", // 25
        "process kill", // 26
        "force kill", // 27
        "download + exec", // 28
        "download + exec", // 29
        "global npm install", // 30
        "user pip install", // 31
        "system package mgmt", // 32
        "system package mgmt", // 33
        "system package mgmt", // 34
        "container launch", // 35
        "container exec", // 36
        "remote git push", // 37
        "git force ops", // 38
        "SSH remote", // 39
        "eval", // 40
        "source script", // 41
        "ANSI-C quoting bypass", // 42
        "process substitution", // 43
        "symlink creation", // 44
        "named pipe creation", // 45
        "device node creation", // 46
        "mount command", // 47
        "job scheduling persistence", // 48
        "session persistence", // 49
        "chmod setuid/setgid", // 50
        "here-string", // 51
        "dot-sourcing" // 52
    ];

    // ── 52 compiled deny patterns (41 from picoclaw + 11 security audit) ──────
    private static readonly Regex[] DenyPatterns =
    [
        DenyRm(), // 1: rm -rf / rm -r / rm -f
        DenyDel(), // 2: del /f, del /q (Windows)
        DenyRmdir(), // 3: rmdir /s (Windows)
        DenyDiskFormat(), // 4: format, mkfs, diskpart
        DenyDd(), // 5: dd if=
        DenyBlockDevice(), // 6: > /dev/sdX
        DenySystemControl(), // 7: shutdown, reboot, poweroff
        DenyForkBomb(), // 8: :() { ... }; :
        DenyCmdSubstitution(), // 9: $(...)
        DenyVarExpansion(), // 10: ${...}
        DenyBacktick(), // 11: `...`
        DenyPipeSh(), // 12: | sh
        DenyPipeBash(), // 13: | bash
        DenyPipePython(), // 13a: | python / python3
        DenyPipePerl(), // 13b: | perl
        DenyPipeRuby(), // 13c: | ruby
        DenyPipeNode(), // 13d: | node
        DenyPipeZsh(), // 13e: | zsh
        DenyPipePwsh(), // 13f: | pwsh / powershell
        DenyPipeFish(), // 13g: | fish
        DenyChainedRm1(), // 14: ; rm -r/-f
        DenyChainedRm2(), // 15: && rm -r/-f
        DenyChainedRm3(), // 16: || rm -r/-f
        DenyHereDoc(), // 17: << EOF
        DenyCmdSubCat(), // 18: $(cat ...
        DenyCmdSubCurl(), // 19: $(curl ...
        DenyCmdSubWget(), // 20: $(wget ...
        DenyCmdSubWhich(), // 21: $(which ...
        DenySudo(), // 22: sudo
        DenyChmod(), // 23: chmod 755 etc.
        DenyChown(), // 24: chown
        DenyPkill(), // 25: pkill
        DenyKillall(), // 26: killall
        DenyKill9(), // 27: kill -9
        DenyCurlExec(), // 28: curl ... | sh/bash
        DenyWgetExec(), // 29: wget ... | sh/bash
        DenyNpmGlobal(), // 30: npm install -g
        DenyPipUser(), // 31: pip install --user
        DenyApt(), // 32: apt install/remove/purge
        DenyYum(), // 33: yum install/remove
        DenyDnf(), // 34: dnf install/remove
        DenyDockerRun(), // 35: docker run
        DenyDockerExec(), // 36: docker exec
        DenyGitPush(), // 37: git push
        DenyGitForce(), // 38: git force
        DenySsh(), // 39: ssh ...@
        DenyEval(), // 40: eval
        DenySource(), // 41: source *.sh
        DenyAnsiCQuoting(), // 42: $'...' ANSI-C quoting bypass
        DenyProcessSubstitution(), // 43: <(cmd) / >(cmd) process substitution
        DenyLn(), // 44: ln (symlink creation)
        DenyMkfifo(), // 45: mkfifo (named pipe creation)
        DenyMknod(), // 46: mknod (device node creation)
        DenyMount(), // 47: mount/umount
        DenyCrontab(), // 48: crontab/at (job scheduling)
        DenySessionPersistence(), // 49: nohup/screen/tmux
        DenyChmodSetuid(), // 50: chmod +s/+t (setuid/setgid)
        DenyHereString(), // 51: <<< (herestring)
        DenyDotSourcing() // 52: . /path/to/script (dot-space sourcing)
    ];

    // ── Source-generated regex patterns ───────────────────────────────────────

    [GeneratedRegex(@"\brm\s+(-[a-z]*[rf][a-z]*\b|--recursive\b|--force\b)", RegexOptions.IgnoreCase)]
    private static partial Regex DenyRm();

    [GeneratedRegex(@"\bdel\s+/[fq]\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyDel();

    [GeneratedRegex(@"\brmdir\s+/s\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyRmdir();

    [GeneratedRegex(@"\b(format|mkfs|diskpart)\b\s", RegexOptions.IgnoreCase)]
    private static partial Regex DenyDiskFormat();

    [GeneratedRegex(@"\bdd\s+if=", RegexOptions.IgnoreCase)]
    private static partial Regex DenyDd();

    [GeneratedRegex(@">\s*/dev/sd[a-z]\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyBlockDevice();

    [GeneratedRegex(@"\b(shutdown|reboot|poweroff)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenySystemControl();

    [GeneratedRegex(@":\(\)\s*\{.*\};\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex DenyForkBomb();

    [GeneratedRegex(@"\$\([^)]+\)", RegexOptions.IgnoreCase)]
    private static partial Regex DenyCmdSubstitution();

    [GeneratedRegex(@"\$\{[^}]+\}", RegexOptions.IgnoreCase)]
    private static partial Regex DenyVarExpansion();

    [GeneratedRegex(@"`[^`]+`", RegexOptions.IgnoreCase)]
    private static partial Regex DenyBacktick();

    [GeneratedRegex(@"\|\s*sh\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyPipeSh();

    [GeneratedRegex(@"\|\s*bash\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyPipeBash();

    [GeneratedRegex(@"\|\s*python[23]?\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyPipePython();

    [GeneratedRegex(@"\|\s*perl\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyPipePerl();

    [GeneratedRegex(@"\|\s*ruby\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyPipeRuby();

    [GeneratedRegex(@"\|\s*node\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyPipeNode();

    [GeneratedRegex(@"\|\s*zsh\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyPipeZsh();

    [GeneratedRegex(@"\|\s*(pwsh|powershell)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyPipePwsh();

    [GeneratedRegex(@"\|\s*fish\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyPipeFish();

    [GeneratedRegex(@";\s*rm\s+-[rf]", RegexOptions.IgnoreCase)]
    private static partial Regex DenyChainedRm1();

    [GeneratedRegex(@"&&\s*rm\s+-[rf]", RegexOptions.IgnoreCase)]
    private static partial Regex DenyChainedRm2();

    [GeneratedRegex(@"\|\|\s*rm\s+-[rf]", RegexOptions.IgnoreCase)]
    private static partial Regex DenyChainedRm3();

    [GeneratedRegex(@"<<[-~]?\s*\w+", RegexOptions.IgnoreCase)]
    private static partial Regex DenyHereDoc();

    [GeneratedRegex(@"\$\(\s*cat\s+", RegexOptions.IgnoreCase)]
    private static partial Regex DenyCmdSubCat();

    [GeneratedRegex(@"\$\(\s*curl\s+", RegexOptions.IgnoreCase)]
    private static partial Regex DenyCmdSubCurl();

    [GeneratedRegex(@"\$\(\s*wget\s+", RegexOptions.IgnoreCase)]
    private static partial Regex DenyCmdSubWget();

    [GeneratedRegex(@"\$\(\s*which\s+", RegexOptions.IgnoreCase)]
    private static partial Regex DenyCmdSubWhich();

    [GeneratedRegex(@"\bsudo\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenySudo();

    [GeneratedRegex(@"\bchmod\s+[0-7]{3,4}\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyChmod();

    [GeneratedRegex(@"\bchown\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyChown();

    [GeneratedRegex(@"\bpkill\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyPkill();

    [GeneratedRegex(@"\bkillall\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyKillall();

    [GeneratedRegex(@"\bkill\s+-9\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyKill9();

    [GeneratedRegex(@"\bcurl\b.*\|\s*(sh|bash|python[23]?|perl|ruby|node|zsh|pwsh|powershell|fish)", RegexOptions.IgnoreCase)]
    private static partial Regex DenyCurlExec();

    [GeneratedRegex(@"\bwget\b.*\|\s*(sh|bash|python[23]?|perl|ruby|node|zsh|pwsh|powershell|fish)", RegexOptions.IgnoreCase)]
    private static partial Regex DenyWgetExec();

    [GeneratedRegex(@"\bnpm\s+install\s+-g\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyNpmGlobal();

    [GeneratedRegex(@"\bpip\s+install\s+--user\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyPipUser();

    [GeneratedRegex(@"\bapt\s+(install|remove|purge)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyApt();

    [GeneratedRegex(@"\byum\s+(install|remove)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyYum();

    [GeneratedRegex(@"\bdnf\s+(install|remove)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyDnf();

    [GeneratedRegex(@"\bdocker\s+run\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyDockerRun();

    [GeneratedRegex(@"\bdocker\s+exec\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyDockerExec();

    [GeneratedRegex(@"\bgit\s+push\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyGitPush();

    [GeneratedRegex(@"\bgit\s+push\s+.*(?:--force|-f)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyGitForce();

    [GeneratedRegex(@"\bssh\b.*@", RegexOptions.IgnoreCase)]
    private static partial Regex DenySsh();

    [GeneratedRegex(@"\beval\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyEval();

    [GeneratedRegex(@"\bsource\s+\S+", RegexOptions.IgnoreCase)]
    private static partial Regex DenySource();

    [GeneratedRegex(@"\$'[^']*'")]
    private static partial Regex DenyAnsiCQuoting();

    [GeneratedRegex(@"[<>]\([^)]*\)")]
    private static partial Regex DenyProcessSubstitution();

    [GeneratedRegex(@"\bln\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyLn();

    [GeneratedRegex(@"\bmkfifo\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyMkfifo();

    [GeneratedRegex(@"\bmknod\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyMknod();

    [GeneratedRegex(@"\b(?:mount|umount)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyMount();

    [GeneratedRegex(@"\b(?:crontab|at)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenyCrontab();

    [GeneratedRegex(@"\b(?:nohup|screen|tmux)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenySessionPersistence();

    [GeneratedRegex(@"\bchmod\b.*[ugo]*[+][st]", RegexOptions.IgnoreCase)]
    private static partial Regex DenyChmodSetuid();

    [GeneratedRegex(@"<<<\s*\S+", RegexOptions.IgnoreCase)]
    private static partial Regex DenyHereString();

    [GeneratedRegex(@"(?:^|[;&|])\s*\.\s+[/~.\w]")]
    private static partial Regex DenyDotSourcing();

    private static void LogInvalidCustomPattern(ILogger? logger, string pattern, Exception ex)
    {
        if (logger is not null)
            LogInvalidPattern(logger, pattern, ex);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Invalid custom ShellGuard pattern '{Pattern}' — skipped")]
    private static partial void LogInvalidPattern(ILogger logger, string pattern, Exception exception);
}