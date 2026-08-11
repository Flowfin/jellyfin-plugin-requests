using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Bridge;

/// <summary>
/// One row of <see cref="BackendStates.Table"/>: a word the external service uses, which of its two
/// lists that word came from, what this side does about it, and why.
/// <para>
/// A row that changes nothing here is a row rather than an absence, for the same reason a refused
/// cell of the transition table is one. The question a reader arrives with is "the service says
/// COMPLETED, why is my request not fulfilled", and a table holding only the words that move
/// something answers a question nobody asked and leaves that one to whoever remembers the argument.
/// </para>
/// </summary>
public sealed record BackendStateMapping
{
    /// <summary>
    /// Gets which of the service's two lists this word belongs to.
    /// </summary>
    public required BackendVocabulary Vocabulary { get; init; }

    /// <summary>
    /// Gets the word the service uses, as the service spells it.
    /// </summary>
    public required string Reported { get; init; }

    /// <summary>
    /// Gets the state this plugin moves the request into on hearing that word, or
    /// <see langword="null"/> where it moves nothing.
    /// <para>
    /// Nothing to move is the ordinary answer and not a gap. The service is downstream of a decision
    /// an operator already made here and upstream of a library check that runs here, so most of what
    /// it can say is either something this side already knows or something only this server's own
    /// library may say.
    /// </para>
    /// </summary>
    public required RequestState? MoveTo { get; init; }

    /// <summary>
    /// Gets the reason this row reads the way it does, in one sentence. This is printed in
    /// <c>docs/bridge.md</c> and is what a person reads when the two systems disagree, so it says
    /// why rather than restating the row.
    /// </summary>
    public required string Why { get; init; }
}
