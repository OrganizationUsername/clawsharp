namespace Clawsharp.Core;

/// <summary>A single message in a chat conversation (used by providers).</summary>
public sealed record ChatMessage(
    /// <summary>The role (system, user, assistant, or tool).</summary>
    MessageRole Role,
    /// <summary>Text content of the message (null for tool-call-only messages).</summary>
    string? Content,
    /// <summary>Tool call ID (for tool-role messages).</summary>
    string? ToolCallId = null,
    /// <summary>Optional tool/function name.</summary>
    string? Name = null,
    /// <summary>Tool calls requested by the assistant.</summary>
    IReadOnlyList<ToolCall>? ToolCalls = null,
    /// <summary>Optional image attachments for multimodal messages.</summary>
    IReadOnlyList<ImageAttachment>? Images = null,
    /// <summary>Optional file attachments for multimodal messages.</summary>
    IReadOnlyList<FileAttachment>? Files = null,
    /// <summary>Optional video attachments.</summary>
    IReadOnlyList<VideoAttachment>? Videos = null,
    /// <summary>Optional raw audio attachment for native model processing.</summary>
    AudioAttachment? Audio = null,
    /// <summary>UTC timestamp when this message was created. Null for legacy messages loaded from disk.</summary>
    DateTimeOffset? Timestamp = null
);