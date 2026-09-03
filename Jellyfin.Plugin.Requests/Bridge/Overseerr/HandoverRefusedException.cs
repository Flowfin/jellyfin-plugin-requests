using System;

namespace Jellyfin.Plugin.Requests.Bridge.Overseerr;

/// <summary>
/// A handover the adapter refused to attempt, because the request or the mapping lacks something the
/// form cannot take a submission without.
/// <para>
/// It is a failure of the submission and not of the service, and it is raised before anything is
/// sent, so nothing over there has seen the request. <see cref="BridgeSubmission"/> treats it like
/// every other failed handover: the approval stands, the moment is written onto the request, the
/// operator's queue shows the request as approved and not handed over, and this exception's message
/// is what the log line beside it says about why. Nothing retries, because the reason does not go
/// away on its own; an operator adds what is missing and hands the request over again.
/// </para>
/// <para>
/// <c>docs/bridge.md</c> is where the first reason for it was decided: an approved request carrying
/// no TMDB identifier is refused here rather than being turned into a search of its title or being
/// refused at approval, and the paragraph there says what the other two answers would have cost.
/// </para>
/// </summary>
public sealed class HandoverRefusedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HandoverRefusedException"/> class.
    /// </summary>
    public HandoverRefusedException()
        : base("The handover was refused before anything was sent.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HandoverRefusedException"/> class.
    /// </summary>
    /// <param name="message">What was missing, in a sentence an operator can act on.</param>
    public HandoverRefusedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HandoverRefusedException"/> class.
    /// </summary>
    /// <param name="message">What was missing, in a sentence an operator can act on.</param>
    /// <param name="innerException">What raised it, where something did.</param>
    public HandoverRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
