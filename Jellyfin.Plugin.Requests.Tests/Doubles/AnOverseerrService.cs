using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// A service speaking the Overseerr form, in the same process, reached through the adapter's own
/// handler pipeline so that no socket is opened.
/// <para>
/// It stands where a real service would stand and answers what it is told to answer, per call,
/// from a function the test hands it. It records every call it receives - the method, the address,
/// the body, the value of the credential header and the media type - so a leg can assert what left
/// the adapter rather than only what came back. The cases a real service would produce and the
/// suite cannot reach are the ones <c>docs/testing.md</c> refuses: a refused connection, an answer
/// that never comes, a body that is not JSON and one of the wrong shape are all here as first-class
/// answers, which is the half of #35 an adapter that reads a response body makes reachable.
/// </para>
/// <para>
/// Nothing here was captured from a running service. The shapes it answers with are the ones
/// <c>docs/bridge.md</c> quotes out of the form's own description, and a service that has moved on
/// from that description is not something this double can know about.
/// </para>
/// </summary>
internal sealed class AnOverseerrService : HttpMessageHandler
{
    private readonly TaskCompletionSource _released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentQueue<Call> _calls = new ConcurrentQueue<Call>();
    private readonly Func<Call, HttpResponseMessage> _answer;
    private readonly bool _holds;

    private AnOverseerrService(Func<Call, HttpResponseMessage> answer, bool holds)
    {
        _answer = answer;
        _holds = holds;
    }

    /// <summary>
    /// Gets every call the adapter made, in the order they arrived.
    /// </summary>
    public IReadOnlyList<Call> Calls => [.. _calls];

    /// <summary>
    /// A service that answers every call with one status and one body.
    /// </summary>
    /// <param name="status">The status to answer with.</param>
    /// <param name="body">The JSON body to answer with.</param>
    /// <returns>The service.</returns>
    public static AnOverseerrService ThatAnswers(HttpStatusCode status, string body = "{}")
        => new AnOverseerrService(_ => Json(status, body), holds: false);

    /// <summary>
    /// A service that answers each call by whatever the given function decides for it.
    /// </summary>
    /// <param name="answer">What to answer, given the call as it arrived.</param>
    /// <returns>The service.</returns>
    public static AnOverseerrService ThatAnswersWith(Func<Call, HttpResponseMessage> answer)
        => new AnOverseerrService(answer, holds: false);

    /// <summary>
    /// A service with nothing listening at its address.
    /// </summary>
    /// <returns>The service.</returns>
    public static AnOverseerrService ThatRefusesTheConnection()
        => new AnOverseerrService(NothingListening, holds: false);

    /// <summary>
    /// A service that takes every call and never answers it, until <see cref="Release"/>.
    /// </summary>
    /// <returns>The service.</returns>
    public static AnOverseerrService ThatNeverAnswers()
        => new AnOverseerrService(_ => Json(HttpStatusCode.OK, "{}"), holds: true);

    /// <summary>
    /// An answer carrying a JSON body.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <param name="body">The body.</param>
    /// <returns>The answer.</returns>
    public static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>
    /// An answer carrying a body that is not JSON at all, which is what a reverse proxy in front of
    /// a service answers with when the service is not there.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <param name="body">The body.</param>
    /// <returns>The answer.</returns>
    public static HttpResponseMessage Html(HttpStatusCode status, string body)
        => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "text/html") };

    /// <summary>
    /// Lets every held call answer.
    /// </summary>
    public void Release() => _released.TrySetResult();

    /// <summary>
    /// What the platform raises when nothing is listening. The message names the host and the
    /// port, because that is what the platform puts into this exception when a connection is
    /// refused, measured on #100. A double whose refusal carried no address would let a leg
    /// asserting that the credential stays out of the log pass over code that logs the whole
    /// exception, which is the shape every caller of the adapter uses.
    /// </summary>
    /// <param name="call">The call that was refused.</param>
    /// <returns>Never; it throws.</returns>
    private static HttpResponseMessage NothingListening(Call call)
        => throw new HttpRequestException(
            FormattableString.Invariant(
                $"There is nothing listening at that address. ({call.Address?.Host}:{call.Address?.Port})"));

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var key = request.Headers.TryGetValues("X-Api-Key", out var values) ? values.FirstOrDefault() : null;

        var call = new Call(request.Method, request.RequestUri, body, key, request.Content?.Headers.ContentType?.MediaType);
        _calls.Enqueue(call);

        if (_holds)
        {
            await _released.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return _answer(call);
    }

    /// <summary>
    /// One call as it reached the service.
    /// </summary>
    /// <param name="Method">The method.</param>
    /// <param name="Address">The whole address, credential header excluded.</param>
    /// <param name="Body">The body, as text.</param>
    /// <param name="Key">The value of the credential header, or nothing where none was sent.</param>
    /// <param name="MediaType">The media type the body was declared as.</param>
    internal sealed record Call(HttpMethod Method, Uri? Address, string Body, string? Key, string? MediaType)
    {
        /// <summary>
        /// Gets the path the call was made to, which is what a leg compares against the form's own
        /// path items.
        /// </summary>
        public string Path => Address?.AbsolutePath ?? string.Empty;
    }
}
