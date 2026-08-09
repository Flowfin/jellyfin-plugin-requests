using System;
using System.Globalization;

namespace Jellyfin.Plugin.Requests.Model;

/// <summary>
/// Thrown when a request that carries no provider identifier is asked to move somewhere that needs
/// one.
/// <para>
/// A request with no identifier is a title somebody typed. It has no identity, so
/// <see cref="RequestIdentity"/> never joins it to anything, a fulfilment check has nothing to match
/// it against, and a bridge has nothing to submit. Approving one is an operator saying yes to
/// something no part of the plugin can act on afterwards, and it would sit in the queue looking
/// decided while nothing at all was happening.
/// </para>
/// <para>
/// A decline is the one move that needs no identifier, and it stays open on purpose. An operator who
/// cannot tell what was asked for can still say so, with the reason, and the person who asked is
/// told. Refusing that as well would leave such a request with no ending at all.
/// </para>
/// <para>
/// The way out is the other direction: somebody puts the identifiers on the request, and then every
/// move is available. Which surface offers that is #52's and #61's, and nothing here decides it.
/// </para>
/// </summary>
public sealed class RequestNotIdentifiedException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequestNotIdentifiedException"/> class for the
    /// move that was refused.
    /// </summary>
    /// <param name="to">The state the move was into.</param>
    public RequestNotIdentifiedException(RequestState to)
        : base(Describe(to))
    {
        To = to;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestNotIdentifiedException"/> class. Present
    /// because the analyzers ask every exception for the three ordinary constructors; the
    /// constructor above is the one this type exists for.
    /// </summary>
    public RequestNotIdentifiedException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestNotIdentifiedException"/> class with a
    /// message of the caller's own. See the note on the parameterless constructor.
    /// </summary>
    /// <param name="message">The message.</param>
    public RequestNotIdentifiedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestNotIdentifiedException"/> class with a
    /// message and an inner exception. See the note on the parameterless constructor.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public RequestNotIdentifiedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets the state the refused move was into, or <see cref="RequestState.Open"/> where this was
    /// built by one of the constructors that names no move.
    /// </summary>
    public RequestState To { get; }

    private static string Describe(RequestState to)
        => string.Format(
            CultureInfo.InvariantCulture,
            "A request that carries no provider identifier cannot be moved to {0}. Nothing can match it in the library and nothing can be submitted for it, so the moves that need an identifier are refused until one is on the request. A decline needs none and stays available.",
            to);
}
