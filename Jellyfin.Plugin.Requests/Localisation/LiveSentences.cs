namespace Jellyfin.Plugin.Requests.Localisation;

/// <summary>
/// The sentences pushed to a person's own client when the request they asked for moves, named by the
/// key each one is under in the catalogue.
/// <para>
/// <b>These are not the ones in <see cref="Sentences"/> and the difference is what each is for.</b>
/// Those three are drawn on a surface a person opened, so they can be long enough to explain and the
/// reader is already looking at their requests. These arrive unasked over the connection the server
/// already holds, land in a corner of whatever the person was doing, and are gone. So they say which
/// title and what happened to it and nothing else, and anything longer is on the page.
/// </para>
/// <para>
/// <b>They are declared here for the same reason the other three are.</b> Nothing draws them from an
/// asset, because no page is involved at all, so the only thing that can hold the catalogue against
/// its readers is a class naming the keys. <c>PageWordsTests</c> walks this one beside
/// <see cref="Sentences"/>, which is what makes a key added to <c>en.json</c> and named nowhere, or
/// named here and absent from the catalogue, a red suite rather than a blank line on somebody's
/// screen.
/// </para>
/// <para>
/// What is deliberately not here is a sentence per state. A state this plugin does not announce
/// sends no message at all, which <see cref="Notify.RequesterMessage"/> decides and says why, and a
/// key sitting here for a state nothing sends would be a string a translator is asked to translate
/// and nobody ever reads.
/// </para>
/// </summary>
public static class LiveSentences
{
    /// <summary>
    /// The line above the message. It says which plugin is talking, because the message lands among
    /// whatever else the person's client has to say and none of the rest of it is about a request.
    /// </summary>
    public const string Header = "live.header";

    /// <summary>
    /// An operator said yes. It carries the title and makes no promise about when the thing turns
    /// up, because approving a request and obtaining what was asked for are two different events and
    /// only the second one puts it in the library.
    /// </summary>
    public const string Approved = "live.approved";

    /// <summary>
    /// An operator said no. It carries the title and the reason, which is the one thing that stops
    /// the person asking for the same title again, and it carries neither note: a message that is
    /// gone in a few seconds is the wrong place for five hundred characters somebody typed.
    /// </summary>
    public const string Declined = "live.declined";

    /// <summary>
    /// The thing that was asked for is in the library. This is the one people are waiting for, and
    /// it is the only one of the three that says something the person can act on immediately.
    /// </summary>
    public const string Fulfilled = "live.fulfilled";
}
