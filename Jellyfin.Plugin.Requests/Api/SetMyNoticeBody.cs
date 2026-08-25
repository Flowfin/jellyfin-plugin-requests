namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// What a person sends to turn the message about their own requests on or off.
/// <para>
/// <b>There is no field naming whose setting this is, and that is the whole mechanism.</b> The
/// person is read off the call by <see cref="ICallerIdentity"/>, so there is nowhere for a caller to
/// put somebody else's identifier and no branch that would have to refuse one. An administrator
/// sending this changes their own setting, exactly as anybody else does.
/// </para>
/// </summary>
public sealed record SetMyNoticeBody
{
    /// <summary>
    /// Gets a value indicating whether this plugin is to push a message to the caller when one of
    /// their own requests moves.
    /// <para>
    /// It is nullable so that a body that left it out is a body that said nothing, rather than one
    /// that said off: a client sending an empty object would otherwise silence the person, which is
    /// the direction where a mistake is not noticed.
    /// </para>
    /// </summary>
    public bool? TellsMe { get; init; }
}
