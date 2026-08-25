using System;
using System.Globalization;
using Jellyfin.Plugin.Requests.Localisation;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Notify;

/// <summary>
/// What the person who asked is told, as the two pieces of text their own client shows and the one
/// identifier that says whose client that is.
/// <para>
/// It is a record of this plugin's own rather than the server's command type, for the reason
/// <see cref="ActivityNote"/> is one: every rule this is actually about - who it goes to, what it
/// says, what it never carries - is a rule about a message, which is testable here, and building the
/// host's command is the job of the one class that names the host.
/// </para>
/// <para>
/// <b>It names exactly one person and there is nowhere to put a second.</b> A message about somebody
/// else's request is the failure this whole surface has to avoid, and the shape is what keeps it
/// away rather than care at the two call sites: <see cref="ToUserId"/> is a single identifier, it is
/// read off the request rather than passed in, and the only thing it can ever be is whoever asked.
/// </para>
/// <para>
/// <b>What it deliberately does not carry.</b> Neither note. The requester's own note tells them
/// nothing they do not know, and the operator's note beside a decline can be five hundred characters
/// while this is a line that disappears by itself; that note is on the person's own page, which is
/// the surface that is still there tomorrow. Nobody else waiting for the same title, because a
/// message about one person's request is not a roster. No identifier of any other person, and no
/// name of any person at all, for the reason in <c>docs/personal-data.md</c>.
/// </para>
/// </summary>
public sealed record RequesterMessage
{
    /// <summary>
    /// The longest a title may be inside a message before the rest of it is dropped.
    /// <para>
    /// A title arrives from whoever asked and nothing caps it on the way here, so a message built
    /// without this is a client's notification area holding as much text as somebody felt like
    /// typing. What is cut is replaced by an ellipsis, so the reader can see that something was.
    /// </para>
    /// </summary>
    public const int TitleMaximumLength = 60;

    /// <summary>
    /// Gets the Jellyfin user this is for, which is always the person who asked for the request it
    /// is about.
    /// </summary>
    public required Guid ToUserId { get; init; }

    /// <summary>
    /// Gets the line above the message, which says that this is about a request rather than about
    /// anything else the person's client may be showing them.
    /// </summary>
    public required string Header { get; init; }

    /// <summary>
    /// Gets what happened, in one sentence, with the title in it.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// The message for a request that has just moved, or nothing where this plugin tells the person
    /// nothing about the state it moved into.
    /// <para>
    /// The mapping from a state to a sentence lives here rather than at the paths that move a
    /// request, for the reason <see cref="OutboundNotice.ForMove"/> gives: two copies of it are two
    /// answers the day a state is added, and the copy that is wrong is the one that sends somebody
    /// the wrong sentence about their own request.
    /// </para>
    /// <para>
    /// <b>A state with no arm sends nothing, and so does a decline carrying no reason.</b>
    /// <see cref="RequestState.Open"/> is where a request starts and there is nothing to tell
    /// anybody about arriving at it, <see cref="RequestState.Failed"/> has no sentence written for
    /// it, and a state added later arrives here with nobody having written the sentence for it.
    /// </para>
    /// <para>
    /// <b>Failed said no path reached it until the reconciliation landed, and now one does.</b> The
    /// run in #83 moves a request there on the service's word and no sentence is written for that
    /// state, so the person waiting is not told and finds out on their own page. That is a gap
    /// rather than a decision, and closing it is a sentence in the catalogue and an arm here rather
    /// than anything about this method's shape. A decline is required to carry a reason and the model is what holds it to that, so a
    /// declined request without one is a request written by something older than that rule; the
    /// sentence for it would have a hole where the reason goes, and sending nothing is what a person
    /// can recover from by opening their own page.
    /// </para>
    /// </summary>
    /// <param name="request">The request as the move left it.</param>
    /// <param name="words">The catalogue the sentences are read out of.</param>
    /// <returns>The message to push, or <see langword="null"/> where nothing is told for it.</returns>
    /// <exception cref="ArgumentNullException">Where there is no request or no catalogue.</exception>
    public static RequesterMessage? ForMove(MediaRequest request, StringCatalogue words)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(words);

        var title = Shortened(request.DisplayTitle);

        var text = request.State switch
        {
            RequestState.Approved => Filled(words, LiveSentences.Approved, title),
            RequestState.Fulfilled => Filled(words, LiveSentences.Fulfilled, title),
            RequestState.Declined when request.DeclineReason is DeclineReason reason
                => Filled(words, LiveSentences.Declined, title, Word(words, reason)),
            _ => null
        };

        if (text is null)
        {
            return null;
        }

        return new RequesterMessage
        {
            ToUserId = request.RequestedByUserId,
            Header = Read(words, LiveSentences.Header),
            Text = text
        };
    }

    /// <summary>
    /// One sentence out of the catalogue, with what goes in it.
    /// </summary>
    /// <param name="words">The catalogue.</param>
    /// <param name="key">Which sentence.</param>
    /// <param name="values">What fills its placeholders, in order.</param>
    /// <returns>The sentence as the person reads it.</returns>
    private static string Filled(StringCatalogue words, string key, params object[] values)
        => string.Format(CultureInfo.InvariantCulture, Read(words, key), values);

    /// <summary>
    /// One string out of the catalogue, in the one language this path has.
    /// <para>
    /// English, because nothing on this path says what the person reads. The server's session
    /// interface takes user identifiers and hands back nothing about a session, so at the moment
    /// this message is built there is no client, no device and no preference to ask, which is a
    /// property of that interface rather than a choice made here. <c>docs/notifications.md</c> says
    /// so plainly rather than leaving somebody to find it.
    /// </para>
    /// </summary>
    /// <param name="words">The catalogue.</param>
    /// <param name="key">Which string.</param>
    /// <returns>The string.</returns>
    private static string Read(StringCatalogue words, string key) => words.Get(key, culture: null);

    /// <summary>
    /// The reason a decline carries, as the word the person reads rather than the enumeration's own
    /// name. It is the same key a surface draws that reason under, so the two say the same thing.
    /// </summary>
    /// <param name="words">The catalogue.</param>
    /// <param name="reason">Why it was declined.</param>
    /// <returns>The word.</returns>
    private static string Word(StringCatalogue words, DeclineReason reason)
        => Read(words, "declineReason." + reason.ToString());

    /// <summary>
    /// The title as it goes into a message, cut to <see cref="TitleMaximumLength"/>.
    /// </summary>
    /// <param name="title">The title snapshot on the request.</param>
    /// <returns>The title, or as much of it as a message carries.</returns>
    private static string Shortened(string title)
        => title.Length <= TitleMaximumLength
            ? title
            : string.Concat(title.AsSpan(0, TitleMaximumLength), "...");
}
