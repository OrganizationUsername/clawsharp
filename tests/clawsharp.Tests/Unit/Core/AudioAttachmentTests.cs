using System.Text.Json;
using Clawsharp.Core;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Tests.Unit.Core;

/// <summary>
/// Unit tests for <see cref="AudioAttachment"/> — creation, MIME-to-format mapping, and JSON round-trip.
/// </summary>
[TestFixture]
public sealed class AudioAttachmentTests
{
    [Test]
    public void Create_FromBytes_ProducesCorrectBase64AndFormat()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var attachment = AudioAttachment.Create(bytes, "audio/wav");

        attachment.Base64Data.ShouldBe(Convert.ToBase64String(bytes));
        attachment.Format.ShouldBe("wav");
    }

    [Test]
    public void Create_FromBytes_OggMime_ProducesOggFormat()
    {
        var bytes = new byte[] { 10, 20 };
        var attachment = AudioAttachment.Create(bytes, "audio/ogg");

        attachment.Format.ShouldBe("ogg");
    }

    [Test]
    public void FromBase64_RetainsDataAndFormat()
    {
        var attachment = AudioAttachment.FromBase64("AQIDBA==", "mp3");

        attachment.Base64Data.ShouldBe("AQIDBA==");
        attachment.Format.ShouldBe("mp3");
    }

    [TestCase("audio/wav", "wav")]
    [TestCase("audio/x-wav", "wav")]
    [TestCase("audio/mpeg", "mp3")]
    [TestCase("audio/mp3", "mp3")]
    [TestCase("audio/ogg", "ogg")]
    [TestCase("application/ogg", "ogg")]
    [TestCase("audio/flac", "flac")]
    [TestCase("audio/aac", "aac")]
    [TestCase("audio/mp4", "m4a")]
    [TestCase("audio/m4a", "m4a")]
    [TestCase("audio/x-m4a", "m4a")]
    [TestCase("audio/webm", "webm")]
    [TestCase("audio/aiff", "aiff")]
    [TestCase("audio/x-aiff", "aiff")]
    public void MimeToFormat_MapsCorrectly(string mimeType, string expectedFormat)
    {
        AudioAttachment.MimeToFormat(mimeType).ShouldBe(expectedFormat);
    }

    [Test]
    public void MimeToFormat_UnknownType_DefaultsToWav()
    {
        AudioAttachment.MimeToFormat("audio/vnd.custom").ShouldBe("wav");
    }

    [Test]
    public void MimeToFormat_WithParameters_StripsBeforeMatching()
    {
        AudioAttachment.MimeToFormat("audio/ogg; codecs=opus").ShouldBe("ogg");
    }

    [Test]
    public void MimeToFormat_CaseInsensitive()
    {
        AudioAttachment.MimeToFormat("Audio/MPEG").ShouldBe("mp3");
    }

    [Test]
    public void Create_EmptyBytes_ProducesEmptyBase64()
    {
        var attachment = AudioAttachment.Create([], "audio/wav");

        attachment.Base64Data.ShouldBe("");
        attachment.Format.ShouldBe("wav");
    }

    [Test]
    public void JsonRoundTrip_PreservesFields()
    {
        var original = AudioAttachment.FromBase64("SGVsbG8=", "ogg");

        var json = JsonSerializer.Serialize(original, SessionJsonContext.Default.AudioAttachment);
        var deserialized = JsonSerializer.Deserialize(json, SessionJsonContext.Default.AudioAttachment);

        deserialized.ShouldNotBeNull();
        deserialized.Base64Data.ShouldBe("SGVsbG8=");
        deserialized.Format.ShouldBe("ogg");
    }

    [Test]
    public void JsonSerialization_UsesExpectedPropertyNames()
    {
        var attachment = AudioAttachment.FromBase64("AAAA", "wav");

        var json = JsonSerializer.Serialize(attachment, SessionJsonContext.Default.AudioAttachment);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("base64Data").GetString().ShouldBe("AAAA");
        root.GetProperty("format").GetString().ShouldBe("wav");
    }

    [Test]
    public void ChatMessage_WithAudio_CanBeCreated()
    {
        var audio = AudioAttachment.FromBase64("data", "mp3");
        var msg = new ChatMessage(MessageRole.User, "test", Audio: audio);

        msg.Audio.ShouldNotBeNull();
        msg.Audio!.Format.ShouldBe("mp3");
    }

    [Test]
    public void InboundMessage_WithAudio_CanBeCreated()
    {
        var audio = AudioAttachment.FromBase64("data", "ogg");
        var msg = new InboundMessage(
            ChannelName.Telegram,
            "123",
            "Test",
            "voice message",
            Audio: audio);

        msg.Audio.ShouldNotBeNull();
        msg.Audio!.Format.ShouldBe("ogg");
    }

    [Test]
    public void ChatMessage_WithoutAudio_HasNullAudio()
    {
        var msg = new ChatMessage(MessageRole.User, "test");
        msg.Audio.ShouldBeNull();
    }
}
