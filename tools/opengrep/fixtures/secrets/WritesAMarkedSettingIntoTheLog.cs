// Fixture for no-marked-setting-in-a-message and for
// an-outbound-failure-is-logged-by-class. This file is in no project and is
// never compiled; it exists so both rules can be watched refusing the mistakes
// they name.
//
// The near-miss in both cases is the sentence somebody writes while trying to
// be helpful. A refusal that quotes the value back is the obvious way to write
// a validation message, and handing the caught exception to the logger is the
// obvious way to report a failed send - it is what every other failure path in
// this plugin does, correctly, because their exceptions carry a local path and
// no marked value.

namespace Jellyfin.Plugin.Requests.Tests.Fixtures;

internal sealed class WritesAMarkedSettingIntoTheLog
{
    // Legal neighbours, left here on purpose, because a rule that also refused
    // these would make the mark unusable and the rule has to stay quiet on
    // them. Naming the setting is how an operator is told which box is wrong,
    // and reading the value in order to validate it is what the rules do.
    public static ConfigurationProblem NamesTheSettingAndNotTheValue(PluginConfiguration configuration)
    {
        if (IsSomewhereANoticeCanBePosted(configuration.OutboundNoticeAddress))
        {
            return null;
        }

        return new ConfigurationProblem
        {
            Setting = nameof(PluginConfiguration.OutboundNoticeAddress),
            Why = "OutboundNoticeAddress is not a complete http or https address."
        };
    }

    // The regression: the value reaching a composed message, in the three
    // spellings somebody would actually reach for.
    public static void QuotesItBack(PluginConfiguration configuration, ILogger logger)
    {
        var why = string.Format(
            CultureInfo.InvariantCulture,
            "OutboundNoticeAddress is \"{0}\", which is not a complete http or https address.",
            configuration.OutboundNoticeAddress);

        logger.LogWarning("Posting to {Address} was refused.", configuration.OutboundNoticeAddress);

        logger.LogDebug($"The sink is set to {configuration.OutboundNoticeAddress} on this install.");
    }

    // The regression the rule above cannot see: nothing here names the address,
    // and the exception carries it anyway.
    public static void HandsOverThePlatformsException(Exception reason, Guid requestId)
    {
        _logger.LogError(
            reason,
            "The notification sink could not deliver the notice about request {RequestId}.",
            requestId);
    }
}
