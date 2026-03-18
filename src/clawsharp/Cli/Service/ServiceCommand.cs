using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Clawsharp.Cli.Service;

/// <summary>Spectre command: clawsharp service install [--system]</summary>
[UsedImplicitly]
public sealed class ServiceInstallCommand : AsyncCommand<ServiceInstallCommand.Settings>
{
    [UsedImplicitly]
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--system")]
        [Description("Install as system-wide service (requires root on Linux)")]
        [DefaultValue(false)]
        public bool System { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        => await ServiceCommand.InstallAsync(settings.System, cancellationToken);
}

/// <summary>Spectre command: clawsharp service uninstall [--system]</summary>
[UsedImplicitly]
public sealed class ServiceUninstallCommand : AsyncCommand<ServiceUninstallCommand.Settings>
{
    [UsedImplicitly]
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--system")]
        [Description("Uninstall the system-wide service (requires root on Linux)")]
        [DefaultValue(false)]
        public bool System { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        => await ServiceCommand.UninstallAsync(settings.System, cancellationToken);
}

/// <summary>Spectre command: clawsharp service status</summary>
[UsedImplicitly]
public sealed class ServiceStatusCommand : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        => await ServiceCommand.StatusAsync(cancellationToken);
}

/// <summary>
///     Implements `clawsharp service install | uninstall | status`.
///     Supports:
///     Linux   — systemd user unit (~/.config/systemd/user/) or system unit (/etc/systemd/system/)
///     macOS   — launchd user agent (~/Library/LaunchAgents/)
///     Windows — Windows Service Control Manager via sc.exe
/// </summary>
public static class ServiceCommand
{
    private const string ServiceName = "clawsharp";

    private const string ServiceDesc = "clawsharp — self-hosted AI assistant gateway";

    // ── Install ─────────────────────────────────────────────────────────────

    public static async Task<int> InstallAsync(bool system, CancellationToken ct = default)
    {
        var binaryPath = GetBinaryPath();
        if (binaryPath is null)
        {
            AnsiConsole.MarkupLine("[red][[service]][/] Cannot determine binary path. Are you running a published build?");
            return 1;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return await InstallSystemdAsync(binaryPath, system, ct);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return await InstallLaunchdAsync(binaryPath, ct);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return await InstallWindowsServiceAsync(binaryPath, ct);
        }

        AnsiConsole.MarkupLine("[red][[service]][/] Unsupported platform.");
        return 1;
    }

    // ── Uninstall ────────────────────────────────────────────────────────────

    public static async Task<int> UninstallAsync(bool system, CancellationToken ct = default)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return await UninstallSystemdAsync(system, ct);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return await UninstallLaunchdAsync(ct);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return await UninstallWindowsServiceAsync(ct);
        }

        AnsiConsole.MarkupLine("[red][[service]][/] Unsupported platform.");
        return 1;
    }

    // ── Status ────────────────────────────────────────────────────────────────

    public static async Task<int> StatusAsync(CancellationToken ct = default)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return await RunAsync("systemctl", $"--user status {ServiceName}", ct);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var plistPath = LaunchdPlistPath();
            if (File.Exists(plistPath))
            {
                AnsiConsole.MarkupLine($"[[service]] Service plist installed at: {Markup.Escape(plistPath)}");
            }
            else
            {
                AnsiConsole.MarkupLine("[[service]] Service not installed.");
            }
            return 0;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return await RunAsync("sc.exe", $"query {ServiceName}", ct);
        }

        AnsiConsole.MarkupLine("[red][[service]][/] Unsupported platform.");
        return 1;
    }

    // ── Linux / systemd ──────────────────────────────────────────────────────

    private static async Task<int> InstallSystemdAsync(string binaryPath, bool system, CancellationToken ct)
    {
        if (!HasSystemd())
        {
            AnsiConsole.MarkupLine("[red][[service]][/] systemd not found. Is this a systemd-based Linux?");
            return 1;
        }

        string unitDir;
        string systemctlArgs;

        if (system)
        {
            unitDir = "/etc/systemd/system";
            systemctlArgs = "--system";
            AnsiConsole.MarkupLine("[[service]] Installing system-level service (requires root).");
        }
        else
        {
            unitDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "systemd", "user");
            systemctlArgs = "--user";
        }

        Directory.CreateDirectory(unitDir);
        var unitPath = Path.Combine(unitDir, $"{ServiceName}.service");

        var configPath = GetConfigPath();
        var unit = SystemdUnit(binaryPath, configPath, system);

        await File.WriteAllTextAsync(unitPath, unit, ct);
        AnsiConsole.MarkupLine($"[[service]] Unit file written: {Markup.Escape(unitPath)}");

        if (await RunAsync("systemctl", $"{systemctlArgs} daemon-reload", ct) != 0)
        {
            return 1;
        }

        if (await RunAsync("systemctl", $"{systemctlArgs} enable {ServiceName}", ct) != 0)
        {
            return 1;
        }

        if (await RunAsync("systemctl", $"{systemctlArgs} start {ServiceName}", ct) != 0)
        {
            return 1;
        }

        AnsiConsole.MarkupLine($"[green][[service]][/] {ServiceName} installed and started.");
        AnsiConsole.MarkupLine($"[[service]] View logs: journalctl {systemctlArgs} -u {ServiceName} -f");
        return 0;
    }

    private static async Task<int> UninstallSystemdAsync(bool system, CancellationToken ct)
    {
        if (!HasSystemd())
        {
            AnsiConsole.MarkupLine("[red][[service]][/] systemd not found.");
            return 1;
        }

        var systemctlArgs = system ? "--system" : "--user";

        await RunAsync("systemctl", $"{systemctlArgs} stop {ServiceName}", ct);
        await RunAsync("systemctl", $"{systemctlArgs} disable {ServiceName}", ct);

        string unitDir;
        if (system)
        {
            unitDir = "/etc/systemd/system";
        }
        else
        {
            unitDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "systemd", "user");
        }

        var unitPath = Path.Combine(unitDir, $"{ServiceName}.service");
        if (File.Exists(unitPath))
        {
            File.Delete(unitPath);
            AnsiConsole.MarkupLine($"[[service]] Deleted: {Markup.Escape(unitPath)}");
        }

        await RunAsync("systemctl", $"{systemctlArgs} daemon-reload", ct);
        AnsiConsole.MarkupLine($"[green][[service]][/] {ServiceName} uninstalled.");
        return 0;
    }

    private static string SystemdUnit(string binaryPath, string? configPath, bool system)
    {
        string envLine;
        if (configPath is not null)
        {
            envLine = $"\nEnvironment=CLAWSHARP_CONFIG={configPath}";
        }
        else
        {
            envLine = "";
        }

        var wantedBy = system ? "multi-user.target" : "default.target";

        return $"""
                [Unit]
                Description={ServiceDesc}
                After=network.target

                [Service]
                Type=simple
                ExecStart={binaryPath} gateway{envLine}
                Restart=on-failure
                RestartSec=5
                StandardOutput=journal
                StandardError=journal

                [Install]
                WantedBy={wantedBy}
                """;
    }

    private static bool HasSystemd()
    {
        return File.Exists("/run/systemd/private") || Directory.Exists("/run/systemd/system");
    }

    // ── macOS / launchd ───────────────────────────────────────────────────────

    private static async Task<int> InstallLaunchdAsync(string binaryPath, CancellationToken ct)
    {
        var plistPath = LaunchdPlistPath();
        Directory.CreateDirectory(Path.GetDirectoryName(plistPath)!);

        var configPath = GetConfigPath();
        var plist = LaunchdPlist(binaryPath, configPath);

        await File.WriteAllTextAsync(plistPath, plist, ct);
        AnsiConsole.MarkupLine($"[[service]] Plist written: {Markup.Escape(plistPath)}");

        var label = LaunchdLabel();
        if (await RunAsync("launchctl", $"load -w {plistPath}", ct) != 0)
        {
            return 1;
        }

        AnsiConsole.MarkupLine($"[green][[service]][/] {Markup.Escape(label)} loaded and started.");
        AnsiConsole.MarkupLine($"[[service]] View logs: log show --predicate 'subsystem==\"{Markup.Escape(label)}\"' --last 1h");
        return 0;
    }

    private static async Task<int> UninstallLaunchdAsync(CancellationToken ct)
    {
        var plistPath = LaunchdPlistPath();
        if (!File.Exists(plistPath))
        {
            AnsiConsole.MarkupLine("[[service]] Service not installed.");
            return 0;
        }

        await RunAsync("launchctl", $"unload -w {plistPath}", ct);
        File.Delete(plistPath);
        AnsiConsole.MarkupLine($"[[service]] Deleted: {Markup.Escape(plistPath)}");
        return 0;
    }

    private static string LaunchdLabel()
    {
        return $"com.clawsharp.{ServiceName}";
    }

    private static string LaunchdPlistPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "LaunchAgents", $"{LaunchdLabel()}.plist");
    }

    private static string LaunchdPlist(string binaryPath, string? configPath)
    {
        string envBlock;
        if (configPath is not null)
        {
            envBlock = $"""
                            <key>EnvironmentVariables</key>
                            <dict>
                                <key>CLAWSHARP_CONFIG</key>
                                <string>{configPath}</string>
                            </dict>
                        """;
        }
        else
        {
            envBlock = "";
        }

        return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                    <key>Label</key>
                    <string>{LaunchdLabel()}</string>
                    <key>ProgramArguments</key>
                    <array>
                        <string>{binaryPath}</string>
                        <string>gateway</string>
                    </array>{envBlock}
                    <key>RunAtLoad</key>
                    <true/>
                    <key>KeepAlive</key>
                    <true/>
                    <key>StandardOutPath</key>
                    <string>{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "clawsharp.log")}</string>
                    <key>StandardErrorPath</key>
                    <string>{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "clawsharp-error.log")}</string>
                </dict>
                </plist>
                """;
    }

    // ── Windows / SCM ─────────────────────────────────────────────────────────

    private static async Task<int> InstallWindowsServiceAsync(string binaryPath, CancellationToken ct)
    {
        var code = await RunAsync("sc.exe",
            $"create {ServiceName} binPath= \"{binaryPath} gateway\" start= auto DisplayName= \"{ServiceDesc}\"", ct);
        if (code != 0)
        {
            return code;
        }

        await RunAsync("sc.exe", $"description {ServiceName} \"{ServiceDesc}\"", ct);

        code = await RunAsync("sc.exe", $"start {ServiceName}", ct);
        if (code != 0)
        {
            return code;
        }

        AnsiConsole.MarkupLine($"[green][[service]][/] {ServiceName} installed and started.");
        AnsiConsole.MarkupLine("[[service]] View logs: Get-EventLog -LogName Application -Source clawsharp");
        return 0;
    }

    private static async Task<int> UninstallWindowsServiceAsync(CancellationToken ct)
    {
        await RunAsync("sc.exe", $"stop {ServiceName}", ct);
        var code = await RunAsync("sc.exe", $"delete {ServiceName}", ct);
        if (code == 0)
        {
            AnsiConsole.MarkupLine($"[green][[service]][/] {ServiceName} uninstalled.");
        }

        return code;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? GetBinaryPath()
    {
        var path = Environment.ProcessPath;
        if (path is not null && File.Exists(path) && !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var dll = path?.Replace(".exe", ".dll", StringComparison.OrdinalIgnoreCase);
        if (dll is not null && File.Exists(dll))
        {
            var dotnet = FindDotnet();
            return dotnet is not null ? $"{dotnet} {dll}" : null;
        }

        return null;
    }

    private static string? FindDotnet()
    {
        foreach (var candidate in new[] { "dotnet", "/usr/bin/dotnet", "/usr/local/bin/dotnet" })
        {
            if (File.Exists(candidate) || candidate == "dotnet")
            {
                return candidate;
            }
        }

        return "dotnet";
    }

    private static string? GetConfigPath()
    {
        var env = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG");
        if (env is not null)
        {
            return env;
        }

        var local = Path.Combine(AppContext.BaseDirectory, "config.json");
        if (File.Exists(local))
        {
            return local;
        }

        return null;
    }

    private static async Task<int> RunAsync(string exe, string args, CancellationToken ct)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            });
            if (proc is null)
            {
                AnsiConsole.MarkupLine($"[red][[service]][/] Failed to start {Markup.Escape(exe)}");
                return 1;
            }

            await proc.WaitForExitAsync(ct);
            return proc.ExitCode;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red][[service]][/] {Markup.Escape(exe)} error: {Markup.Escape(ex.Message)}");
            return 1;
        }
    }
}