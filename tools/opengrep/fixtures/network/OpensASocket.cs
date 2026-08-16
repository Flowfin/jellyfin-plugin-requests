// Fixture for suite-opens-no-socket. This file is in no project and is never
// compiled; it exists so the rule can be watched refusing the mistake it names.
//
// The near-miss is the first test written against the notification sink by
// somebody who has not read docs/testing.md. The sink takes its handler as a
// constructor argument, so handing it the real one instead of the endpoint
// double is a one-word change and reads like the more honest test. What it
// costs is the property the suite is built on: the test then asserts something
// that is true only on a machine which can reach the address in it, and on the
// machine that cannot it fails for a reason that has nothing to do with the
// sink.

namespace Jellyfin.Plugin.Requests.Tests.Fixtures;

internal sealed class OpensASocket
{
    // Legal neighbour, left here on purpose: an endpoint double derives from
    // HttpMessageHandler and is handed to the type under test, which is the
    // shape this rule exists to keep, and the rule has to stay quiet on it.
    public static OutboundSink ThroughTheDouble(IInstallSettings settings, ILogger logger)
    {
        var endpoint = ASinkEndpoint.ThatAccepts();

        return new OutboundSink(settings, endpoint, logger, OutboundSink.DefaultAnswerWithin);
    }

    // The regression, in the spellings a test would actually reach for.
    public static async Task ReachOut(IInstallSettings settings, ILogger logger)
    {
        var sending = new SocketsHttpHandler();
        var sink = new OutboundSink(settings, sending, logger, OutboundSink.DefaultAnswerWithin);

        using var client = new HttpClient(new HttpClientHandler());
        _ = await client.GetStringAsync("https://requests.invalid/notices");

        var listener = new TcpListener(IPAddress.Loopback, 0);
        var connection = new TcpClient();
        var raw = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var serving = new HttpListener();
        var byName = Dns.GetHostEntry("requests.invalid");
        var addresses = await Dns.GetHostAddressesAsync("requests.invalid");

        _ = sink;
        _ = listener;
        _ = connection;
        _ = raw;
        _ = serving;
        _ = byName;
        _ = addresses;
    }
}
