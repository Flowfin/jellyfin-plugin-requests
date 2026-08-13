using System;
using System.Globalization;

namespace Jellyfin.Plugin.Requests.Model;

/// <summary>
/// Thrown when somebody asks for something and is already waiting for as many things as they are
/// allowed to.
/// <para>
/// A refusal rather than a queue that grows anyway, because the quota is the only thing between one
/// enthusiastic person and every open request on the server, and a limit that is applied later is a
/// limit applied against habits people already have.
/// </para>
/// <para>
/// The limit and the number held are carried as values so a surface can say them without reading
/// English out of a message. The sentence a person is shown is not this message: what a user reads
/// when the answer is "not yet" is #70's, written once and rendered by every surface, and this
/// message is what reaches a log.
/// </para>
/// </summary>
public sealed class RequestQuotaReachedException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequestQuotaReachedException"/> class for
    /// somebody at their limit.
    /// </summary>
    /// <param name="held">How many open or approved requests they are already waiting for.</param>
    /// <param name="limit">How many they are allowed to be waiting for.</param>
    public RequestQuotaReachedException(int held, int limit)
        : base(Describe(held, limit))
    {
        Held = held;
        Limit = limit;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestQuotaReachedException"/> class. Present
    /// because the analyzers ask every exception for the three ordinary constructors; the
    /// constructor above is the one this type exists for.
    /// </summary>
    public RequestQuotaReachedException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestQuotaReachedException"/> class with a
    /// message of the caller's own. See the note on the parameterless constructor.
    /// </summary>
    /// <param name="message">The message.</param>
    public RequestQuotaReachedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestQuotaReachedException"/> class with a
    /// message and an inner exception. See the note on the parameterless constructor.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public RequestQuotaReachedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets how many open or approved requests the person was already waiting for.
    /// </summary>
    public int Held { get; }

    /// <summary>
    /// Gets how many they are allowed to be waiting for.
    /// </summary>
    public int Limit { get; }

    private static string Describe(int held, int limit)
        => string.Format(
            CultureInfo.InvariantCulture,
            "This person is waiting for {0} open or approved requests and the limit is {1}. Something they asked for has to be answered before they can ask for another.",
            held,
            limit);
}
