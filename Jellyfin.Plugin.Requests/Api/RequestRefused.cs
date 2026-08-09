namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// Why a call was refused, naming the field that is wrong.
/// <para>
/// The field is here rather than only in the sentence because a client showing the message beside
/// the box the person typed in needs to know which box, and a client that has to read English to
/// find out is a client that stops working when the sentence is reworded.
/// </para>
/// <para>
/// This is the smallest shape that carries what the create endpoint has to say. What an error looks
/// like across the whole API, and which status code each failure gets, is #56, and this is expected
/// to be replaced by whatever that decides rather than to be the answer.
/// </para>
/// </summary>
public sealed record RequestRefused
{
    /// <summary>
    /// Gets the field that was wrong, named as the body names it.
    /// </summary>
    public required string Field { get; init; }

    /// <summary>
    /// Gets what is wrong with it, in a sentence a person can act on.
    /// </summary>
    public required string Reason { get; init; }
}
