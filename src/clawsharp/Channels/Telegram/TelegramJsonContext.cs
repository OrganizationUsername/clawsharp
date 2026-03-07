using System.Text.Json.Serialization;

namespace Clawsharp.Channels.Telegram;

/// <summary>Source-generated JSON serialization context for Telegram Bot API models.</summary>
[JsonSerializable(typeof(TelegramEditMessageTextRequest))]
[JsonSerializable(typeof(TelegramGetUpdatesResponse))]
[JsonSerializable(typeof(TelegramSendMessageRequest))]
[JsonSerializable(typeof(TelegramSendChatActionRequest))]
[JsonSerializable(typeof(TelegramGetFileResponse))]
[JsonSerializable(typeof(TelegramFile))]
[JsonSerializable(typeof(TelegramPhotoSize))]
[JsonSerializable(typeof(TelegramDocument))]
[JsonSerializable(typeof(TelegramVoice))]
[JsonSerializable(typeof(TelegramAudio))]
[JsonSerializable(typeof(TelegramVideo))]
[JsonSerializable(typeof(TelegramCallbackQuery))]
[JsonSerializable(typeof(TelegramEntity))]
[JsonSerializable(typeof(TelegramGetMeResponse))]
[JsonSerializable(typeof(TelegramBotInfo))]
[JsonSerializable(typeof(List<TelegramUpdate>))]
[JsonSerializable(typeof(TelegramSendMessageResponse))]
[JsonSerializable(typeof(TelegramSendChatActionResponse))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class TelegramJsonContext : JsonSerializerContext;