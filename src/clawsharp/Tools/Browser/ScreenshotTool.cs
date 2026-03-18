using System.Diagnostics;
using System.Text.Json;
using Clawsharp.Security;

namespace Clawsharp.Tools.Browser;

public sealed class ScreenshotTool(string workspace, AuditLogger? auditLogger = null) : Tool
{
    private readonly string _workspace = Path.GetFullPath(workspace);

    public string? ChannelName => ToolRegistry.CurrentChannelName;

    public override string Name => "screenshot";

    public override ToolSensitivity Sensitivity => ToolSensitivity.Low;

    public override string Description =>
        "Capture a screenshot of the current display and save it to the workspace. " +
        "Returns the path to the saved PNG file. Requires scrot (Linux) or built-in screencapture (macOS).";

    public override string ParametersSchemaJson => """
                                                   {
                                                     "type": "object",
                                                     "properties": {
                                                       "filename": {
                                                         "type": "string",
                                                         "description": "Output filename without extension (default: screenshot-{timestamp})"
                                                       }
                                                     }
                                                   }
                                                   """;

    public override async Task<string> ExecuteAsync(JsonElement args, CancellationToken ct = default)
    {
        var screenshotsDir = Path.Combine(_workspace, "screenshots");
        Directory.CreateDirectory(screenshotsDir);

        string filename;
        if (args.TryGetProperty("filename", out var fnEl) && fnEl.GetString() is { Length: > 0 } fn)
        {
            filename = fn;
        }
        else
        {
            filename = $"screenshot-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
        }

        // Sanitize filename: strip path separators
        filename = string.Concat(filename.Where(c => c != '/' && c != '\\' && c != ':'));
        if (string.IsNullOrWhiteSpace(filename))
        {
            filename = $"screenshot-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
        }

        var outputPath = Path.Combine(screenshotsDir, $"{filename}.png");

        var (program, arguments) = GetCaptureCommand(outputPath);
        if (program is null)
        {
            return "Error: screenshot capture is not available on this platform. Install 'scrot' (Linux) or run on macOS.";
        }

        var psi = new ProcessStartInfo
        {
            FileName = program,
            WorkingDirectory = screenshotsDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in arguments)
        {
            psi.ArgumentList.Add(a);
        }

        ShellGuard.SanitizeEnvironment(psi);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            using var proc = Process.Start(psi)
                             ?? throw new InvalidOperationException("Could not start capture process.");
            await proc.WaitForExitAsync(cts.Token);

            if (proc.ExitCode != 0)
            {
                var err = await proc.StandardError.ReadToEndAsync(cts.Token);
                return $"Error: capture failed (exit {proc.ExitCode}): {err.Trim()}";
            }

            if (!File.Exists(outputPath))
            {
                return "Error: capture command succeeded but output file was not created.";
            }

            var fileSize = new FileInfo(outputPath).Length;

            if (auditLogger is not null)
            {
                _ = auditLogger.LogFileAccessAsync(outputPath, "screenshot", ChannelName, success: true, ct: ct);
            }

            return $"Screenshot saved: {outputPath} ({fileSize / 1024} KB)";
        }
        catch (OperationCanceledException)
        {
            return "Error: screenshot timed out after 15s.";
        }
        catch (Exception)
        {
            return "Error: operation failed.";
        }
    }

    private static (string? Program, string[] Arguments) GetCaptureCommand(string outputPath)
    {
        if (OperatingSystem.IsLinux())
        {
            return ("scrot", [outputPath]);
        }

        if (OperatingSystem.IsMacOS())
        {
            return ("screencapture", ["-x", outputPath]);
        }

        if (OperatingSystem.IsWindows())
        {
            var psPath = outputPath.Replace("'", "''");
            var ps = $"Add-Type -Assembly System.Windows.Forms; " +
                     $"[System.Windows.Forms.Screen]::PrimaryScreen | " +
                     $"ForEach-Object {{ $b = New-Object System.Drawing.Bitmap($_.Bounds.Width,$_.Bounds.Height); " +
                     $"$g = [System.Drawing.Graphics]::FromImage($b); " +
                     $"$g.CopyFromScreen($_.Bounds.Location,[System.Drawing.Point]::Empty,$_.Bounds.Size); " +
                     $"$b.Save('{psPath}') }}";
            return ("powershell", ["-NonInteractive", "-Command", ps]);
        }

        return (null, []);
    }
}