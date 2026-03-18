using System.Text;
using System.Text.RegularExpressions;

namespace Clawsharp.Providers;

/// <summary>
/// Parameterized state machine that strips XML-like tag blocks from text.
/// Supports both a static method for complete text and a streaming state machine
/// for incremental chunk processing.
/// </summary>
/// <remarks>
/// Use <see cref="CreateThinkingFilter"/> for stripping <c>&lt;think&gt;</c>/<c>&lt;thinking&gt;</c>
/// reasoning blocks, and <see cref="CreateStreamingFilter"/> for stripping
/// <c>&lt;tool_call&gt;</c>/<c>&lt;tool_result&gt;</c> blocks.
/// </remarks>
internal sealed partial class TagStripFilter
{
    private readonly string[] _openTags;

    private readonly string[] _closeTags;

    /// <summary>
    /// Creates a new <see cref="TagStripFilter"/> that strips blocks enclosed by the given tag names.
    /// Each tag name (e.g. "think") maps to <c>&lt;think&gt;...&lt;/think&gt;</c>.
    /// </summary>
    public TagStripFilter(params string[] tagNames)
    {
        ArgumentOutOfRangeException.ThrowIfZero(tagNames.Length);
        _openTags = new string[tagNames.Length];
        _closeTags = new string[tagNames.Length];
        for (var i = 0; i < tagNames.Length; i++)
        {
            _openTags[i] = $"<{tagNames[i]}>";
            _closeTags[i] = $"</{tagNames[i]}>";
        }
    }

    // ── Factory methods ─────────────────────────────────────────────────────

    /// <summary>Creates a filter that strips <c>&lt;think&gt;</c> and <c>&lt;thinking&gt;</c> blocks.</summary>
    public static TagStripFilter CreateThinkingFilter() => new("think", "thinking");

    /// <summary>Creates a filter that strips <c>&lt;tool_call&gt;</c> and <c>&lt;tool_result&gt;</c> blocks.</summary>
    public static TagStripFilter CreateStreamingFilter() => new("tool_call", "tool_result");

    // ── Pre-compiled regexes for known tag pairs ────────────────────────────

    [GeneratedRegex(@"<think(?:ing)?>.*?</think(?:ing)?>", RegexOptions.Singleline, 200)]
    private static partial Regex ThinkingTagRegex();

    [GeneratedRegex(@"<tool_(?:call|result)>.*?</tool_(?:call|result)>", RegexOptions.Singleline, 200)]
    private static partial Regex ToolTagRegex();

    // ── Static (complete text) ───────────────────────────────────────────────

    /// <summary>
    /// Removes all matching tag blocks (including the tags themselves) from complete text.
    /// Uses dynamic regex construction — prefer the specialized <see cref="StripThinkingBlocks"/>
    /// and <see cref="StripToolTags"/> methods for known tag names.
    /// </summary>
    public static string StripTags(string text, params string[] tagNames)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var result = text;
        foreach (var tag in tagNames)
        {
            result = Regex.Replace(result, $@"<{Regex.Escape(tag)}>.*?</{Regex.Escape(tag)}>", string.Empty, RegexOptions.Singleline, TimeSpan.FromMilliseconds(200));
        }

        return result;
    }

    /// <summary>
    /// Removes all <c>&lt;think&gt;...&lt;/think&gt;</c> and <c>&lt;thinking&gt;...&lt;/thinking&gt;</c>
    /// blocks from complete text using a pre-compiled regex.
    /// </summary>
    public static string StripThinkingBlocks(string text) =>
        string.IsNullOrEmpty(text) ? text : ThinkingTagRegex().Replace(text, string.Empty);

    /// <summary>
    /// Removes all <c>&lt;tool_call&gt;...&lt;/tool_call&gt;</c> and
    /// <c>&lt;tool_result&gt;...&lt;/tool_result&gt;</c> blocks from complete text using a pre-compiled regex.
    /// </summary>
    public static string StripToolTags(string text) =>
        string.IsNullOrEmpty(text) ? text : ToolTagRegex().Replace(text, string.Empty);

    // ── Streaming state machine ──────────────────────────────────────────────

    private enum State
    {
        /// <summary>Normal output -- pass through to caller.</summary>
        Normal,

        /// <summary>Buffering characters that might be the start of an opening tag.</summary>
        MaybeOpenTag,

        /// <summary>Inside a tag block -- suppress all text.</summary>
        InsideBlock,

        /// <summary>Inside a block, buffering characters that might be a closing tag.</summary>
        MaybeCloseTag
    }

    private State _state = State.Normal;

    private readonly StringBuilder _tagBuffer = new();

    /// <summary>
    /// Tracks which opening tag matched so we can close with the corresponding tag.
    /// Only valid when <see cref="_state"/> is <see cref="State.InsideBlock"/> or <see cref="State.MaybeCloseTag"/>.
    /// </summary>
    private int _matchedTagIndex = -1;

    /// <summary>
    /// Process a streaming chunk. Returns the portion that should be visible to the user.
    /// </summary>
    public string ProcessChunk(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return string.Empty;
        }

        var output = new StringBuilder();

        foreach (var ch in chunk)
        {
            switch (_state)
            {
                case State.Normal:
                    ProcessNormal(ch, output);
                    break;

                case State.MaybeOpenTag:
                    ProcessMaybeOpenTag(ch, output);
                    break;

                case State.InsideBlock:
                    ProcessInsideBlock(ch);
                    break;

                case State.MaybeCloseTag:
                    ProcessMaybeCloseTag(ch);
                    break;
            }
        }

        return output.ToString();
    }

    /// <summary>
    /// Call when the stream ends. Returns any buffered text that was not part of
    /// a confirmed tag block (e.g. a partial "&lt;thi" that never completed).
    /// </summary>
    public string Flush()
    {
        // If we were buffering a potential opening tag that never completed,
        // that text is real content -- return it.
        if (_state == State.MaybeOpenTag)
        {
            var buffered = _tagBuffer.ToString();
            _tagBuffer.Clear();
            _state = State.Normal;
            return buffered;
        }

        // If we're inside a block or buffering a potential close tag,
        // the block was never closed. Discard the buffered content since
        // the tag block was opened but the model stopped mid-output.
        _tagBuffer.Clear();
        _state = State.Normal;
        _matchedTagIndex = -1;
        return string.Empty;
    }

    // ── State handlers ───────────────────────────────────────────────────────

    private void ProcessNormal(char ch, StringBuilder output)
    {
        if (ch == '<')
        {
            _tagBuffer.Clear();
            _tagBuffer.Append(ch);
            _state = State.MaybeOpenTag;
        }
        else
        {
            output.Append(ch);
        }
    }

    private void ProcessMaybeOpenTag(char ch, StringBuilder output)
    {
        _tagBuffer.Append(ch);
        var buffered = _tagBuffer.ToString();

        // Check if buffer fully matches an opening tag
        for (var i = 0; i < _openTags.Length; i++)
        {
            if (string.Equals(buffered, _openTags[i], StringComparison.Ordinal))
            {
                // Full match -- enter the tag block
                _tagBuffer.Clear();
                _state = State.InsideBlock;
                _matchedTagIndex = i;
                return;
            }
        }

        // Check if buffer is still a valid prefix of any opening tag
        for (var i = 0; i < _openTags.Length; i++)
        {
            if (_openTags[i].StartsWith(buffered, StringComparison.Ordinal))
            {
                // Still a valid prefix -- keep buffering
                return;
            }
        }

        // Not a valid prefix of any tag -- flush buffer as normal text
        output.Append(buffered);
        _tagBuffer.Clear();
        _state = State.Normal;
    }

    private void ProcessInsideBlock(char ch)
    {
        if (ch == '<')
        {
            // Might be the start of a closing tag
            _tagBuffer.Clear();
            _tagBuffer.Append(ch);
            _state = State.MaybeCloseTag;
        }
        // Otherwise discard -- we're inside a tag block
    }

    private void ProcessMaybeCloseTag(char ch)
    {
        _tagBuffer.Append(ch);
        var buffered = _tagBuffer.ToString();
        var closeTag = _closeTags[_matchedTagIndex];

        if (string.Equals(buffered, closeTag, StringComparison.Ordinal))
        {
            // Full match on closing tag -- exit the block
            _tagBuffer.Clear();
            _state = State.Normal;
            _matchedTagIndex = -1;
            return;
        }

        if (closeTag.StartsWith(buffered, StringComparison.Ordinal))
        {
            // Still a valid prefix of the closing tag -- keep buffering
            return;
        }

        // Not a valid close tag prefix -- discard buffer (still inside block)
        _tagBuffer.Clear();
        _state = State.InsideBlock;
    }
}