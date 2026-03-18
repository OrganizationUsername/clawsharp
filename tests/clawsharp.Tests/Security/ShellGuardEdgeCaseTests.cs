using Clawsharp.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clawsharp.Tests.Security;

[TestFixture]
public sealed class ShellGuardEdgeCaseTests
{
    [TearDown]
    public void ResetCustomPatterns()
    {
        // Reset compiled custom patterns after each test so state doesn't bleed
        ShellGuard.ConfigureCustomPatterns(null, null, null, NullLogger.Instance);
    }

    // ── CRIT-1: Legacy custom deny ReDoS now blocks (fail-closed) ─────────
    //
    // A custom deny pattern that triggers RegexMatchTimeoutException should
    // BLOCK the command rather than allowing it through. This verifies the
    // fix for the fail-open ReDoS vulnerability.
    //
    // The legacy path is used when ConfigureCustomPatterns has NOT been called
    // and raw pattern strings are passed via the customDenyPatterns parameter.

    [Test]
    public void CheckCommand_LegacyCustomDeny_ReDoSTimeout_BlocksCommand()
    {
        // Catastrophic backtracking pattern: (a+)+$ against a long string of 'a's
        // followed by a non-matching char forces exponential backtracking.
        var redosPattern = @"(a+)+$";
        var customDenyPatterns = new List<string> { redosPattern };

        // Input that triggers catastrophic backtracking: many 'a's followed by '!'
        // The '!' prevents the match from succeeding, forcing the engine to try
        // exponentially many groupings of the 'a's before giving up.
        var maliciousInput = new string('a', 30) + "!";

        var result = ShellGuard.CheckCommand(maliciousInput, customDenyPatterns);

        // After the fix, a ReDoS timeout should result in the command being BLOCKED
        // rather than silently allowed through.
        result.ShouldNotBeNull("ReDoS timeout on custom deny pattern should block the command (fail-closed)");
        result.ShouldContain("timed out");
        result.ShouldContain("ReDoS");
    }

    [Test]
    public void CheckCommand_CompiledCustomDeny_ReDoSTimeout_BlocksCommand()
    {
        // Same test but via the pre-compiled path (ConfigureCustomPatterns called)
        ShellGuard.ConfigureCustomPatterns(
            denyPatterns: [@"(a+)+$"],
            approvalPatterns: null,
            autoApprovePatterns: null,
            logger: NullLogger.Instance);

        var maliciousInput = new string('a', 30) + "!";

        var result = ShellGuard.CheckCommand(maliciousInput);

        result.ShouldNotBeNull("ReDoS timeout on compiled custom deny pattern should block the command");
        result.ShouldContain("timed out");
        result.ShouldContain("ReDoS");
    }

    // ── HIGH-3: Fullwidth Unicode character bypass ─────────────────────────
    //
    // NormalizeCommand does NOT perform NFKD Unicode normalization, so fullwidth
    // characters like ｒｍ (U+FF52 U+FF4D) are not decomposed to their ASCII
    // equivalents. This means deny patterns for "rm" will not match "\uFF52\uFF4D".
    //
    // This is a known limitation — documented here so it is tracked.

    [Test]
    public void CheckCommand_FullwidthRm_KnownLimitation_NotBlocked()
    {
        // Fullwidth ｒｍ (U+FF52 U+FF4D) is visually similar to "rm" but different codepoints.
        // NormalizeCommand strips shell quotes and backslash escapes but does not
        // perform Unicode NFKD normalization, so fullwidth chars pass through undetected.
        var command = "\uFF52\uFF4D -rf /";

        var result = ShellGuard.CheckCommand(command);

        // This is the CURRENT behavior: fullwidth chars bypass deny patterns.
        // If NFKD normalization is added in the future, this test should be updated
        // to assert the command IS blocked.
        result.ShouldBeNull(
            "Known limitation: fullwidth Unicode chars bypass deny patterns because " +
            "NormalizeCommand does not perform NFKD normalization.");
    }

    [Test]
    public void CheckCommand_FullwidthSudo_KnownLimitation_NotBlocked()
    {
        // Fullwidth ｓｕｄｏ
        var command = "\uFF53\uFF55\uFF44\uFF4F apt update";

        var result = ShellGuard.CheckCommand(command);

        result.ShouldBeNull(
            "Known limitation: fullwidth 'sudo' bypasses deny patterns.");
    }

    // ── MED-5: Bare $HOME (no braces) not blocked ─────────────────────────
    //
    // DenyVarExpansion regex requires ${...} braces — $HOME (bare dollar-sign
    // variable without braces) does not match. This is by design: bare variable
    // references are extremely common in safe commands (e.g., echo $PATH).

    [Test]
    public void CheckCommand_BareVariableExpansion_KnownLimitation_Allowed()
    {
        // $HOME without braces does not trigger the ${...} variable expansion deny
        var result = ShellGuard.CheckCommand("echo $HOME");

        result.ShouldBeNull(
            "Known limitation: bare $HOME passes because DenyVarExpansion regex " +
            "only matches ${VAR} form with braces.");
    }

    [Test]
    public void CheckCommand_BracedVariableExpansion_Blocked()
    {
        // ${HOME} WITH braces is blocked
        var result = ShellGuard.CheckCommand("echo ${HOME}");

        result.ShouldNotBeNull("${HOME} with braces should be blocked by DenyVarExpansion");
        result.ShouldContain("blocked");
    }

    // ── MED-6: Pipe to php/lua/tclsh interpreters ─────────────────────────
    //
    // The pipe-to-interpreter deny list covers sh, bash, python, perl, ruby,
    // node, zsh, pwsh/powershell, and fish. It does NOT cover php, lua, or tclsh.

    [Test]
    public void CheckCommand_PipeToPhp_KnownLimitation_NotBlocked()
    {
        var result = ShellGuard.CheckCommand("cat payload | php");

        // php is NOT in the pipe-to-interpreter deny list
        result.ShouldBeNull(
            "Known limitation: piping to php is not in the deny list. " +
            "Only sh, bash, python, perl, ruby, node, zsh, pwsh, fish are covered.");
    }

    [Test]
    public void CheckCommand_PipeToLua_KnownLimitation_NotBlocked()
    {
        var result = ShellGuard.CheckCommand("cat payload | lua");

        result.ShouldBeNull(
            "Known limitation: piping to lua is not in the deny list.");
    }

    [Test]
    public void CheckCommand_PipeToTclsh_KnownLimitation_NotBlocked()
    {
        var result = ShellGuard.CheckCommand("cat payload | tclsh");

        result.ShouldBeNull(
            "Known limitation: piping to tclsh is not in the deny list.");
    }

    // ── MED-7: RequiresApproval timeout behavior ──────────────────────────
    //
    // When a compiled approval pattern times out due to ReDoS, the catch block
    // swallows the exception and continues checking remaining patterns. This means
    // the timed-out pattern is effectively skipped (command NOT marked as needing
    // approval). Similarly, auto-approve pattern timeout means the auto-approve
    // is skipped, so approval is still checked.

    [Test]
    public void RequiresApproval_ApprovalPatternReDoS_KnownLimitation_SkipsTimedOutPattern()
    {
        // Configure an approval pattern that will trigger ReDoS
        ShellGuard.ConfigureCustomPatterns(
            denyPatterns: null,
            approvalPatterns: [@"(a+)+$"],
            autoApprovePatterns: null,
            logger: NullLogger.Instance);

        var maliciousInput = new string('a', 30) + "!";

        // The approval pattern will time out. The catch block swallows the exception
        // and returns null (no approval required). Built-in patterns are checked first
        // and won't match this input.
        var result = ShellGuard.RequiresApproval(maliciousInput, null, null);

        // Known behavior: timeout on approval pattern = pattern is skipped = no approval required
        // This is less critical than deny-pattern ReDoS because it fails in the
        // "more permissive" direction (allowing without approval vs blocking outright).
        result.ShouldBeNull(
            "Known limitation: ReDoS timeout on approval pattern causes the pattern to be " +
            "skipped, meaning the command is NOT flagged for approval.");
    }

    [Test]
    public void RequiresApproval_AutoApprovePatternReDoS_FallsThroughToApprovalCheck()
    {
        // Configure auto-approve with ReDoS pattern and a normal approval pattern
        ShellGuard.ConfigureCustomPatterns(
            denyPatterns: null,
            approvalPatterns: [@"\bdeploy\b"],
            autoApprovePatterns: [@"(a+)+$"],
            logger: NullLogger.Instance);

        // "deploy" followed by ReDoS-triggering suffix
        var command = "deploy " + new string('a', 30) + "!";

        // The auto-approve pattern will time out (swallowed), so auto-approve is skipped.
        // Then the approval pattern "deploy" will match.
        var result = ShellGuard.RequiresApproval(command, null, null);

        // When auto-approve times out, approval check still runs
        result.ShouldNotBeNull(
            "Auto-approve ReDoS timeout should fall through to approval patterns");
    }

    // ── LOW-7: Tab character in command ────────────────────────────────────
    //
    // Tabs are NOT control characters that trigger the newline/control char check.
    // NormalizeCommand also does not replace tabs with spaces. The question is
    // whether "rm\t-rf /" bypasses the deny pattern (which uses \s matching).

    [Test]
    public void CheckCommand_TabBetweenRmAndFlag_StillBlocked()
    {
        // \s in regex matches tabs, so "rm\t-rf" still matches \brm\s+(-[a-z]*[rf]...)
        var result = ShellGuard.CheckCommand("rm\t-rf /");

        result.ShouldNotBeNull("Tab should be treated as whitespace by regex \\s patterns");
        result.ShouldContain("blocked");
    }

    [Test]
    public void CheckCommand_TabBetweenSudoAndCommand_StillBlocked()
    {
        var result = ShellGuard.CheckCommand("sudo\tapt update");

        result.ShouldNotBeNull("Tab after sudo should still match \\bsudo\\b");
        result.ShouldContain("blocked");
    }

    [Test]
    public void CheckCommand_TabInPipeToInterpreter_StillBlocked()
    {
        // "|\tsh" should still match \|\s*sh\b
        var result = ShellGuard.CheckCommand("cat file |\tsh");

        result.ShouldNotBeNull("Tab between pipe and interpreter should be caught by \\s* in pattern");
        result.ShouldContain("blocked");
    }

    // ── LOW-8: mkfs -t ext4 /dev/sda (with space after mkfs) ──────────────
    //
    // The DenyDiskFormat pattern is \b(format|mkfs|diskpart)\b\s which requires
    // whitespace after the word boundary. "mkfs -t ext4" has a space after "mkfs"
    // so it DOES match. But "mkfs.ext4" does NOT match (no whitespace).

    [Test]
    public void CheckCommand_MkfsWithSpaceAndType_Blocked()
    {
        // "mkfs -t ext4 /dev/sda" — has whitespace after "mkfs", so pattern 4 matches
        var result = ShellGuard.CheckCommand("mkfs -t ext4 /dev/sda");

        result.ShouldNotBeNull("mkfs followed by space should be caught by DenyDiskFormat");
        result.ShouldContain("blocked");
        result.ShouldContain("disk format");
    }

    [Test]
    public void CheckCommand_MkfsDotExt4_KnownLimitation_NotBlocked()
    {
        // "mkfs.ext4" — no whitespace between word boundary and next char
        // This is documented in the existing ShellGuardTests.CheckCommand_MkfsWithFsType_NotBlockedByDiskPattern
        var result = ShellGuard.CheckCommand("mkfs.ext4 /dev/sda1");

        result.ShouldBeNull(
            "Known limitation: mkfs.ext4 (no space after mkfs) bypasses the disk format pattern.");
    }

    // ── LOW-9: PATH variable not sanitized in command ──────────────────────
    //
    // ShellGuard.CheckCommand does not detect PATH manipulation in the command
    // string. PATH= is not in any deny pattern. SanitizeEnvironment strips it,
    // but the command text itself is not scanned for environment variable assignments.

    [Test]
    public void CheckCommand_PathManipulation_KnownLimitation_Allowed()
    {
        // PATH=/evil:$PATH command — the PATH assignment is allowed through
        // because there's no deny pattern matching "PATH=" in the command.
        // Note: $PATH (bare variable) also passes (see MED-5 tests above).
        var result = ShellGuard.CheckCommand("PATH=/evil command");

        result.ShouldBeNull(
            "Known limitation: PATH=/evil in command text is allowed. " +
            "SanitizeEnvironment handles PATH for subprocess environment, " +
            "but the command string is not scanned for env assignments.");
    }

    [Test]
    public void SanitizeEnvironment_PathVariable_IsPreservedFromSystem()
    {
        // SanitizeEnvironment DOES preserve PATH from the system — it's in the safe list.
        // The concern is about attacker-controlled PATH= in command strings, not the
        // environment variable sanitization itself.
        var psi = new System.Diagnostics.ProcessStartInfo("echo", "test");
        ShellGuard.SanitizeEnvironment(psi);

        if (Environment.GetEnvironmentVariable("PATH") is not null)
        {
            psi.Environment.ShouldContainKey("PATH");
        }
    }

    // ── Additional edge cases ─────────────────────────────────────────────

    [Test]
    public void CheckCommand_EmptyString_Allowed()
    {
        var result = ShellGuard.CheckCommand("");

        result.ShouldBeNull("Empty command should not be blocked");
    }

    [Test]
    public void CheckCommand_WhitespaceOnly_Allowed()
    {
        var result = ShellGuard.CheckCommand("   ");

        result.ShouldBeNull("Whitespace-only command should not be blocked");
    }

    [Test]
    public void CheckCommand_MultipleSpacesBetweenArgs_StillBlocked()
    {
        // \s+ in patterns matches multiple spaces
        var result = ShellGuard.CheckCommand("rm    -rf    /");

        result.ShouldNotBeNull("Multiple spaces should still match \\s+ patterns");
        result.ShouldContain("blocked");
    }

    [Test]
    public void CheckCommand_AnsiCQuotingBypass_Blocked()
    {
        // $'\x72\x6D' could decode to "rm" in bash — pattern 42 detects ANSI-C quoting
        var result = ShellGuard.CheckCommand("$'\\x72\\x6D' -rf /");

        result.ShouldNotBeNull("ANSI-C quoting ($'...') should be blocked");
        result.ShouldContain("blocked");
    }

    [Test]
    public void CheckCommand_ProcessSubstitution_Blocked()
    {
        var result = ShellGuard.CheckCommand("diff <(ls dir1) <(ls dir2)");

        result.ShouldNotBeNull("Process substitution <(...) should be blocked");
        result.ShouldContain("blocked");
    }

    [Test]
    public void CheckCommand_HereString_Blocked()
    {
        var result = ShellGuard.CheckCommand("cat <<< 'some data'");

        result.ShouldNotBeNull("Here-string (<<<) should be blocked");
        result.ShouldContain("blocked");
    }

    [Test]
    public void CheckCommand_DotSourcing_Blocked()
    {
        var result = ShellGuard.CheckCommand(". /etc/profile");

        result.ShouldNotBeNull("Dot-space sourcing should be blocked");
        result.ShouldContain("blocked");
    }

    [Test]
    public void CheckCommand_SymlinkCreation_Blocked()
    {
        var result = ShellGuard.CheckCommand("ln -s /etc/passwd ~/link");

        result.ShouldNotBeNull("ln (symlink creation) should be blocked");
        result.ShouldContain("blocked");
    }

    [Test]
    public void CheckCommand_MkfifoCreation_Blocked()
    {
        var result = ShellGuard.CheckCommand("mkfifo /tmp/pipe");

        result.ShouldNotBeNull("mkfifo should be blocked");
        result.ShouldContain("blocked");
    }

    [Test]
    public void CheckCommand_CrontabScheduling_Blocked()
    {
        var result = ShellGuard.CheckCommand("crontab -e");

        result.ShouldNotBeNull("crontab should be blocked");
        result.ShouldContain("blocked");
    }

    [Test]
    public void CheckCommand_NohupPersistence_Blocked()
    {
        var result = ShellGuard.CheckCommand("nohup long-running-process &");

        result.ShouldNotBeNull("nohup should be blocked");
        result.ShouldContain("blocked");
    }

    [Test]
    public void CheckCommand_ChmodSetuid_Blocked()
    {
        var result = ShellGuard.CheckCommand("chmod u+s /usr/bin/exploit");

        result.ShouldNotBeNull("chmod +s (setuid) should be blocked");
        result.ShouldContain("blocked");
    }
}
