using Intellenum;

namespace Clawsharp.Core.Transcription;

/// <summary>Well-known voice transcription provider identifiers.</summary>
[Intellenum<string>]
internal partial class TranscriptionProvider
{
    public static readonly TranscriptionProvider Groq = new("groq");
    public static readonly TranscriptionProvider OpenAi = new("openai");
    public static readonly TranscriptionProvider Azure = new("azure");
    public static readonly TranscriptionProvider Gcp = new("gcp");
    public static readonly TranscriptionProvider AwsTranscribe = new("awstranscribe");
}
