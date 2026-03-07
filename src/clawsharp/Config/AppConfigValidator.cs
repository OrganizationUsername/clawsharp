using Microsoft.Extensions.Options;

namespace Clawsharp.Config;

/// <summary>
///     Cross-cutting <see cref="IValidateOptions{TOptions}"/> for <see cref="AppConfig"/>.
///     Delegates to <see cref="ConfigValidator.Validate"/> so runtime startup validation
///     and the CLI <c>config validate</c> command share a single source of truth.
/// </summary>
internal sealed class AppConfigValidator : IValidateOptions<AppConfig>
{
    public ValidateOptionsResult Validate(string? name, AppConfig options)
    {
        var errors = ConfigValidator.Validate(options);
        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}