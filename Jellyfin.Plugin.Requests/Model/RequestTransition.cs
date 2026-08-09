namespace Jellyfin.Plugin.Requests.Model;

/// <summary>
/// One cell of <see cref="RequestLifecycle.Table"/>: an ordered pair of states, whether moving
/// between them is allowed, and the sentence that says why.
/// <para>
/// A refused cell is a row here rather than an absence, because an absence cannot carry a reason.
/// The question a reader arrives with is almost never "is this allowed" but "why is this not
/// allowed", and a table of only the legal moves answers the first and leaves the second to
/// whoever remembers the argument.
/// </para>
/// </summary>
public sealed record RequestTransition
{
    /// <summary>
    /// Gets the state being moved out of.
    /// </summary>
    public required RequestState From { get; init; }

    /// <summary>
    /// Gets the state being moved into.
    /// </summary>
    public required RequestState To { get; init; }

    /// <summary>
    /// Gets a value indicating whether this move is allowed.
    /// </summary>
    public required bool IsLegal { get; init; }

    /// <summary>
    /// Gets the reason this cell reads the way it does, in one sentence. This is printed in
    /// <c>docs/lifecycle.md</c> and is the text a person reads when a move they expected to work
    /// was refused, so it says why rather than restating the verdict.
    /// </summary>
    public required string Why { get; init; }
}
