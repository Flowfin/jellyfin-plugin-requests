using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Notify;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// An activity log that keeps what was written to it instead of writing it anywhere.
/// <para>
/// The server's own activity log is a database on a running server, which the headless rule in
/// <c>docs/testing.md</c> refuses. What a test here can assert is what this plugin asked to be
/// written, which is every rule #75 states: one entry per move, what it says, and what it never
/// carries.
/// </para>
/// </summary>
internal sealed class RecordingJournal : IActivityJournal
{
    private readonly List<ActivityNote> _written = [];

    /// <summary>
    /// Gets every entry written, in the order it was written.
    /// </summary>
    public IReadOnlyList<ActivityNote> Written => _written;

    /// <summary>
    /// Gets or sets what to raise instead of writing, where the test is about a host that refused.
    /// </summary>
    public Exception? Refuses { get; set; }

    /// <inheritdoc />
    public Task WriteAsync(ActivityNote note, CancellationToken cancellationToken)
    {
        if (Refuses is Exception refusal)
        {
            return Task.FromException(refusal);
        }

        _written.Add(note);

        return Task.CompletedTask;
    }
}
