using System.Diagnostics.CodeAnalysis;
using Clawsharp.Config;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Clawsharp.Cli.Memory;

/// <summary>Lists all stored memory facts.</summary>
[UsedImplicitly]
public sealed class MemoryListCommand : AsyncCommand
{
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "MemoryFactory creates EF Core DbContext types that are statically rooted in this project.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "MemoryFactory creates EF Core DbContext types that are statically rooted in this project.")]
    public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var config = ClawsharpConfiguration.GetAppConfig();
        var memory = MemoryFactory.Create(config);
        var facts = await memory.ListFactsAsync(cancellationToken);

        if (facts.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey](no facts stored)[/]");
            return 0;
        }

        var table = new Table()
                    .Border(TableBorder.Simple)
                    .AddColumn(new TableColumn("ID").RightAligned())
                    .AddColumn("Content")
                    .AddColumn("Created");

        foreach (var fact in facts)
        {
            var created = fact.CreatedAt != default
                ? fact.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                : "-";
            table.AddRow(
                fact.Id.ToString(),
                Markup.Escape(Truncate(fact.Content, 80)),
                created);
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[grey]{facts.Count} fact(s) total.[/]");
        return 0;
    }

    private static string Truncate(string value, int maxLen) =>
        value.Length > maxLen ? string.Concat(value.AsSpan(0, maxLen - 1), "~") : value;
}