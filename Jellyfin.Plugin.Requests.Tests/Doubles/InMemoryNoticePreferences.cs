using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Notify;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// Who wants to be told, kept in memory instead of in a file.
/// <para>
/// It is the double for every test whose subject is a path rather than the keeping: what a test of
/// the endpoint or of the switch in front of the notice can assert is which person was asked about
/// and what happened next. Whether a setting survives a restart is
/// <c>FileNoticePreferences</c>'s own, and is tested against a directory of its own.
/// </para>
/// <para>
/// It keeps the refusals rather than a row per person, exactly as the file does, so a test that
/// never touches this gets the shipped default without saying so.
/// </para>
/// </summary>
internal sealed class InMemoryNoticePreferences : INoticePreferences
{
    private readonly HashSet<Guid> _quiet = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryNoticePreferences"/> class.
    /// </summary>
    /// <param name="quiet">Who has already turned it off.</param>
    public InMemoryNoticePreferences(params Guid[] quiet) => _quiet = [.. quiet];

    /// <summary>
    /// Gets how many times a setting has been written, so a test can assert that a call which
    /// changes nothing writes nothing.
    /// </summary>
    public int Writes { get; private set; }

    /// <inheritdoc />
    public Task<bool> TellsThemAsync(Guid userId, CancellationToken cancellationToken)
        => Task.FromResult(!_quiet.Contains(userId));

    /// <inheritdoc />
    public Task<bool> SetAsync(Guid userId, bool tellsThem, CancellationToken cancellationToken)
    {
        if (tellsThem ? _quiet.Remove(userId) : _quiet.Add(userId))
        {
            Writes++;
        }

        return Task.FromResult(tellsThem);
    }
}
