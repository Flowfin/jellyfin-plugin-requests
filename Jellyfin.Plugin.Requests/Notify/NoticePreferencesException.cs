using System;

namespace Jellyfin.Plugin.Requests.Notify;

/// <summary>
/// What is kept about who wants to be told could not be read, or could not be written.
/// <para>
/// It is raised rather than answered as a default, for the reason the request store refuses to open
/// on a file it cannot parse: a read that quietly answers "yes, tell them" is a person who asked not
/// to be told being told anyway, and nothing afterwards can tell that from a person who never asked.
/// </para>
/// </summary>
public sealed class NoticePreferencesException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoticePreferencesException"/> class.
    /// </summary>
    public NoticePreferencesException()
        : base("What is kept about who wants to be told about their own requests could not be read.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NoticePreferencesException"/> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    public NoticePreferencesException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NoticePreferencesException"/> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">What the runtime raised underneath.</param>
    public NoticePreferencesException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
