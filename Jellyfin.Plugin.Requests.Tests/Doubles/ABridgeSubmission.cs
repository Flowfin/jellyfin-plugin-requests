using Jellyfin.Plugin.Requests.Bridge;
using Jellyfin.Plugin.Requests.Storage;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// The submission every test that is not about the bridge is given.
/// <para>
/// It is the real thing over the shipping bridge rather than a stand-in, because that is what every
/// install without an external service runs and a decision on such a server has to behave exactly as
/// it did before this existed. A double here would let the submission path drift while the tests of
/// the endpoints around it stayed green.
/// </para>
/// </summary>
internal static class ABridgeSubmission
{
    /// <summary>
    /// A submission with no service behind it, over the store the test is using.
    /// </summary>
    /// <param name="store">The store under test, which the submission writes a reference back into.</param>
    /// <returns>The submission.</returns>
    public static BridgeSubmission WithNothingBehindIt(IRequestStore store)
        => new BridgeSubmission(new NoRequestBackend(), store, new RecordingLogger());
}
