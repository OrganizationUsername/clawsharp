using Clawsharp.Features.Chat.Commands;
using Clawsharp.Features.Chat.Queries;
using Clawsharp.Features.Cost.Commands;
using Clawsharp.Features.Cost.Queries;
using Clawsharp.Features.Memory.Commands;
using Clawsharp.Features.Memory.Queries;
using Clawsharp.Features.Session.Commands;
using Clawsharp.Features.Session.Queries;
using Clawsharp.Core;
using Clawsharp.Core.Pipeline;
using Clawsharp.Core.Services;
using Clawsharp.Core.Sessions;
using Clawsharp.Core.Utilities;

namespace Clawsharp.Core.Pipeline;

/// <summary>
///     Aggregate record that groups all Immediate.Handlers handler dependencies
///     required by <see cref="AgentLoop" />. Reduces the constructor parameter count
///     by replacing 13 individual handler parameters with a single aggregate.
/// </summary>
public sealed record AgentHandlers(
    LoadSession.Handler LoadSession,
    SaveSession.Handler SaveSession,
    ClearSession.Handler ClearSession,
    PruneSession.Handler PruneSession,
    CheckBudget.Handler CheckBudget,
    RecordUsage.Handler RecordUsage,
    GetCostSummary.Handler GetCostSummary,
    GetMemoryContext.Handler GetMemoryContext,
    ExtractFacts.Handler ExtractFacts,
    BuildChatRequest.Handler BuildChatRequest,
    RouteModel.Handler RouteModel,
    ApplySecurityGuards.Handler ApplySecurityGuards,
    SanitizeReply.Handler SanitizeReply);