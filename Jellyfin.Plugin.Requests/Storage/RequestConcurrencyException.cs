using System;
using System.Globalization;

namespace Jellyfin.Plugin.Requests.Storage;

/// <summary>
/// A write was refused because the caller's read is out of date. Somebody else moved the request
/// between the read and the write.
/// <para>
/// This is thrown rather than returned as a false, because the alternative every store reaches for
/// is to write anyway, and the two callers this plugin has are an operator declining and the
/// plugin itself marking something fulfilled. Losing either one silently is the failure the whole
/// contract in <see cref="IRequestStore"/> exists against.
/// </para>
/// <para>
/// It carries what the caller needs to do something other than give up: which request, what they
/// thought they had, and what the store holds now. The administrator surface shows the operator
/// the second of those rather than a generic failure, and a sweep running on its own re-reads and
/// retries.
/// </para>
/// </summary>
public class RequestConcurrencyException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequestConcurrencyException"/> class.
    /// </summary>
    /// <param name="requestId">The request the write was refused on.</param>
    /// <param name="expectedRevision">The revision the caller wrote against.</param>
    /// <param name="current">
    /// What the store holds now, or <see langword="null"/> where the request is no longer in the
    /// store at all.
    /// </param>
    public RequestConcurrencyException(Guid requestId, long expectedRevision, StoredRequest? current)
        : base(Describe(requestId, expectedRevision, current))
    {
        RequestId = requestId;
        ExpectedRevision = expectedRevision;
        Current = current;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestConcurrencyException"/> class.
    /// </summary>
    public RequestConcurrencyException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestConcurrencyException"/> class.
    /// </summary>
    /// <param name="message">What happened.</param>
    public RequestConcurrencyException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestConcurrencyException"/> class.
    /// </summary>
    /// <param name="message">What happened.</param>
    /// <param name="innerException">What it happened because of.</param>
    public RequestConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets the request the write was refused on.
    /// </summary>
    public Guid RequestId { get; }

    /// <summary>
    /// Gets the revision the caller wrote against, which is the one they read.
    /// </summary>
    public long ExpectedRevision { get; }

    /// <summary>
    /// Gets what the store holds now, or <see langword="null"/> where the request has been removed
    /// since the caller read it. A caller showing this to a person is showing them what actually
    /// happened while they were deciding.
    /// </summary>
    public StoredRequest? Current { get; }

    private static string Describe(Guid requestId, long expectedRevision, StoredRequest? current)
        => current is null
            ? string.Format(
                CultureInfo.InvariantCulture,
                "Request {0} was written against revision {1} and is no longer in the store.",
                requestId,
                expectedRevision)
            : string.Format(
                CultureInfo.InvariantCulture,
                "Request {0} was written against revision {1} and the store holds revision {2}.",
                requestId,
                expectedRevision,
                current.Value.Revision);
}
