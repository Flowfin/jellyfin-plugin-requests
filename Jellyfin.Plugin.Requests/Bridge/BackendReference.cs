using System;

namespace Jellyfin.Plugin.Requests.Bridge;

/// <summary>
/// What an external service called the thing this plugin handed it, and which service said so.
/// <para>
/// Both halves are kept because a reference without the service it belongs to cannot be used
/// against anything: an operator who changes which service is configured would otherwise have
/// requests carrying identifiers that mean something somewhere else.
/// </para>
/// <para>
/// The value is the service's own, unread. Nothing here parses it, compares it to a pattern or
/// assumes it is a number, because the shape of an identifier is that service's business and a
/// plugin that decided otherwise would break on the first service that used a word.
/// </para>
/// </summary>
public sealed record BackendReference
{
    private readonly string _service = string.Empty;
    private readonly string _id = string.Empty;

    /// <summary>
    /// Gets which external service issued this. Its own name for itself, as the adapter reports it.
    /// </summary>
    /// <exception cref="ArgumentException">Where there is nothing in it.</exception>
    public required string Service
    {
        get => _service;
        init => _service = Present(value, nameof(Service));
    }

    /// <summary>
    /// Gets what that service called the submitted request.
    /// </summary>
    /// <exception cref="ArgumentException">Where there is nothing in it.</exception>
    public required string Id
    {
        get => _id;
        init => _id = Present(value, nameof(Id));
    }

    /// <summary>
    /// Refuses an empty half. A reference is what makes the two systems reconcilable again, and one
    /// stored with an empty side is a row that looks like a handover happened and cannot be used to
    /// ask anybody about it. Refused where it is built rather than checked wherever it is read,
    /// because one of those readers will eventually not check.
    /// </summary>
    /// <param name="value">The text as it arrived.</param>
    /// <param name="field">The property being written, for the refusal to name.</param>
    /// <returns>The text.</returns>
    private static string Present(string value, string field)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                FormattableString.Invariant(
                    $"A backend reference needs {field}, and an empty one names nothing anybody can be asked about."),
                nameof(value))
            : value;
}
