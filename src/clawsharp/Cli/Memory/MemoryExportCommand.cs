using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Clawsharp.Config;
using Clawsharp.Memory.Entities;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Clawsharp.Cli.Memory;

/// <summary>Exports all memory facts to a JSON file.</summary>
[UsedImplicitly]
public sealed class MemoryExportCommand : AsyncCommand<MemoryExportCommand.Settings>
{
    [UsedImplicitly]
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Output file path for the JSON export")]
        public string Path { get; set; } = "";
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "MemoryFactory creates EF Core DbContext types that are statically rooted in this project.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "MemoryFactory creates EF Core DbContext types that are statically rooted in this project.")]
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings,
                                                 CancellationToken cancellationToken)
    {
        var config = ClawsharpConfiguration.GetAppConfig();
        var memory = MemoryFactory.Create(config);
        var facts = await memory.ListFactsAsync(cancellationToken);

        if (facts.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey](no facts to export)[/]");
            return 0;
        }

        var outputPath = Path.GetFullPath(settings.Path);
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var list = new List<Fact>(facts);
        var json = JsonSerializer.Serialize(list, ConfigJsonContext.Default.ListFact);
        await File.WriteAllTextAsync(outputPath, json, cancellationToken);

        AnsiConsole.MarkupLine($"[green]Exported {facts.Count} fact(s) to[/] {Markup.Escape(outputPath)}");
        return 0;
    }
}