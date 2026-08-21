using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Fulfilment;

/// <summary>
/// The server's library, reduced to the two things this plugin asks of it: what it holds of one
/// title, and when it gained or lost something.
/// <para>
/// It is a seam of this plugin's own rather than the server's own interface, and the reason is
/// specific rather than a preference for indirection. The server's library interface is not the
/// same interface on the two server lines this plugin is built for: members exist on one and not
/// on the other. Anything standing in for it would therefore have to be written twice, once per
/// line, and a double that differs per line proves a different thing on each. This interface has
/// two members and they are the same on both lines, so one double stands for it everywhere and
/// every rule below it is decided by the suite rather than by a server.
/// </para>
/// <para>
/// What that leaves outside the suite is <see cref="ServerLibrary"/>, which is the translation
/// between this interface and the server's. It is named here rather than left to be discovered:
/// nothing in this repository runs it, and what it does is checked by reading it.
/// </para>
/// </summary>
public interface ILibrary
{
    /// <summary>
    /// Raised when the library gains or loses a title. The handler is called on whatever thread the
    /// server raised the event on, so a subscriber does its work elsewhere rather than holding up a
    /// library scan.
    /// </summary>
    event EventHandler<LibraryChangeEventArgs> Changed;

    /// <summary>
    /// What the server holds of one title.
    /// </summary>
    /// <param name="kind">What sort of thing is being asked about.</param>
    /// <param name="providerIds">
    /// The external identifiers to look it up by, keyed by provider name. A title matches where any
    /// one of them matches, for the reason <see cref="RequestIdentity"/> gives: a request may carry
    /// only the identifier the client that made it happened to know.
    /// </param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>
    /// What is held, or <see cref="LibraryHolding.Nothing"/> where the server has none of it. An
    /// empty set of identifiers is answered with <see cref="LibraryHolding.Nothing"/> rather than
    /// with an exception, because a library item carrying no identifier is ordinary.
    /// </returns>
    Task<LibraryHolding> HoldingOfAsync(
        RequestedItemKind kind,
        IReadOnlyDictionary<string, string> providerIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// What the server holds of one title, as one person is allowed to see it.
    /// <para>
    /// This is the same question as <see cref="HoldingOfAsync"/> asked on somebody's behalf, and the
    /// two are separate members because the answers are different facts. The sweep asks what the
    /// server has, because that decides whether a request was fulfilled and that is not a fact about
    /// any reader. A surface asks what a reader may see, because "it is here" about a title in a
    /// library that reader cannot open is a statement about that library, which is #71.
    /// </para>
    /// </summary>
    /// <param name="userId">The person the answer is for.</param>
    /// <param name="kind">What sort of thing is being asked about.</param>
    /// <param name="providerIds">
    /// The external identifiers to look it up by, keyed by provider name, matched the same way
    /// <see cref="HoldingOfAsync"/> matches them.
    /// </param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>
    /// What that person may see of it, or <see cref="LibraryHolding.Nothing"/> where they may see
    /// none of it. A title the server holds and this person may not open is answered exactly like a
    /// title the server does not hold, because that is the answer the server gives them everywhere
    /// else.
    /// </returns>
    Task<LibraryHolding> HoldingSeenByAsync(
        Guid userId,
        RequestedItemKind kind,
        IReadOnlyDictionary<string, string> providerIds,
        CancellationToken cancellationToken);
}
