using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using Jellyfin.Plugin.Requests.Configuration;
using Jellyfin.Plugin.Requests.Notify;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Notify;

/// <summary>
/// The near-miss <c>suite-opens-no-socket</c> exists to refuse, written the way somebody who has not
/// read <c>docs/testing.md</c> would write it. It compiles and it passes, so the only thing that
/// separates it from a legitimate test is the rule.
/// <para>
/// This file is never merged. It is here so the refusal can be watched happening on a head that
/// carries it, which is what the first done-condition of #115 asks for.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class ASinkAgainstARealHandlerTests
{
    /// <summary>
    /// An install where nobody typed an address sends nothing, asserted against the handler that
    /// would actually have sent it.
    /// </summary>
    [Fact]
    public void AnInstallWhereNobodyTypedAnAddressSendsNothing()
    {
        using var sending = new SocketsHttpHandler();
        using var sink = new OutboundSink(
            new FakeInstallSettings(new PluginConfiguration()),
            sending,
            new RecordingLogger(),
            OutboundSink.DefaultAnswerWithin);

        Assert.False(sink.IsConfigured);
    }
}
