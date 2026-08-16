using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Requests.Notify;

/// <summary>
/// What a notice is about, as the word that goes on the wire.
/// <para>
/// It is a vocabulary of its own rather than the request's state, because the two answer different
/// questions. A state says where a request is; an event says what just happened to it, and a reader
/// wanting to post "this was turned down" needs the second. The two agree today and would stop
/// agreeing the moment a state is reachable by two routes.
/// </para>
/// <para>
/// The names are serialised as written here, so this enumeration is part of the document and moving
/// a name is a change to <see cref="OutboundNotice.CurrentVersion"/> rather than a rename.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<NoticeEvent>))]
public enum NoticeEvent
{
    /// <summary>
    /// Somebody asked for something that was not already in the queue.
    /// </summary>
    Asked = 0,

    /// <summary>
    /// An operator said yes.
    /// </summary>
    Approved = 1,

    /// <summary>
    /// An operator said no.
    /// </summary>
    Declined = 2,

    /// <summary>
    /// The thing that was asked for turned up in the library. Nobody moved this one, which is why a
    /// notice carrying it names no mover.
    /// </summary>
    Fulfilled = 3
}
