using Microsoft.Extensions.Options;
using Clawsharp.Config.Agent;
using Clawsharp.Config.Memory;

namespace Clawsharp.Config;

/// <summary>Source-generated validator for <see cref="AgentDefaults"/>.</summary>
[OptionsValidator]
public partial class ValidateAgentDefaults : IValidateOptions<AgentDefaults>;

/// <summary>Source-generated validator for <see cref="AgentConfig"/>.</summary>
[OptionsValidator]
public partial class ValidateAgentConfig : IValidateOptions<AgentConfig>;

/// <summary>Source-generated validator for <see cref="MemoryConfig"/>.</summary>
[OptionsValidator]
public partial class ValidateMemoryConfig : IValidateOptions<MemoryConfig>;