using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// Whatever an operator pointed the notification sink at, inside this process.
/// <para>
/// It is a message handler rather than a server on a port, so the sink's own client, its
/// serialisation, its timeout and its error handling are all exercised and no socket is opened. That
/// is the replacement <c>docs/testing.md</c> names for a real outbound call.
/// </para>
/// <para>
/// The unhappy endpoints are the point of it. An endpoint that refuses the connection, one that
/// answers with a failure and one that accepts the connection and then says nothing are the three
/// shapes a notification sink meets in the field, and the last is the one that needs no clock here:
/// it holds the send until the test lets go, so a suite can assert what happened while it is still
/// holding.
/// </para>
/// </summary>
internal sealed class ASinkEndpoint : HttpMessageHandler
{
    private readonly TaskCompletionSource _entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentQueue<SentNotice> _received = new ConcurrentQueue<SentNotice>();
    private readonly HttpStatusCode? _answers;
    private readonly bool _holds;

    private ASinkEndpoint(HttpStatusCode? answers, bool holds)
    {
        _answers = answers;
        _holds = holds;
    }

    /// <summary>
    /// Gets everything that was posted to it, oldest first.
    /// </summary>
    public IReadOnlyList<SentNotice> Received => [.. _received];

    /// <summary>
    /// Gets a task that completes the moment a send reaches this endpoint, so a test can assert
    /// against a delivery that is under way without waiting on a clock for it.
    /// </summary>
    public Task Entered => _entered.Task;

    /// <summary>
    /// An endpoint that takes what it is given and says so.
    /// </summary>
    /// <returns>An endpoint that answers 202.</returns>
    public static ASinkEndpoint ThatAccepts() => new ASinkEndpoint(HttpStatusCode.Accepted, holds: false);

    /// <summary>
    /// An endpoint that answers, with something other than success.
    /// </summary>
    /// <param name="status">What it answers with.</param>
    /// <returns>An endpoint that answers that.</returns>
    public static ASinkEndpoint ThatAnswers(HttpStatusCode status) => new ASinkEndpoint(status, holds: false);

    /// <summary>
    /// An endpoint that is not there. The connection is refused the way the runtime refuses one,
    /// rather than with an exception invented for the test.
    /// </summary>
    /// <returns>An endpoint nothing reaches.</returns>
    public static ASinkEndpoint ThatRefusesTheConnection() => new ASinkEndpoint(answers: null, holds: false);

    /// <summary>
    /// An endpoint that accepts the connection and then says nothing until it is let go.
    /// </summary>
    /// <returns>An endpoint that holds every send.</returns>
    public static ASinkEndpoint ThatNeverAnswers() => new ASinkEndpoint(HttpStatusCode.Accepted, holds: true);

    /// <summary>
    /// Lets go of every send this endpoint is holding.
    /// </summary>
    public void Release() => _released.TrySetResult();

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        _received.Enqueue(new SentNotice(request.Method, request.RequestUri, body, request.Content?.Headers.ContentType?.MediaType));
        _entered.TrySetResult();

        if (_holds)
        {
            await _released.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_answers is not HttpStatusCode answer)
        {
            throw new HttpRequestException("There is nothing listening at that address.");
        }

        return new HttpResponseMessage(answer);
    }

    /// <summary>
    /// One thing that arrived at the endpoint.
    /// </summary>
    /// <param name="Method">How it was sent.</param>
    /// <param name="Address">Where it was sent.</param>
    /// <param name="Body">What it carried.</param>
    /// <param name="MediaType">What it said it carried.</param>
    internal sealed record SentNotice(HttpMethod Method, Uri? Address, string Body, string? MediaType);
}
