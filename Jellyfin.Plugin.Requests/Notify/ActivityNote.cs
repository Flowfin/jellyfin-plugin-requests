using System;
using System.Globalization;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Notify;

/// <summary>
/// One line for the server's activity log, built from a move a request has already made.
/// <para>
/// It is a record of this plugin's own rather than the server's entity, for two reasons. The entity
/// is a database row with a settable identifier and a row version, so building one is a thing only
/// the code that talks to the host should do; and every rule this issue is actually about - what an
/// entry says, how long it is, what it may never carry - is a rule about text, which is testable
/// here and is not testable through a host nothing in this suite runs.
/// </para>
/// <para>
/// <b>An entry is built from four fields of the request and from nothing else.</b> The two states,
/// the identifier, the title snapshot and who made the move are all of it. That is what keeps the
/// third condition of #75 a property of this type rather than a thing to remember: the note text
/// nobody else may read is never reached for, the configuration is not in scope here at all, and no
/// path on the server's disk is either.
/// </para>
/// </summary>
public sealed record ActivityNote
{
    /// <summary>
    /// The longest a title may be inside an entry before the rest of it is dropped.
    /// <para>
    /// A title arrives from whoever asked and is capped nowhere else on the way here, so an entry
    /// built without this is one row of the operator's activity list carrying five hundred
    /// characters somebody typed. Sixty is a line, and what is cut is replaced by an ellipsis so a
    /// reader can see that something was.
    /// </para>
    /// </summary>
    public const int TitleMaximumLength = 60;

    /// <summary>
    /// Gets the line the dashboard shows. Short, because these sit among the server's own entries
    /// and everything else the operator's plugins have to say.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the second line, which carries the move and the identifier that finds the request again.
    /// </summary>
    public required string ShortOverview { get; init; }

    /// <summary>
    /// Gets the machine-readable word for what happened, which is what an operator filtering their
    /// activity list has to match on. It names this plugin and the state moved into, so a second
    /// plugin writing about its own requests does not collide with these.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Gets the Jellyfin user the entry is attributed to, or <see cref="Guid.Empty"/> where this
    /// plugin made the move on its own after looking at the library. The server's entity has no
    /// nullable user, so the empty identifier is what "nobody" is on that side, and the text says
    /// so in words rather than leaving a reader to know that.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Builds the entry for a move that has happened.
    /// <para>
    /// Both states are taken from the two requests rather than from the last history entry. The
    /// history is the model's record and this is the server's, and reading one out of the other
    /// would make an entry that is silently wrong the day a move appends two.
    /// </para>
    /// </summary>
    /// <param name="before">The request as it was read.</param>
    /// <param name="after">The request as the move left it.</param>
    /// <returns>The entry, or <see langword="null"/> where the two states are the same and nothing moved.</returns>
    /// <exception cref="ArgumentNullException">Where either request is absent.</exception>
    public static ActivityNote? For(MediaRequest before, MediaRequest after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        if (before.State == after.State)
        {
            // A write that changed something other than the state, which the fulfilment sweep makes
            // every time it observes availability without moving anything. An entry for one of those
            // is the wall of entries this issue says is worse than none.
            return null;
        }

        var by = after.StateChangedByUserId;

        return new ActivityNote
        {
            Name = string.Create(
                CultureInfo.InvariantCulture,
                $"Request {Word(after.State)}: {Shortened(after.DisplayTitle)}"),
            ShortOverview = by is null
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{before.State} to {after.State}, by the plugin rather than by a person. Request {after.Id}.")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{before.State} to {after.State}. Request {after.Id}."),
            Type = "MediaRequest" + after.State,
            UserId = by ?? Guid.Empty
        };
    }

    /// <summary>
    /// The title as it goes into an entry, cut to <see cref="TitleMaximumLength"/>.
    /// </summary>
    /// <param name="title">The title snapshot on the request.</param>
    /// <returns>The title, or as much of it as an entry carries.</returns>
    private static string Shortened(string title)
        => title.Length <= TitleMaximumLength
            ? title
            : string.Concat(title.AsSpan(0, TitleMaximumLength), "...");

    /// <summary>
    /// The state as the first line says it, which is a verb rather than the enumeration's own name.
    /// </summary>
    /// <param name="state">The state moved into.</param>
    /// <returns>The word.</returns>
    private static string Word(RequestState state) => state switch
    {
        RequestState.Open => "reopened",
        RequestState.Approved => "approved",
        RequestState.Declined => "declined",
        RequestState.Fulfilled => "fulfilled",
        RequestState.Failed => "failed",
        _ => "moved"
    };
}
