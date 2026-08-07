using System;
using System.Globalization;

namespace Jellyfin.Plugin.Requests.Storage;

/// <summary>
/// An add was refused because the store already holds a request with that identifier.
/// <para>
/// Separate from <see cref="RequestConcurrencyException"/> because the two say different things to
/// a caller. A conflict means read again and decide again. This one means the identifier source
/// handed out a value twice, or the same add was replayed, and re-reading changes nothing.
/// </para>
/// <para>
/// This is not the answer to "somebody already asked for this title". Whether two requests are the
/// same request is a question about providers and titles rather than about identifiers, and it is
/// decided above the store.
/// </para>
/// </summary>
public class DuplicateRequestException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateRequestException"/> class.
    /// </summary>
    /// <param name="requestId">The identifier the store already holds.</param>
    public DuplicateRequestException(Guid requestId)
        : base(string.Format(
            CultureInfo.InvariantCulture,
            "The store already holds a request with the identifier {0}.",
            requestId))
    {
        RequestId = requestId;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateRequestException"/> class.
    /// </summary>
    public DuplicateRequestException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateRequestException"/> class.
    /// </summary>
    /// <param name="message">What happened.</param>
    public DuplicateRequestException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateRequestException"/> class.
    /// </summary>
    /// <param name="message">What happened.</param>
    /// <param name="innerException">What it happened because of.</param>
    public DuplicateRequestException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets the identifier the store already holds.
    /// </summary>
    public Guid RequestId { get; }
}
