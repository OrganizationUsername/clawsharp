using Clawsharp.Core;

namespace Clawsharp.Providers.OpenAi;

/// <summary>
/// Fluent builder for constructing <see cref="MessageContent"/> from mixed multimodal inputs.
/// Shared by all providers that use the OpenAI-compatible content part format.
/// <para>
/// Usage: <c>MessageContentBuilder.Create().WithText("Hello").WithImages(images).Build()</c>
/// or the shorthand <c>MessageContentBuilder.From(chatMessage).Build()</c>.
/// </para>
/// </summary>
internal sealed class MessageContentBuilder
{
    private string? _text;
    private IReadOnlyList<ImageAttachment>? _images;
    private IReadOnlyList<FileAttachment>? _files;
    private IReadOnlyList<VideoAttachment>? _videos;
    private AudioAttachment? _audio;

    /// <summary>Creates a new empty builder.</summary>
    public static MessageContentBuilder Create() => new();

    /// <summary>
    /// Creates a builder pre-populated from a <see cref="ChatMessage"/>'s multimodal fields.
    /// </summary>
    public static MessageContentBuilder From(ChatMessage message) =>
        new MessageContentBuilder()
            .WithText(message.Content)
            .WithImages(message.Images)
            .WithFiles(message.Files)
            .WithVideos(message.Videos)
            .WithAudio(message.Audio);

    /// <summary>Sets the text content.</summary>
    public MessageContentBuilder WithText(string? text)
    {
        _text = text;
        return this;
    }

    /// <summary>Sets the image attachments.</summary>
    public MessageContentBuilder WithImages(IReadOnlyList<ImageAttachment>? images)
    {
        _images = images;
        return this;
    }

    /// <summary>Sets the file attachments (PDFs, text files, etc.).</summary>
    public MessageContentBuilder WithFiles(IReadOnlyList<FileAttachment>? files)
    {
        _files = files;
        return this;
    }

    /// <summary>Sets the video attachments.</summary>
    public MessageContentBuilder WithVideos(IReadOnlyList<VideoAttachment>? videos)
    {
        _videos = videos;
        return this;
    }

    /// <summary>Sets the audio attachment.</summary>
    public MessageContentBuilder WithAudio(AudioAttachment? audio)
    {
        _audio = audio;
        return this;
    }

    /// <summary>
    /// Builds the <see cref="MessageContent"/>. Returns a text-only content when no multimodal
    /// attachments are present, or a multipart content array otherwise.
    /// </summary>
    public MessageContent Build()
    {
        var hasImages = _images is { Count: > 0 };
        var hasFiles = _files is { Count: > 0 };
        var hasAudio = _audio is not null;
        var hasVideos = _videos is { Count: > 0 };

        if (!hasImages && !hasFiles && !hasAudio && !hasVideos)
        {
            return new MessageContent { Text = _text };
        }

        var partCount = (hasImages ? _images!.Count : 0)
                        + (hasFiles ? _files!.Count : 0)
                        + (hasAudio ? 1 : 0)
                        + (hasVideos ? _videos!.Count : 0)
                        + 1; // +1 for potential text part

        var parts = new List<ContentPart>(partCount);

        if (hasImages)
        {
            foreach (var img in _images!)
            {
                parts.Add(new ContentPart
                {
                    Type = "image_url",
                    ImageUrl = new ImageUrl { Url = $"data:{img.MimeType};base64,{img.Base64Data}" }
                });
            }
        }

        if (hasFiles)
        {
            foreach (var file in _files!)
            {
                parts.Add(new ContentPart
                {
                    Type = "file",
                    File = new FileContentData
                    {
                        Filename = file.Filename,
                        FileData = $"data:{file.MimeType};base64,{file.Base64Data}"
                    }
                });
            }
        }

        if (hasAudio)
        {
            parts.Add(new ContentPart
            {
                Type = "input_audio",
                InputAudio = new AudioContentData
                {
                    Data = _audio!.Base64Data,
                    Format = _audio.Format
                }
            });
        }

        if (hasVideos)
        {
            foreach (var video in _videos!)
            {
                parts.Add(new ContentPart
                {
                    Type = "video_url",
                    VideoUrl = new VideoUrl { Url = $"data:{video.MimeType};base64,{video.Base64Data}" }
                });
            }
        }

        if (_text is { Length: > 0 })
        {
            parts.Add(new ContentPart { Type = "text", Text = _text });
        }

        return new MessageContent { Parts = parts };
    }
}
