using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Clawsharp.Cli;

/// <summary>Generates shell completion scripts for bash, zsh, or fish.</summary>
[UsedImplicitly]
internal sealed class CompletionCommand : AsyncCommand<CompletionCommand.Settings>
{
    [UsedImplicitly]
    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<shell>")]
        public string Shell { get; init; } = "";
    }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var script = settings.Shell.ToLowerInvariant() switch
        {
            "bash" => BashScript,
            "zsh" => ZshScript,
            "fish" => FishScript,
            _ => null
        };

        if (script is null)
        {
            AnsiConsole.MarkupLine($"[red]Unknown shell:[/] {Markup.Escape(settings.Shell)}. Supported: bash, zsh, fish");
            return Task.FromResult(1);
        }

        Console.Out.Write(script);
        return Task.FromResult(0);
    }

    private const string BashScript =
        """
        _clawsharp_completions() {
            local cur prev
            COMPREPLY=()
            cur="${COMP_WORDS[COMP_CWORD]}"
            prev="${COMP_WORDS[COMP_CWORD-1]}"

            local commands="agent gateway status doctor onboard channel config session service cron memory models pairing completion"

            case "${prev}" in
                clawsharp)
                    COMPREPLY=( $(compgen -W "${commands}" -- "${cur}") )
                    return 0
                    ;;
                channel)
                    COMPREPLY=( $(compgen -W "status pair-web" -- "${cur}") )
                    return 0
                    ;;
                config)
                    COMPREPLY=( $(compgen -W "show validate set" -- "${cur}") )
                    return 0
                    ;;
                session)
                    COMPREPLY=( $(compgen -W "list clear" -- "${cur}") )
                    return 0
                    ;;
                service)
                    COMPREPLY=( $(compgen -W "install uninstall status" -- "${cur}") )
                    return 0
                    ;;
                cron)
                    COMPREPLY=( $(compgen -W "list add remove run" -- "${cur}") )
                    return 0
                    ;;
                memory)
                    COMPREPLY=( $(compgen -W "list search clear export" -- "${cur}") )
                    return 0
                    ;;
                models)
                    COMPREPLY=( $(compgen -W "list" -- "${cur}") )
                    return 0
                    ;;
                pairing)
                    COMPREPLY=( $(compgen -W "list approve" -- "${cur}") )
                    return 0
                    ;;
                completion)
                    COMPREPLY=( $(compgen -W "bash zsh fish" -- "${cur}") )
                    return 0
                    ;;
            esac
        }

        complete -F _clawsharp_completions clawsharp
        """;

    private const string ZshScript =
        """
        #compdef clawsharp

        _clawsharp() {
            local -a commands
            commands=(
                'agent:Start the AI assistant gateway'
                'gateway:Start the AI assistant gateway'
                'status:Show current status'
                'doctor:Run health checks'
                'onboard:Run the onboarding wizard'
                'channel:Manage channels'
                'config:Manage configuration'
                'session:Manage sessions'
                'service:Manage system service'
                'cron:Manage scheduled jobs'
                'memory:Manage memory'
                'models:List available models'
                'pairing:Manage DM pairing codes'
                'completion:Generate shell completions'
            )

            local -a subcommands

            case $words[2] in
                channel)
                    subcommands=('status:Show channel status' 'pair-web:Pair a web client')
                    _describe 'subcommand' subcommands
                    ;;
                config)
                    subcommands=('show:Show resolved config' 'validate:Validate config' 'set:Set a config value')
                    _describe 'subcommand' subcommands
                    ;;
                session)
                    subcommands=('list:List sessions' 'clear:Clear sessions')
                    _describe 'subcommand' subcommands
                    ;;
                service)
                    subcommands=('install:Install system service' 'uninstall:Remove system service' 'status:Show service status')
                    _describe 'subcommand' subcommands
                    ;;
                cron)
                    subcommands=('list:List cron jobs' 'add:Add a cron job' 'remove:Remove a cron job' 'run:Run a cron job now')
                    _describe 'subcommand' subcommands
                    ;;
                memory)
                    subcommands=('list:List all facts' 'search:Search facts' 'clear:Clear all facts' 'export:Export facts to file')
                    _describe 'subcommand' subcommands
                    ;;
                models)
                    subcommands=('list:List available models')
                    _describe 'subcommand' subcommands
                    ;;
                pairing)
                    subcommands=('list:List pending pairs' 'approve:Approve a pairing code')
                    _describe 'subcommand' subcommands
                    ;;
                completion)
                    subcommands=('bash:Generate bash completion' 'zsh:Generate zsh completion' 'fish:Generate fish completion')
                    _describe 'subcommand' subcommands
                    ;;
                *)
                    _describe 'command' commands
                    ;;
            esac
        }

        _clawsharp
        """;

    private const string FishScript =
        """
        # Fish completion for clawsharp
        complete -c clawsharp -f

        # Top-level commands
        complete -c clawsharp -n '__fish_use_subcommand' -a 'agent' -d 'Start the AI assistant gateway'
        complete -c clawsharp -n '__fish_use_subcommand' -a 'gateway' -d 'Start the AI assistant gateway'
        complete -c clawsharp -n '__fish_use_subcommand' -a 'status' -d 'Show current status'
        complete -c clawsharp -n '__fish_use_subcommand' -a 'doctor' -d 'Run health checks'
        complete -c clawsharp -n '__fish_use_subcommand' -a 'onboard' -d 'Run the onboarding wizard'
        complete -c clawsharp -n '__fish_use_subcommand' -a 'channel' -d 'Manage channels'
        complete -c clawsharp -n '__fish_use_subcommand' -a 'config' -d 'Manage configuration'
        complete -c clawsharp -n '__fish_use_subcommand' -a 'session' -d 'Manage sessions'
        complete -c clawsharp -n '__fish_use_subcommand' -a 'service' -d 'Manage system service'
        complete -c clawsharp -n '__fish_use_subcommand' -a 'cron' -d 'Manage scheduled jobs'
        complete -c clawsharp -n '__fish_use_subcommand' -a 'memory' -d 'Manage memory'
        complete -c clawsharp -n '__fish_use_subcommand' -a 'models' -d 'List available models'
        complete -c clawsharp -n '__fish_use_subcommand' -a 'pairing' -d 'Manage DM pairing codes'
        complete -c clawsharp -n '__fish_use_subcommand' -a 'completion' -d 'Generate shell completions'

        # channel subcommands
        complete -c clawsharp -n '__fish_seen_subcommand_from channel' -a 'status' -d 'Show channel status'
        complete -c clawsharp -n '__fish_seen_subcommand_from channel' -a 'pair-web' -d 'Pair a web client'

        # config subcommands
        complete -c clawsharp -n '__fish_seen_subcommand_from config' -a 'show' -d 'Show resolved config'
        complete -c clawsharp -n '__fish_seen_subcommand_from config' -a 'validate' -d 'Validate config'
        complete -c clawsharp -n '__fish_seen_subcommand_from config' -a 'set' -d 'Set a config value'

        # session subcommands
        complete -c clawsharp -n '__fish_seen_subcommand_from session' -a 'list' -d 'List sessions'
        complete -c clawsharp -n '__fish_seen_subcommand_from session' -a 'clear' -d 'Clear sessions'

        # service subcommands
        complete -c clawsharp -n '__fish_seen_subcommand_from service' -a 'install' -d 'Install system service'
        complete -c clawsharp -n '__fish_seen_subcommand_from service' -a 'uninstall' -d 'Remove system service'
        complete -c clawsharp -n '__fish_seen_subcommand_from service' -a 'status' -d 'Show service status'

        # cron subcommands
        complete -c clawsharp -n '__fish_seen_subcommand_from cron' -a 'list' -d 'List cron jobs'
        complete -c clawsharp -n '__fish_seen_subcommand_from cron' -a 'add' -d 'Add a cron job'
        complete -c clawsharp -n '__fish_seen_subcommand_from cron' -a 'remove' -d 'Remove a cron job'
        complete -c clawsharp -n '__fish_seen_subcommand_from cron' -a 'run' -d 'Run a cron job now'

        # memory subcommands
        complete -c clawsharp -n '__fish_seen_subcommand_from memory' -a 'list' -d 'List all facts'
        complete -c clawsharp -n '__fish_seen_subcommand_from memory' -a 'search' -d 'Search facts'
        complete -c clawsharp -n '__fish_seen_subcommand_from memory' -a 'clear' -d 'Clear all facts'
        complete -c clawsharp -n '__fish_seen_subcommand_from memory' -a 'export' -d 'Export facts to file'

        # models subcommands
        complete -c clawsharp -n '__fish_seen_subcommand_from models' -a 'list' -d 'List available models'

        # pairing subcommands
        complete -c clawsharp -n '__fish_seen_subcommand_from pairing' -a 'list' -d 'List pending pairs'
        complete -c clawsharp -n '__fish_seen_subcommand_from pairing' -a 'approve' -d 'Approve a pairing code'

        # completion subcommands
        complete -c clawsharp -n '__fish_seen_subcommand_from completion' -a 'bash' -d 'Generate bash completion'
        complete -c clawsharp -n '__fish_seen_subcommand_from completion' -a 'zsh' -d 'Generate zsh completion'
        complete -c clawsharp -n '__fish_seen_subcommand_from completion' -a 'fish' -d 'Generate fish completion'
        """;
}