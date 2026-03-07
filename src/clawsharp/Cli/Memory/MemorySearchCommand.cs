using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Clawsharp.Config;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Clawsharp.Cli.Memory;

/// <summary>Searches memory facts by query string.</summary>
[UsedImplicitly]
public sealed class MemorySearchCommand : AsyncCommand<MemorySearchCommand.Settings>
{
    [UsedImplicitly]
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<query>")]
        [Description("Search query string")]
        public string Query { get; set; } = "";

        [CommandOption("-n|--limit")]
        [Description("Maximum number of results (default: 20)")]
        [DefaultValue(20)]
        public int Limit { get; set; } = 20;
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
        var results = await memory.SearchAsync(settings.Query, settings.Limit, cancellationToken);

        if (results.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey](no matching facts found)[/]");
            return 0;
        }

        var table = new Table()
                    .Border(TableBorder.Simple)
                    .AddColumn(new TableColumn("#").RightAligned())
                    .AddColumn("Content");

        for (var i = 0; i < results.Count; i++)
        {
            table.AddRow((i + 1).ToString(), Markup.Escape(results[i]));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[grey]{results.Count} result(s).[/]");
        return 0;
    }
}