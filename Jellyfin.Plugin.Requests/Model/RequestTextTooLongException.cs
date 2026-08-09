using System;
using System.Globalization;

namespace Jellyfin.Plugin.Requests.Model;

/// <summary>
/// Thrown when a piece of free text on a request is longer than the model allows.
/// <para>
/// The cap is refused rather than applied. Truncating is the tempting alternative and it is worse:
/// the person who wrote the text is not told, the sentence that mattered is usually the last one,
/// and what is stored is something nobody wrote. A refusal gives a surface something to say.
/// </para>
/// <para>
/// The field, the cap and the length that arrived are carried as values, so an API layer can turn
/// this into a message naming the field and the number without parsing English out of a string.
/// </para>
/// </summary>
public sealed class RequestTextTooLongException : ArgumentException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequestTextTooLongException"/> class for a named
    /// field.
    /// </summary>
    /// <param name="field">The property the text was being written to.</param>
    /// <param name="maximumLength">The longest the text may be.</param>
    /// <param name="actualLength">How long the text was.</param>
    public RequestTextTooLongException(string field, int maximumLength, int actualLength)
        : base(Describe(field, maximumLength, actualLength), field)
    {
        Field = field;
        MaximumLength = maximumLength;
        ActualLength = actualLength;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestTextTooLongException"/> class. Present
    /// because the analyzers ask every exception for the three ordinary constructors; the
    /// constructor above is the one this type exists for.
    /// </summary>
    public RequestTextTooLongException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestTextTooLongException"/> class with a
    /// message of the caller's own. See the note on the parameterless constructor.
    /// </summary>
    /// <param name="message">The message.</param>
    public RequestTextTooLongException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestTextTooLongException"/> class with a
    /// message and an inner exception. See the note on the parameterless constructor.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public RequestTextTooLongException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets the property the text was being written to, or <see langword="null"/> where this was
    /// built by one of the constructors that names no field.
    /// </summary>
    public string? Field { get; }

    /// <summary>
    /// Gets the longest the text may be.
    /// </summary>
    public int MaximumLength { get; }

    /// <summary>
    /// Gets how long the text that was refused was.
    /// </summary>
    public int ActualLength { get; }

    private static string Describe(string field, int maximumLength, int actualLength)
        => string.Format(
            CultureInfo.InvariantCulture,
            "{0} may be at most {1} characters and {2} were given. The text is refused rather than shortened, because a truncated note is something nobody wrote.",
            field,
            maximumLength,
            actualLength);
}
