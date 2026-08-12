namespace Jellyfin.Plugin.Requests.Configuration;

/// <summary>
/// One thing about a configuration this plugin cannot honour, named as the field an operator has to
/// change.
/// <para>
/// The field is carried as a value rather than left inside the sentence, so the settings page can
/// put the message beside the box somebody typed in without parsing English out of it. That is the
/// same shape <see cref="Api.RequestFailure.Field"/> uses for a body, and for the same reason: a
/// surface that has to read a message to work out which control was wrong reads it from an example.
/// </para>
/// </summary>
public sealed record ConfigurationProblem
{
    /// <summary>
    /// Gets the setting that is wrong, spelled as <see cref="PluginConfiguration"/> spells it, which
    /// is also how the stored file and the settings page spell it.
    /// </summary>
    public required string Setting { get; init; }

    /// <summary>
    /// Gets why this install cannot run on that value, in one sentence written for the operator who
    /// typed it. It says what the value has to be and what goes wrong otherwise, so somebody refused
    /// can fix it rather than guess at a number that will be accepted.
    /// </summary>
    public required string Why { get; init; }
}
