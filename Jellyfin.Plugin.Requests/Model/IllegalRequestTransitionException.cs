using System;
using System.Globalization;

namespace Jellyfin.Plugin.Requests.Model;

/// <summary>
/// Thrown when something asks for a move <see cref="RequestLifecycle.Table"/> refuses.
/// <para>
/// The two states are carried as values as well as being named in the message, so a caller can act
/// on them without reading English out of a string, and a log line says which move was attempted
/// rather than that one was. A refusal that says only "invalid state transition" sends whoever
/// reads it back to the code to work out which pair it meant.
/// </para>
/// </summary>
public sealed class IllegalRequestTransitionException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IllegalRequestTransitionException"/> class for
    /// a named pair of states.
    /// </summary>
    /// <param name="from">The state the move was out of.</param>
    /// <param name="to">The state the move was into.</param>
    /// <param name="why">The reason the table gives for refusing this pair.</param>
    public IllegalRequestTransitionException(RequestState from, RequestState to, string why)
        : base(Describe(from, to, why))
    {
        From = from;
        To = to;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IllegalRequestTransitionException"/> class.
    /// Present because the analyzers ask every exception for the three ordinary constructors; the
    /// constructor above is the one this type exists for, and it is the only one that can say which
    /// move was refused.
    /// </summary>
    public IllegalRequestTransitionException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IllegalRequestTransitionException"/> class with
    /// a message of the caller's own. See the note on the parameterless constructor.
    /// </summary>
    /// <param name="message">The message.</param>
    public IllegalRequestTransitionException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IllegalRequestTransitionException"/> class with
    /// a message and an inner exception. See the note on the parameterless constructor.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public IllegalRequestTransitionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets the state the refused move was out of, or <see cref="RequestState.Open"/> where this
    /// was built by one of the constructors that names no pair.
    /// </summary>
    public RequestState From { get; }

    /// <summary>
    /// Gets the state the refused move was into, or <see cref="RequestState.Open"/> where this was
    /// built by one of the constructors that names no pair.
    /// </summary>
    public RequestState To { get; }

    private static string Describe(RequestState from, RequestState to, string why)
        => string.Format(
            CultureInfo.InvariantCulture,
            "A request cannot move from {0} to {1}. {2}",
            from,
            to,
            why);
}
