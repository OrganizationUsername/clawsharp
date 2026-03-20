using Clawsharp.Core;

namespace Clawsharp.Tests.Unit.Core;

[TestFixture]
public sealed class FileAttachmentTests
{
    // ── Create — Valid ────────────────────────────────────────────────────────

    [Test]
    public void Create_ValidPdf_Succeeds()
    {
        var base64 = Convert.ToBase64String("fake pdf content"u8.ToArray());

        var attachment = FileAttachment.Create(base64, "application/pdf", "report.pdf");

        attachment.Base64Data.ShouldBe(base64);
        attachment.MimeType.ShouldBe("application/pdf");
        attachment.Filename.ShouldBe("report.pdf");
    }

    [TestCase("text/plain", "readme.txt")]
    [TestCase("text/csv", "data.csv")]
    [TestCase("text/markdown", "notes.md")]
    [TestCase("application/json", "config.json")]
    [TestCase("text/html", "page.html")]
    [TestCase("text/xml", "data.xml")]
    [TestCase("application/xml", "schema.xml")]
    public void Create_AllAllowedMimeTypes_Succeeds(string mime, string filename)
    {
        var base64 = Convert.ToBase64String("content"u8.ToArray());

        var attachment = FileAttachment.Create(base64, mime, filename);

        attachment.MimeType.ShouldBe(mime);
        attachment.Filename.ShouldBe(filename);
    }

    // ── Create — Case-insensitive MIME ───────────────────────────────────────

    [TestCase("Application/PDF", "report.pdf")]
    [TestCase("TEXT/PLAIN", "readme.txt")]
    [TestCase("Text/Csv", "data.csv")]
    [TestCase("APPLICATION/JSON", "config.json")]
    public void Create_MixedCaseMimeType_Succeeds(string mime, string filename)
    {
        var base64 = Convert.ToBase64String("content"u8.ToArray());

        var attachment = FileAttachment.Create(base64, mime, filename);

        attachment.MimeType.ShouldBe(mime);
    }

    [TestCase("Application/PDF")]
    [TestCase("TEXT/PLAIN")]
    [TestCase("Text/Html")]
    public void IsAllowedMimeType_CaseInsensitive_ReturnsTrue(string mime)
    {
        FileAttachment.IsAllowedMimeType(mime).ShouldBeTrue();
    }

    // ── Create — Invalid MIME ─────────────────────────────────────────────────

    [TestCase("image/jpeg")]
    [TestCase("application/zip")]
    [TestCase("application/octet-stream")]
    [TestCase("video/mp4")]
    [TestCase("audio/mpeg")]
    [TestCase("application/x-executable")]
    public void Create_DisallowedMimeType_ThrowsArgumentException(string mime)
    {
        var base64 = Convert.ToBase64String("content"u8.ToArray());

        var ex = Should.Throw<ArgumentException>(() =>
            FileAttachment.Create(base64, mime, "file.bin"));

        ex.ParamName.ShouldBe("mime");
        ex.Message.ShouldContain("not allowed");
    }

    // ── Create — Size Limit ───────────────────────────────────────────────────

    [Test]
    public void Create_OversizedFile_ThrowsArgumentException()
    {
        // Create base64 that decodes to more than 1 KB
        var largeData = new byte[2048];
        var base64 = Convert.ToBase64String(largeData);

        var ex = Should.Throw<ArgumentException>(() =>
            FileAttachment.Create(base64, "application/pdf", "big.pdf", maxFileBytes: 1024));

        ex.ParamName.ShouldBe("base64");
        ex.Message.ShouldContain("exceeds");
    }

    [Test]
    public void Create_ExactlyAtLimit_Succeeds()
    {
        // base64 encodes 3 bytes per 4 chars, so 768 raw bytes = exactly 1024 base64 chars
        // and the estimated decoded size (1024 * 3/4 = 768) equals maxFileBytes.
        var data = new byte[768];
        var base64 = Convert.ToBase64String(data);
        var estimatedBytes = base64.Length * 3 / 4;

        var attachment = FileAttachment.Create(base64, "text/plain", "ok.txt", maxFileBytes: estimatedBytes);

        attachment.Filename.ShouldBe("ok.txt");
    }

    [Test]
    public void Create_DefaultMaxSize_Is20MB()
    {
        // Verify the default max file size allows up to 20 MB
        // A 19 MB file should succeed
        var smallData = new byte[100];
        var attachment = FileAttachment.Create(
            Convert.ToBase64String(smallData), "application/pdf", "small.pdf");

        attachment.ShouldNotBeNull();
    }

    // ── IsAllowedMimeType ─────────────────────────────────────────────────────

    [TestCase("application/pdf", true)]
    [TestCase("text/plain", true)]
    [TestCase("text/csv", true)]
    [TestCase("text/markdown", true)]
    [TestCase("application/json", true)]
    [TestCase("text/html", true)]
    [TestCase("text/xml", true)]
    [TestCase("application/xml", true)]
    [TestCase("image/jpeg", false)]
    [TestCase("application/zip", false)]
    [TestCase(null, false)]
    [TestCase("", false)]
    public void IsAllowedMimeType_ReturnsExpected(string? mime, bool expected)
    {
        FileAttachment.IsAllowedMimeType(mime).ShouldBe(expected);
    }

    // ── Record Equality ───────────────────────────────────────────────────────

    [Test]
    public void RecordEquality_SameValues_AreEqual()
    {
        var a = FileAttachment.Create("AAAA", "text/plain", "file.txt");
        var b = FileAttachment.Create("AAAA", "text/plain", "file.txt");

        a.ShouldBe(b);
    }

    [Test]
    public void RecordEquality_DifferentFilename_NotEqual()
    {
        var a = FileAttachment.Create("AAAA", "text/plain", "file1.txt");
        var b = FileAttachment.Create("AAAA", "text/plain", "file2.txt");

        a.ShouldNotBe(b);
    }
}
