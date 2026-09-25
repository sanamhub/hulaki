using System;
using Microsoft.Extensions.Options;

namespace Hulaki.Teams;

/// <summary>Checks named <see cref="TeamsChannelOptions"/> at startup. Never echoes the URL.</summary>
internal sealed class TeamsChannelOptionsValidator : IValidateOptions<TeamsChannelOptions>
{
    public ValidateOptionsResult Validate(string? name, TeamsChannelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return TeamsChannel.Problem(options.WorkflowUrl) is { } problem
            ? ValidateOptionsResult.Fail($"Teams channel '{name}': {problem}")
            : ValidateOptionsResult.Success;
    }
}
