using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Clawsharp.Security;
using UglyToad.PdfPig;

using Clawsharp.Tools;
namespace Clawsharp.Tools.Ops;

public sealed class DocumentReadTool : Tool
{
    private const int MaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB

    private const int DefaultMaxChars = 50_000;

    private const int HardMaxChars = 200_000;

    private readonly string _workspace;

    private readonly AuditLogger? _auditLogger;

    public DocumentReadTool(string workspace, AuditLogger? auditLogger = null)
    {
        _workspace = Path.GetFullPath(workspace);
        _auditLogger = auditLogger;
    }

    public string? ChannelName => ToolRegistry.CurrentChannelName;

    public override string Name => "document_read";

    public override ToolSensitivity Sensitivity => ToolSensitivity.Low;

    public override string Description =>
        "Extract text from a document file (.pdf, .docx, .xlsx, .pptx). " +
        "Returns plain text content suitable for LLM processing.";

    public override string ParametersSchemaJson => """
                                                   {
                                                     "type": "object",
                                                     "properties": {
                                                       "path": {
                                                         "type": "string",
                                                         "description": "Path to document file (relative to workspace, or absolute)"
                                                       },
                                                       "max_chars": {
                                                         "type": "integer",
                                                         "description": "Maximum characters to return (default 50000, max 200000)"
                                                       }
                                                     },
                                                     "required": ["path"]
                                                   }
                                                   """;

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var inputPath = args.GetProperty("path").GetString() ?? "";
        var maxChars = args.TryGetProperty("max_chars", out var mc) ? mc.GetInt32() : DefaultMaxChars;
        maxChars = Math.Clamp(maxChars, 1, HardMaxChars);

        string resolvedPath;
        try
        {
            resolvedPath = PathGuard.SafeResolve(_workspace, inputPath);
        }
        catch (InvalidOperationException ex)
        {
            if (_auditLogger is not null)
            {
                _ = _auditLogger.LogFileAccessAsync(inputPath, "document_read", ChannelName, success: false, error: ex.Message, ct: ct);
            }

            return $"Error: {ex.Message}";
        }

        if (!File.Exists(resolvedPath))
        {
            return $"Error: file not found: {resolvedPath}";
        }

        var info = new FileInfo(resolvedPath);
        if (info.Length > MaxFileSizeBytes)
        {
            return $"Error: file too large ({info.Length / (1024 * 1024)} MB). Maximum is 50 MB.";
        }

        var ext = Path.GetExtension(resolvedPath).ToLowerInvariant();
        string text;
        try
        {
            text = ext switch
            {
                ".pdf" => await ExtractPdfAsync(resolvedPath, ct),
                ".docx" => ExtractDocx(resolvedPath),
                ".xlsx" => ExtractXlsx(resolvedPath),
                ".pptx" => ExtractPptx(resolvedPath),
                _ => $"Error: unsupported file type '{ext}'. Supported: .pdf, .docx, .xlsx, .pptx"
            };
        }
        catch (Exception ex)
        {
            return $"Error reading document: {ex.Message}";
        }

        if (text.StartsWith("Error:", StringComparison.Ordinal))
        {
            return text;
        }

        if (text.Length > maxChars)
        {
            text = text[..maxChars] + $"\n...[truncated at {maxChars:N0} chars of {text.Length:N0} total]";
        }

        if (_auditLogger is not null)
        {
            _ = _auditLogger.LogFileAccessAsync(resolvedPath, "document_read", ChannelName, success: true, ct: ct);
        }

        return text.Length > 0 ? text : "(document appears to be empty or contains no extractable text)";
    }

    private static Task<string> ExtractPdfAsync(string path, CancellationToken ct) =>
        Task.Run(() =>
        {
            var sb = new StringBuilder();
            using var pdf = PdfDocument.Open(path);
            foreach (var page in pdf.GetPages())
            {
                foreach (var word in page.GetWords())
                {
                    sb.Append(word.Text).Append(' ');
                }

                sb.AppendLine();
            }

            return sb.ToString().Trim();
        }, ct);

    private static string ExtractDocx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("word/document.xml");
        if (entry is null)
        {
            return "Error: not a valid .docx file (word/document.xml not found).";
        }

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        var paragraphs = doc.Descendants(w + "p")
                            .Select(p => string.Concat(p.Descendants(w + "t").Select(t => t.Value)));
        return string.Join("\n", paragraphs.Where(p => p.Length > 0));
    }

    private static string ExtractXlsx(string path)
    {
        using var zip = ZipFile.OpenRead(path);

        var sharedStrings = new List<string>();
        var ssEntry = zip.GetEntry("xl/sharedStrings.xml");
        if (ssEntry is not null)
        {
            using var ss = ssEntry.Open();
            var ssDoc = XDocument.Load(ss);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            sharedStrings.AddRange(ssDoc.Descendants(ns + "si")
                                        .Select(si => string.Concat(si.Descendants(ns + "t").Select(t => t.Value))));
        }

        var sb = new StringBuilder();
        var sheetEntries = zip.Entries
                              .Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal)
                                          && e.FullName.EndsWith(".xml", StringComparison.Ordinal))
                              .OrderBy(e => e.Name);

        foreach (var sheetEntry in sheetEntries)
        {
            sb.AppendLine($"[Sheet: {Path.GetFileNameWithoutExtension(sheetEntry.Name)}]");
            using var s = sheetEntry.Open();
            var sheet = XDocument.Load(s);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            foreach (var row in sheet.Descendants(ns + "row"))
            {
                var cells = row.Descendants(ns + "c").Select(c =>
                {
                    var t = c.Attribute("t")?.Value;
                    var vEl = c.Element(ns + "v");
                    if (vEl is null)
                    {
                        return "";
                    }

                    if (t == "s" && int.TryParse(vEl.Value, out var idx) && idx < sharedStrings.Count)
                    {
                        return sharedStrings[idx];
                    }

                    return vEl.Value;
                });
                sb.AppendLine(string.Join("\t", cells));
            }

            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    private static string ExtractPptx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";

        var sb = new StringBuilder();
        var slides = zip.Entries
                        .Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.Ordinal)
                                    && e.FullName.EndsWith(".xml", StringComparison.Ordinal))
                        .OrderBy(e => e.Name);

        int slideNum = 1;
        foreach (var slide in slides)
        {
            sb.AppendLine($"[Slide {slideNum++}]");
            using var s = slide.Open();
            var doc = XDocument.Load(s);
            var texts = doc.Descendants(a + "t").Select(t => t.Value);
            sb.AppendLine(string.Join(" ", texts));
            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }
}