using System.Text.Json.Serialization;

namespace Clawsharp.Tools.Browser;

/// <summary>PinchTab navigate request.</summary>
public sealed record PtNavigateRequest(
    [property: JsonPropertyName("url")]
    string Url,
    [property: JsonPropertyName("tabId")]
    string? TabId);

/// <summary>PinchTab navigate response.</summary>
public sealed record PtNavigateResponse(
    [property: JsonPropertyName("tabId")]
    string TabId,
    [property: JsonPropertyName("url")]
    string Url,
    [property: JsonPropertyName("title")]
    string Title);

/// <summary>PinchTab single action request.</summary>
public sealed record PtActionRequest(
    [property: JsonPropertyName("kind")]
    string Kind,
    [property: JsonPropertyName("tabId")]
    string TabId,
    [property: JsonPropertyName("ref")]
    string? Ref = null,
    [property: JsonPropertyName("text")]
    string? Text = null,
    [property: JsonPropertyName("value")]
    string? Value = null,
    [property: JsonPropertyName("key")]
    string? Key = null);

/// <summary>PinchTab action response.</summary>
public sealed record PtActionResponse(
    [property: JsonPropertyName("success")]
    bool Success);

/// <summary>PinchTab tab info.</summary>
public sealed record PtTabInfo(
    [property: JsonPropertyName("id")]
    string Id,
    [property: JsonPropertyName("url")]
    string Url,
    [property: JsonPropertyName("title")]
    string Title);

/// <summary>PinchTab tabs list response.</summary>
public sealed record PtTabsResponse(
    [property: JsonPropertyName("tabs")]
    PtTabInfo[] Tabs);

/// <summary>PinchTab evaluate request.</summary>
public sealed record PtEvaluateRequest(
    [property: JsonPropertyName("tabId")]
    string TabId,
    [property: JsonPropertyName("expression")]
    string Expression);

/// <summary>PinchTab evaluate response (result is freeform JSON; read body as string).</summary>
public sealed record PtEvaluateResponse(
    [property: JsonPropertyName("success")]
    bool Success);

/// <summary>PinchTab tab close request.</summary>
public sealed record PtTabCloseRequest(
    [property: JsonPropertyName("action")]
    string Action,
    [property: JsonPropertyName("tabId")]
    string TabId);

/// <summary>Source-generated JSON context for PinchTab DTOs.</summary>
[JsonSerializable(typeof(PtNavigateRequest))]
[JsonSerializable(typeof(PtNavigateResponse))]
[JsonSerializable(typeof(PtActionRequest))]
[JsonSerializable(typeof(PtActionResponse))]
[JsonSerializable(typeof(PtTabInfo[]))]
[JsonSerializable(typeof(PtTabsResponse))]
[JsonSerializable(typeof(PtEvaluateRequest))]
[JsonSerializable(typeof(PtEvaluateResponse))]
[JsonSerializable(typeof(PtTabCloseRequest))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class PinchTabJsonContext : JsonSerializerContext;