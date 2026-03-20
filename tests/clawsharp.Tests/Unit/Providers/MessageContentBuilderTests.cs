using Clawsharp.Core;
using Clawsharp.Providers.OpenAi;

namespace Clawsharp.Tests.Unit.Providers;

/// <summary>
/// Tests for <see cref="MessageContentBuilder"/> — fluent builder for multimodal message content.
/// </summary>
[TestFixture]
public sealed class MessageContentBuilderTests
{
    // ── Text-only ──────────────────────────────────────────────────────────────

    [Test]
    public void TextOnly_ReturnsStringContent()
    {
        var content = MessageContentBuilder.Create()
            .WithText("Hello world")
            .Build();

        content.Text.ShouldBe("Hello world");
        content.Parts.ShouldBeNull();
    }

    [Test]
    public void NullText_NoAttachments_ReturnsNullTextContent()
    {
        var content = MessageContentBuilder.Create()
            .WithText(null)
            .Build();

        content.Text.ShouldBeNull();
        content.Parts.ShouldBeNull();
    }

    [Test]
    public void EmptyText_NoAttachments_ReturnsEmptyTextContent()
    {
        var content = MessageContentBuilder.Create()
            .WithText("")
            .Build();

        content.Text.ShouldBe("");
        content.Parts.ShouldBeNull();
    }

    [Test]
    public void NoWithCalls_ReturnsNullTextContent()
    {
        var content = MessageContentBuilder.Create().Build();

        content.Text.ShouldBeNull();
        content.Parts.ShouldBeNull();
    }

    // ── Images ─────────────────────────────────────────────────────────────────

    [Test]
    public void WithImages_ProducesImageUrlParts()
    {
        var images = new[]
        {
            new ImageAttachment { Base64Data = "abc123", MimeType = "image/png" },
            new ImageAttachment { Base64Data = "def456", MimeType = "image/jpeg" }
        };

        var content = MessageContentBuilder.Create()
            .WithText("Describe these")
            .WithImages(images)
            .Build();

        content.Parts.ShouldNotBeNull();
        content.Parts!.Count.ShouldBe(3); // 2 images + 1 text

        content.Parts[0].Type.ShouldBe("image_url");
        content.Parts[0].ImageUrl.ShouldNotBeNull();
        content.Parts[0].ImageUrl!.Url.ShouldBe("data:image/png;base64,abc123");

        content.Parts[1].Type.ShouldBe("image_url");
        content.Parts[1].ImageUrl!.Url.ShouldBe("data:image/jpeg;base64,def456");

        content.Parts[2].Type.ShouldBe("text");
        content.Parts[2].Text.ShouldBe("Describe these");
    }

    [Test]
    public void WithImages_EmptyList_ReturnsTextOnly()
    {
        var content = MessageContentBuilder.Create()
            .WithText("Hello")
            .WithImages([])
            .Build();

        content.Text.ShouldBe("Hello");
        content.Parts.ShouldBeNull();
    }

    [Test]
    public void WithImages_NullList_ReturnsTextOnly()
    {
        var content = MessageContentBuilder.Create()
            .WithText("Hello")
            .WithImages(null)
            .Build();

        content.Text.ShouldBe("Hello");
        content.Parts.ShouldBeNull();
    }

    // ── Files ──────────────────────────────────────────────────────────────────

    [Test]
    public void WithFiles_ProducesFileParts()
    {
        var files = new[]
        {
            new FileAttachment { Base64Data = "pdfdata", MimeType = "application/pdf", Filename = "doc.pdf" }
        };

        var content = MessageContentBuilder.Create()
            .WithText("Summarize this")
            .WithFiles(files)
            .Build();

        content.Parts.ShouldNotBeNull();
        content.Parts!.Count.ShouldBe(2); // 1 file + 1 text

        content.Parts[0].Type.ShouldBe("file");
        content.Parts[0].File.ShouldNotBeNull();
        content.Parts[0].File!.Filename.ShouldBe("doc.pdf");
        content.Parts[0].File.FileData.ShouldBe("data:application/pdf;base64,pdfdata");

        content.Parts[1].Type.ShouldBe("text");
        content.Parts[1].Text.ShouldBe("Summarize this");
    }

    // ── Audio ──────────────────────────────────────────────────────────────────

    [Test]
    public void WithAudio_ProducesInputAudioPart()
    {
        var audio = AudioAttachment.FromBase64("audiodata", "wav");

        var content = MessageContentBuilder.Create()
            .WithText("Transcribe this")
            .WithAudio(audio)
            .Build();

        content.Parts.ShouldNotBeNull();
        content.Parts!.Count.ShouldBe(2); // 1 audio + 1 text

        content.Parts[0].Type.ShouldBe("input_audio");
        content.Parts[0].InputAudio.ShouldNotBeNull();
        content.Parts[0].InputAudio!.Data.ShouldBe("audiodata");
        content.Parts[0].InputAudio.Format.ShouldBe("wav");

        content.Parts[1].Type.ShouldBe("text");
    }

    [Test]
    public void WithAudio_Null_ReturnsTextOnly()
    {
        var content = MessageContentBuilder.Create()
            .WithText("Hello")
            .WithAudio(null)
            .Build();

        content.Text.ShouldBe("Hello");
        content.Parts.ShouldBeNull();
    }

    // ── Videos ─────────────────────────────────────────────────────────────────

    [Test]
    public void WithVideos_ProducesVideoUrlParts()
    {
        var videos = new[]
        {
            new VideoAttachment { Base64Data = "videodata", MimeType = "video/mp4" }
        };

        var content = MessageContentBuilder.Create()
            .WithText("What happens in this video?")
            .WithVideos(videos)
            .Build();

        content.Parts.ShouldNotBeNull();
        content.Parts!.Count.ShouldBe(2); // 1 video + 1 text

        content.Parts[0].Type.ShouldBe("video_url");
        content.Parts[0].VideoUrl.ShouldNotBeNull();
        content.Parts[0].VideoUrl!.Url.ShouldBe("data:video/mp4;base64,videodata");

        content.Parts[1].Type.ShouldBe("text");
    }

    // ── Mixed multimodal ───────────────────────────────────────────────────────

    [Test]
    public void AllContentTypes_ProducesCorrectOrder()
    {
        var images = new[] { new ImageAttachment { Base64Data = "img", MimeType = "image/png" } };
        var files = new[] { new FileAttachment { Base64Data = "pdf", MimeType = "application/pdf", Filename = "f.pdf" } };
        var videos = new[] { new VideoAttachment { Base64Data = "vid", MimeType = "video/mp4" } };
        var audio = AudioAttachment.FromBase64("aud", "mp3");

        var content = MessageContentBuilder.Create()
            .WithText("Analyze all of this")
            .WithImages(images)
            .WithFiles(files)
            .WithAudio(audio)
            .WithVideos(videos)
            .Build();

        content.Parts.ShouldNotBeNull();
        content.Parts!.Count.ShouldBe(5); // 1 image + 1 file + 1 audio + 1 video + 1 text

        // Order: images, files, audio, videos, text
        content.Parts[0].Type.ShouldBe("image_url");
        content.Parts[1].Type.ShouldBe("file");
        content.Parts[2].Type.ShouldBe("input_audio");
        content.Parts[3].Type.ShouldBe("video_url");
        content.Parts[4].Type.ShouldBe("text");
    }

    [Test]
    public void MultipleImagesAndFiles_AllIncluded()
    {
        var images = new[]
        {
            new ImageAttachment { Base64Data = "img1", MimeType = "image/png" },
            new ImageAttachment { Base64Data = "img2", MimeType = "image/jpeg" }
        };
        var files = new[]
        {
            new FileAttachment { Base64Data = "f1", MimeType = "application/pdf", Filename = "a.pdf" },
            new FileAttachment { Base64Data = "f2", MimeType = "text/plain", Filename = "b.txt" }
        };

        var content = MessageContentBuilder.Create()
            .WithText("Review")
            .WithImages(images)
            .WithFiles(files)
            .Build();

        content.Parts.ShouldNotBeNull();
        content.Parts!.Count.ShouldBe(5); // 2 images + 2 files + 1 text

        content.Parts.Count(p => p.Type == "image_url").ShouldBe(2);
        content.Parts.Count(p => p.Type == "file").ShouldBe(2);
        content.Parts.Count(p => p.Type == "text").ShouldBe(1);
    }

    [Test]
    public void AttachmentsOnly_NoText_OmitsTextPart()
    {
        var images = new[] { new ImageAttachment { Base64Data = "img", MimeType = "image/png" } };

        var content = MessageContentBuilder.Create()
            .WithImages(images)
            .Build();

        content.Parts.ShouldNotBeNull();
        content.Parts!.Count.ShouldBe(1); // image only, no text
        content.Parts[0].Type.ShouldBe("image_url");
    }

    [Test]
    public void AttachmentsWithEmptyText_OmitsTextPart()
    {
        var files = new[] { new FileAttachment { Base64Data = "data", MimeType = "application/pdf", Filename = "x.pdf" } };

        var content = MessageContentBuilder.Create()
            .WithText("")
            .WithFiles(files)
            .Build();

        content.Parts.ShouldNotBeNull();
        content.Parts!.Count.ShouldBe(1); // file only
        content.Parts[0].Type.ShouldBe("file");
    }

    // ── From(ChatMessage) ──────────────────────────────────────────────────────

    [Test]
    public void From_ChatMessage_TextOnly()
    {
        var msg = new ChatMessage(MessageRole.User, "Hello");

        var content = MessageContentBuilder.From(msg).Build();

        content.Text.ShouldBe("Hello");
        content.Parts.ShouldBeNull();
    }

    [Test]
    public void From_ChatMessage_WithAllAttachments()
    {
        var msg = new ChatMessage(
            MessageRole.User,
            "Analyze",
            Images: [new ImageAttachment { Base64Data = "i", MimeType = "image/png" }],
            Files: [new FileAttachment { Base64Data = "f", MimeType = "application/pdf", Filename = "x.pdf" }],
            Videos: [new VideoAttachment { Base64Data = "v", MimeType = "video/mp4" }],
            Audio: AudioAttachment.FromBase64("a", "wav"));

        var content = MessageContentBuilder.From(msg).Build();

        content.Parts.ShouldNotBeNull();
        content.Parts!.Count.ShouldBe(5);
        content.Parts[0].Type.ShouldBe("image_url");
        content.Parts[1].Type.ShouldBe("file");
        content.Parts[2].Type.ShouldBe("input_audio");
        content.Parts[3].Type.ShouldBe("video_url");
        content.Parts[4].Type.ShouldBe("text");
        content.Parts[4].Text.ShouldBe("Analyze");
    }

    [Test]
    public void From_ChatMessage_NullContent_NullAttachments()
    {
        var msg = new ChatMessage(MessageRole.User, null);

        var content = MessageContentBuilder.From(msg).Build();

        content.Text.ShouldBeNull();
        content.Parts.ShouldBeNull();
    }

    // ── Fluency ────────────────────────────────────────────────────────────────

    [Test]
    public void FluentChaining_ReturnsSameInstance()
    {
        var builder = MessageContentBuilder.Create();

        var result = builder
            .WithText("x")
            .WithImages(null)
            .WithFiles(null)
            .WithVideos(null)
            .WithAudio(null);

        result.ShouldBeSameAs(builder);
    }

    [Test]
    public void Builder_CanBeReused_WithDifferentInputs()
    {
        var builder = MessageContentBuilder.Create();

        var content1 = builder.WithText("First").Build();
        content1.Text.ShouldBe("First");

        var content2 = builder.WithText("Second").Build();
        content2.Text.ShouldBe("Second");
    }

    // ── Data URL formatting ────────────────────────────────────────────────────

    [Test]
    public void ImageDataUrl_CorrectFormat()
    {
        var content = MessageContentBuilder.Create()
            .WithImages([new ImageAttachment { Base64Data = "R0lGODlh", MimeType = "image/gif" }])
            .Build();

        content.Parts![0].ImageUrl!.Url.ShouldBe("data:image/gif;base64,R0lGODlh");
    }

    [Test]
    public void FileDataUrl_CorrectFormat()
    {
        var content = MessageContentBuilder.Create()
            .WithFiles([new FileAttachment { Base64Data = "JVBERi0", MimeType = "application/pdf", Filename = "doc.pdf" }])
            .Build();

        content.Parts![0].File!.FileData.ShouldBe("data:application/pdf;base64,JVBERi0");
        content.Parts[0].File.Filename.ShouldBe("doc.pdf");
    }

    [Test]
    public void VideoDataUrl_CorrectFormat()
    {
        var content = MessageContentBuilder.Create()
            .WithVideos([new VideoAttachment { Base64Data = "AAAA", MimeType = "video/webm" }])
            .Build();

        content.Parts![0].VideoUrl!.Url.ShouldBe("data:video/webm;base64,AAAA");
    }

    [Test]
    public void AudioFormat_PassedCorrectly()
    {
        var content = MessageContentBuilder.Create()
            .WithAudio(AudioAttachment.FromBase64("AAAA", "flac"))
            .Build();

        content.Parts![0].InputAudio!.Data.ShouldBe("AAAA");
        content.Parts[0].InputAudio.Format.ShouldBe("flac");
    }
}
