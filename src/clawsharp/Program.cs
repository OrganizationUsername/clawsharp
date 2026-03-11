using System.Diagnostics.CodeAnalysis;
using Clawsharp.Cli;
using Clawsharp.Cli.Audit;
using Clawsharp.Cli.Auth;
using Clawsharp.Cli.Channel;
using Clawsharp.Cli.Config;
using Clawsharp.Cli.Cost;
using Clawsharp.Cli.Cron;
using Clawsharp.Cli.Memory;
using Clawsharp.Cli.Migrate;
using Clawsharp.Cli.Models;
using Clawsharp.Cli.Pairing;
using Clawsharp.Cli.Service;
using Clawsharp.Cli.Session;
using Clawsharp.Cli.Skills;
using Spectre.Console.Cli;

// Spectre.Console.Cli uses reflection internally for command resolution.
// This is a known limitation documented in the Spectre.Console repository.
// All command types in this project are registered explicitly, so no members will be unexpectedly trimmed.
[assembly:
    UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Spectre.Console.Cli requires dynamic code. All command types are statically registered.")]

// Set up cancellation for Ctrl+C.
// First press triggers graceful shutdown; second press force-exits the process
// to guarantee termination even if a blocking call (e.g. Console.ReadLine) hangs.
using var cts = new CancellationTokenSource();
var shutdownRequested = 0;
Console.CancelKeyPress += (_, e) =>
{
    if (Interlocked.Increment(ref shutdownRequested) == 1)
    {
        e.Cancel = true;
        cts.Cancel();
        Console.WriteLine("\nShutdown requested... (press Ctrl+C again to force)");
    }
    else
    {
        Console.WriteLine("\nForcing exit.");
        Environment.Exit(1);
    }
};

var app = new CommandApp<AgentCommand>();

app.Configure(config =>
{
    config.SetApplicationName("clawsharp");
    config.SetApplicationVersion(
        typeof(Program).Assembly.GetName().Version?.ToString() ?? "dev");

    config.AddCommand<AgentCommand>("agent")
          .WithDescription("Start the gateway, or send a single-shot message with -m");
    config.AddCommand<GatewayCommand>("gateway")
          .WithDescription("Start the AI agent gateway");
    config.AddCommand<StatusCommand>("status")
          .WithDescription("Print a summary of the current configuration");
    config.AddCommand<DoctorCommand>("doctor")
          .WithDescription("Run health checks (exit 0 = all clear, 1 = warnings only, 2 = failures)");
    config.AddCommand<OnboardCommand>("onboard")
          .WithDescription("Write a starter config.json");

    config.AddBranch("config", cfg =>
    {
        cfg.SetDescription("Config file management");
        cfg.AddCommand<ConfigShowCommand>("show")
           .WithDescription("Show resolved configuration");
        cfg.AddCommand<ConfigValidateCommand>("validate")
           .WithDescription("Validate configuration (exit 0 = valid)");
        cfg.AddCommand<ConfigSetCommand>("set")
           .WithDescription("Set a config value (e.g. providers.openai.apiKey=sk-xxx)");
        cfg.AddCommand<EncryptSecretsCommand>("encrypt-secrets")
           .WithDescription("Encrypt all plaintext secret fields in config.json");
    });

    config.AddBranch("audit", audit =>
    {
        audit.SetDescription("Audit log management");
        audit.AddCommand<AuditTailCommand>("tail")
             .WithDescription("Show recent audit events");
        audit.AddCommand<AuditSearchCommand>("search")
             .WithDescription("Search audit events by type or channel");
    });

    config.AddBranch("cost", cost =>
    {
        cost.SetDescription("Cost tracking and budget information");
        cost.AddCommand<CostShowCommand>("show")
            .WithDescription("Show cost summary (daily, monthly, per-model)");
    });

    config.AddBranch("cron", cron =>
    {
        cron.SetDescription("Manage scheduled cron jobs");
        cron.AddCommand<CronListCommand>("list")
            .WithDescription("List all cron jobs");
        cron.AddCommand<CronAddCommand>("add")
            .WithDescription("Add a new cron job");
        cron.AddCommand<CronRemoveCommand>("remove")
            .WithDescription("Remove a cron job by ID prefix");
        cron.AddCommand<CronRunCommand>("run")
            .WithDescription("Show how to run a cron job immediately");
    });

    config.AddBranch("memory", mem =>
    {
        mem.SetDescription("Manage the memory store (facts and history)");
        mem.AddCommand<MemoryListCommand>("list")
           .WithDescription("List all stored facts");
        mem.AddCommand<MemorySearchCommand>("search")
           .WithDescription("Search facts by query string");
        mem.AddCommand<MemoryClearCommand>("clear")
           .WithDescription("Delete all facts and history");
        mem.AddCommand<MemoryExportCommand>("export")
           .WithDescription("Export all facts to a JSON file");
    });

    config.AddBranch("channel", channel =>
    {
        channel.SetDescription("Channel management");
        channel.AddCommand<ChannelStatusCommand>("status")
               .WithDescription("Show enabled/disabled state for all 8 channels");
        channel.AddCommand<ChannelPairWebCommand>("pair-web")
               .WithDescription("Request a new web pairing code from the running gateway");
    });

    config.AddBranch("session", sess =>
    {
        sess.SetDescription("Session management");
        sess.AddCommand<SessionListCommand>("list")
            .WithDescription("List all sessions");
        sess.AddCommand<SessionClearCommand>("clear")
            .WithDescription("Clear one or all sessions");
    });

    config.AddBranch("pairing", pairing =>
    {
        pairing.SetDescription("Manage DM pairing requests");
        pairing.AddCommand<PairingListCommand>("list")
               .WithDescription("List pending pairing requests");
        pairing.AddCommand<PairingApproveCommand>("approve")
               .WithDescription("Approve a pairing request by 6-digit code");
    });

    config.AddBranch("auth", auth =>
    {
        auth.SetDescription("Authentication management");
        auth.AddCommand<AuthLoginCopilotCommand>("login-copilot")
            .WithDescription("Log in to GitHub Copilot via device flow");
        auth.AddCommand<AuthStatusCommand>("status")
            .WithDescription("Show authentication status for all providers");
    });

    config.AddBranch("models", models =>
    {
        models.SetDescription("Model management");
        models.AddCommand<ModelsListCommand>("list")
              .WithDescription("List available models for each configured provider");
    });

    config.AddBranch("service", service =>
    {
        service.SetDescription("Manage the clawsharp system service");
        service.AddCommand<ServiceInstallCommand>("install")
               .WithDescription("Write and enable a service unit (systemd/launchd/SCM)");
        service.AddCommand<ServiceUninstallCommand>("uninstall")
               .WithDescription("Stop, disable and remove the service unit");
        service.AddCommand<ServiceStatusCommand>("status")
               .WithDescription("Show service status");
    });

    config.AddBranch("skills", skills =>
    {
        skills.SetDescription("Manage installed skills");
        skills.AddCommand<SkillsListCommand>("list")
              .WithDescription("List installed skills");
        skills.AddCommand<SkillsSearchCommand>("search")
              .WithDescription("Search available skills");
        skills.AddCommand<SkillsInstallCommand>("install")
              .WithDescription("Install a skill by name");
        skills.AddCommand<SkillsRemoveCommand>("remove")
              .WithDescription("Remove an installed skill");
    });

    config.AddBranch("migrate", migrate =>
    {
        migrate.SetDescription("Import config from another claw project");
        migrate.AddCommand<MigrateCommand>("openclaw")
               .WithDescription("Migrate config from openclaw to clawsharp");
    });

    config.AddCommand<CompletionCommand>("completion")
          .WithDescription("Generate shell completion scripts (bash/zsh/fish)");
});

return await app.RunAsync(args, cts.Token);