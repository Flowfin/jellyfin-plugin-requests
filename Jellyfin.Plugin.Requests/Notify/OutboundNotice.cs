using System;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Notify;

/// <summary>
/// The document this plugin posts to an address an operator chose, and the whole of what leaves the
/// server on that path.
/// <para>
/// <b>It is a contract with somebody this repository will never see.</b> Whoever configures the sink
/// points it at a script, a chat bridge or an automation service they wrote, and that thing reads
/// these field names. So the names are declared here rather than derived from how the properties
/// happen to be spelled in C#, and <see cref="Version"/> is on every document rather than on the
/// ones that needed it, because a reader that has to guess which shape it is holding is a reader
/// that breaks on the first change.
/// </para>
/// <para>
/// <b>What it carries is the smallest set an operator can act on.</b> Which request, what happened
/// to it, when, what was asked for, and the identifiers of the person who asked and the person who
/// moved it. Anything beyond that is a field somebody else's machine ends up storing, and this
/// plugin is the reason it left.
/// </para>
/// <para>
/// <b>What it deliberately does not carry.</b> Neither note, because both are free text somebody
/// typed and either can hold anything at all, including a name or an address that this plugin has no
/// business forwarding. The provider identifiers, because they place the title in third-party
/// catalogues and the display title and year are what a person reads. Everybody who joined the
/// request, because a notice about one movement is not a roster of everybody waiting. The history,
/// because it is the record and this is a message. No user name of any kind appears, for the reason
/// in <c>docs/personal-data.md</c>: this plugin holds a person as the server's identifier and never
/// as a name, so it has none to send.
/// </para>
/// </summary>
public sealed record OutboundNotice
{
    /// <summary>
    /// The version this plugin writes today.
    /// <para>
    /// It moves when a reader that understood the old document would misread the new one, which is a
    /// field removed, renamed, or given a different meaning. A field added beside the existing ones
    /// does not move it: a reader that ignores what it does not recognise is unharmed, and bumping
    /// for an addition trains readers to treat the number as noise.
    /// </para>
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Gets which shape this document is. Always <see cref="CurrentVersion"/> as written here, and
    /// present on every notice so that a reader never has to infer it from which fields it found.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; init; } = CurrentVersion;

    /// <summary>
    /// Gets what happened, as one word a reader can branch on without comparing states.
    /// </summary>
    [JsonPropertyName("event")]
    public required NoticeEvent Event { get; init; }

    /// <summary>
    /// Gets which request this is about, so a reader that keeps its own rows can recognise a second
    /// notice about the same one instead of holding two.
    /// </summary>
    [JsonPropertyName("requestId")]
    public required Guid RequestId { get; init; }

    /// <summary>
    /// Gets when it happened, which is the moment the request moved rather than the moment this was
    /// sent. A sink that was unreachable for an hour delivers nothing late-looking when it comes
    /// back, because the document says when the thing happened.
    /// </summary>
    [JsonPropertyName("at")]
    public required DateTimeOffset At { get; init; }

    /// <summary>
    /// Gets the state the request is in now.
    /// <para>
    /// Written as a word, and the converter is on this property rather than on the model's own
    /// enumeration. Putting it there would change how the store writes every request on disk, which
    /// is a different document with a different reader and a migration behind it.
    /// </para>
    /// </summary>
    [JsonPropertyName("state")]
    [JsonConverter(typeof(JsonStringEnumConverter<RequestState>))]
    public required RequestState State { get; init; }

    /// <summary>
    /// Gets the identifier of the person who asked. It is the server's own user identifier, which is
    /// what this plugin holds; resolving it to a name is the reader's to do against the server it is
    /// already talking to, and doing it here would send a name this plugin never stored.
    /// </summary>
    [JsonPropertyName("requestedByUserId")]
    public required Guid RequestedByUserId { get; init; }

    /// <summary>
    /// Gets the identifier of whoever moved it, where a person did. It is absent where nothing did:
    /// a request that arrived is not one somebody moved, and fulfilment is decided by the library
    /// rather than by anybody.
    /// </summary>
    [JsonPropertyName("movedByUserId")]
    public Guid? MovedByUserId { get; init; }

    /// <summary>
    /// Gets whether a film or a series was asked for. A word here for the same reason, and by the
    /// same local converter, as the state above.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonConverter(typeof(JsonStringEnumConverter<RequestedItemKind>))]
    public required RequestedItemKind Kind { get; init; }

    /// <summary>
    /// Gets the title as it was asked for, which is what a person reading the message recognises.
    /// </summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>
    /// Gets the year, where one was known. Two films share a title often enough that the year is
    /// what makes a one-line message unambiguous.
    /// </summary>
    [JsonPropertyName("year")]
    public int? Year { get; init; }

    /// <summary>
    /// The notice for a request as it stands, with what happened to it.
    /// <para>
    /// Built from the record rather than from arguments a caller assembles, so every path that
    /// announces something sends the same fields and a new path cannot quietly send a different
    /// document. The moment comes off the request as well, which is why nothing here reads a clock.
    /// </para>
    /// </summary>
    /// <param name="request">The request the notice is about.</param>
    /// <param name="what">What happened to it.</param>
    /// <returns>The document to send.</returns>
    /// <exception cref="ArgumentNullException">Where there is no request to describe.</exception>
    public static OutboundNotice For(MediaRequest request, NoticeEvent what)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new OutboundNotice
        {
            Event = what,
            RequestId = request.Id,
            At = what == NoticeEvent.Asked ? request.RequestedAt : request.StateChangedAt,
            State = request.State,
            RequestedByUserId = request.RequestedByUserId,
            MovedByUserId = what == NoticeEvent.Asked ? null : request.StateChangedByUserId,
            Kind = request.Kind,
            Title = request.DisplayTitle,
            Year = request.DisplayYear
        };
    }

    /// <summary>
    /// The notice for a request that has just moved, or nothing where the state it moved into is not
    /// one this plugin announces.
    /// <para>
    /// The mapping from a state to an event lives here rather than at the two paths that move a
    /// request, because two copies of it are two answers the day a state is added, and the copy that
    /// is wrong is the one that quietly sends the wrong word to somebody else's machine.
    /// </para>
    /// <para>
    /// <b>A state with no arm is not announced, and that is the deliberate direction.</b>
    /// <see cref="RequestState.Open"/> is the state a request is in before anybody has done
    /// anything, and <see cref="RequestState.Failed"/> has no path that reaches it in this tree; a
    /// state added later arrives here with nobody having decided what word it goes out as, and
    /// withholding a message is recoverable where sending the wrong one is not.
    /// </para>
    /// </summary>
    /// <param name="request">The request as the move left it.</param>
    /// <returns>The document to send, or <see langword="null"/> where nothing is sent for it.</returns>
    /// <exception cref="ArgumentNullException">Where there is no request to describe.</exception>
    public static OutboundNotice? ForMove(MediaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.State switch
        {
            RequestState.Approved => For(request, NoticeEvent.Approved),
            RequestState.Declined => For(request, NoticeEvent.Declined),
            RequestState.Fulfilled => For(request, NoticeEvent.Fulfilled),
            _ => null
        };
    }
}
