using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Clawsharp.Config;
using Clawsharp.Config.Security;

namespace Clawsharp.Security;

/// <summary>
/// Action taken when a prompt injection pattern is detected.
/// </summary>
public enum InjectionAction
{
    /// <summary>No injection pattern was found.</summary>
    None,

    /// <summary>A pattern was found and logged but the message was allowed through (warn mode or sanitize mode).</summary>
    Warn,

    /// <summary>A pattern was found and the message should be rejected (block mode).</summary>
    Block,
}

/// <summary>
/// Mode constants for prompt injection handling.
/// </summary>
internal static class PromptGuardModes
{
    public const string Block = "block";

    public const string Warn = "warn";

    public const string Sanitize = "sanitize";
}

/// <summary>
/// Wraps untrusted content in XML-style delimiters before injecting into LLM context
/// and heuristically scans for adversarial directive phrases.
/// </summary>
internal static partial class PromptGuard
{
    /// <summary>Wrap an untrusted user message in delimiters.</summary>
    public static string WrapUserMessage(string content) =>
        $"<user_message>\n{EscapeDelimiterContent(content)}\n</user_message>";

    /// <summary>Wrap a tool result in delimiters with the tool name.</summary>
    public static string WrapToolResult(string toolName, string content) =>
        $"<tool_result name=\"{SanitizeTagName(toolName)}\">\n{EscapeDelimiterContent(content)}\n</tool_result>";

    /// <summary>Wrap arbitrary system-injected content in a named tag.</summary>
    public static string WrapSystemContent(string tag, string content) =>
        $"<{SanitizeTagName(tag)}>\n{EscapeDelimiterContent(content)}\n</{SanitizeTagName(tag)}>";

    /// <summary>
    /// Escapes XML-sensitive characters in untrusted content before inserting between XML delimiters.
    /// Prevents delimiter breakout via &lt;/user_message&gt; or similar injection.
    /// </summary>
    internal static string EscapeDelimiterContent(string content)
        => content.AsSpan().IndexOfAny("&<>") < 0
            ? content
            : content
              .Replace("&", "&amp;")
              .Replace("<", "&lt;")
              .Replace(">", "&gt;");

    /// <summary>
    /// Sanitizes a string for use as an XML tag name or attribute value.
    /// Only allows alphanumeric characters and underscores; all others are stripped.
    /// </summary>
    private static string SanitizeTagName(string name)
    {
        Span<char> buf = stackalloc char[name.Length];
        var pos = 0;
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                buf[pos++] = c;
            }
        }

        return pos == name.Length ? name : new string(buf[..pos]);
    }

    // Raw strings used both for the [GeneratedRegex] alternation and for building
    // custom combined patterns at runtime. ImmutableArray ensures this cannot be
    // mutated after initialization (string[] would allow element replacement).
    private static readonly ImmutableArray<string> BuiltInPatterns =
    [
        "ignore previous instructions",
        "ignore all previous",
        "disregard previous",
        "forget previous instructions",
        "you are now",
        "act as if",
        "pretend you are",
        "new persona",
        "system prompt",
        "override instructions",
        "jailbreak",
        "dan mode",
        "developer mode",
        "ignore your instructions",
    ];

    [GeneratedRegex(
        "ignore previous instructions|ignore all previous|disregard previous|" +
        "forget previous instructions|you are now|act as if|pretend you are|" +
        "new persona|system prompt|override instructions|jailbreak|" +
        "dan mode|developer mode|ignore your instructions",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BuiltInPatternRegex();

    // Set at startup when custom patterns are configured; null means use BuiltInPatternRegex().
    private static volatile Regex? _customRegex;

    /// <summary>
    /// Call at startup when custom patterns are configured.
    /// Builds a combined regex of built-in + custom patterns.
    /// </summary>
    public static void Configure(PromptGuardConfig? config)
    {
        if (config?.CustomPatterns is { Count: > 0 } extra)
        {
            var escapedBuiltIns = BuiltInPatterns.Select(Regex.Escape);
            var escapedCustom = extra.Select(Regex.Escape);
            var combined = string.Join("|", escapedBuiltIns.Concat(escapedCustom));
            _customRegex = new Regex(
                combined,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        }
        else
        {
            // No custom patterns — reset to null so we use the source-generated regex.
            _customRegex = null;
        }
    }

    private static Regex ActiveRegex => _customRegex ?? BuiltInPatternRegex();

    /// <summary>
    /// Strips zero-width and invisible Unicode characters that could be inserted between letters
    /// to evade pattern matching (e.g. "ignore\u200Bprevious" bypassing "ignore previous").
    /// </summary>
    [GeneratedRegex(@"[\u200B\u200C\u200D\u200E\u200F\uFEFF\u00AD\u2060\u2061\u2062\u2063\u2064\u180E]")]
    private static partial Regex InvisibleCharsRegex();

    /// <summary>
    /// Normalizes input for injection scanning: strips invisible Unicode characters and applies
    /// NFKD decomposition to defeat confusable-character evasion (e.g. Cyrillic "І" → Latin "I").
    /// </summary>
    private static string NormalizeForScanning(string input)
    {
        var stripped = InvisibleCharsRegex().Replace(input, "");
        return stripped.Normalize(NormalizationForm.FormKD);
    }

    /// <summary>
    /// Heuristic scan for prompt injection patterns.
    /// Returns the first matched phrase if suspicious, null if clean.
    /// </summary>
    public static string? ScanForInjection(string content)
    {
        var normalized = NormalizeForScanning(content);
        var m = ActiveRegex.Match(normalized);
        return m.Success ? m.Value : null;
    }

    /// <summary>
    /// Scans <paramref name="content"/> for injection patterns and applies the configured
    /// <paramref name="mode"/> action in-place. Writes an audit event when a match is found.
    /// </summary>
    /// <returns>
    /// <see cref="InjectionAction.None"/> — clean.
    /// <see cref="InjectionAction.Warn"/> — matched but allowed (warn or sanitize mode).
    /// <see cref="InjectionAction.Block"/> — matched and caller must reject the message.
    /// </returns>
    public static InjectionAction ScanAndApply(
        ref string content,
        string source,
        string mode,
        AuditLogger? auditLogger,
        string? channel = null,
        string? userId = null,
        CancellationToken ct = default)
    {
        // Normalize to defeat zero-width char and Unicode confusable evasion.
        var normalized = NormalizeForScanning(content);
        var m = ActiveRegex.Match(normalized);
        if (!m.Success)
        {
            return InjectionAction.None;
        }

        if (auditLogger is not null)
        {
            _ = auditLogger.LogSecurityEventAsync(
                $"Prompt injection detected in {source}: matched '{m.Value}'",
                channel, userId, ct);
        }

        if (string.Equals(mode, PromptGuardModes.Block, StringComparison.OrdinalIgnoreCase))
            return InjectionAction.Block;

        if (string.Equals(mode, PromptGuardModes.Sanitize, StringComparison.OrdinalIgnoreCase))
            return SanitizeContent(ref content);

        return InjectionAction.Warn; // "warn" or any unknown value
    }

    private static InjectionAction SanitizeContent(ref string content)
    {
        // MED-01: Normalize content before replacement so that zero-width characters
        // between pattern words (e.g. "ignore\u200Bprevious") are properly redacted.
        content = NormalizeForScanning(content);
        content = ActiveRegex.Replace(content, "[FILTERED]");
        return InjectionAction.Warn;
    }

    // ── Metadata sentinel stripping ────────────────────────────────────

    /// <summary>
    /// Strips or escapes internal metadata sentinel markers from user input before processing.
    /// Prevents users from injecting model role markers, ChatML delimiters, Llama instruction
    /// tags, or canary guard tokens that could confuse the system or LLM.
    /// </summary>
    public static string StripMetadataSentinels(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        return MetadataSentinelRegex().Replace(input, "");
    }

    [GeneratedRegex(
        @"\[SYSTEM\]|\[/SYSTEM\]|<\|system\|>|<\|user\|>|<\|assistant\|>|" +
        @"<\|im_start\|>|<\|im_end\|>|" +
        @"<<SYS>>|<</SYS>>|" +
        @"\[INST\]|\[/INST\]|" +
        @"CSSEC-[0-9A-Fa-f]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetadataSentinelRegex();

    // ── Backward-compatible scan ────────────────────────────────────

    /// <summary>
    /// Scans for injection and writes an audit event if a match is found.
    /// Returns the matched pattern or null.
    /// Kept for backward compatibility with call sites that do not need mode-aware handling.
    /// </summary>
    public static string? ScanAndAudit(
        string content,
        string source,
        AuditLogger? auditLogger,
        string? channel = null,
        string? userId = null,
        CancellationToken ct = default)
    {
        var pattern = ScanForInjection(content);
        if (pattern is not null && auditLogger is not null)
        {
            _ = auditLogger.LogSecurityEventAsync(
                $"Prompt injection detected in {source}: matched '{pattern}'",
                channel, userId, ct);
        }

        return pattern;
    }
}