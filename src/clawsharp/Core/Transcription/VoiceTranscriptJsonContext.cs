using System.Text.Json.Serialization;

namespace Clawsharp.Core.Transcription;

[JsonSerializable(typeof(VoiceTranscriptResult))]
[JsonSerializable(typeof(AzureFastTranscriptionResponse))]
[JsonSerializable(typeof(AzureCombinedPhrase))]
[JsonSerializable(typeof(AzurePhrase))]
[JsonSerializable(typeof(AzureTranscriptionDefinition))]
[JsonSerializable(typeof(AzureDiarizationOptions))]
[JsonSerializable(typeof(List<AzureCombinedPhrase>))]
[JsonSerializable(typeof(List<AzurePhrase>))]
[JsonSerializable(typeof(GcpSpeechRequest))]
[JsonSerializable(typeof(GcpSpeechConfig))]
[JsonSerializable(typeof(GcpAudioContent))]
[JsonSerializable(typeof(GcpSpeechResponse))]
[JsonSerializable(typeof(GcpSpeechResult))]
[JsonSerializable(typeof(GcpSpeechAlternative))]
[JsonSerializable(typeof(GcpWord))]
[JsonSerializable(typeof(List<GcpSpeechResult>))]
[JsonSerializable(typeof(List<GcpSpeechAlternative>))]
[JsonSerializable(typeof(List<GcpWord>))]
[JsonSerializable(typeof(List<string>))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class VoiceTranscriptJsonContext : JsonSerializerContext;