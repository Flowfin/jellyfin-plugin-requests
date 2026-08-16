using System;
using System.Threading.Tasks;
using Jellyfin.Data.Events;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// The server's activity log with the database behind it refusing every write.
/// <para>
/// It stands in for the case the plugin cannot do anything about: the entry describes a decision
/// that is already in the store, and what is being asserted is that the decision stands and the
/// failure is reported rather than raised at whoever clicked.
/// </para>
/// </summary>
internal sealed class AnActivityLogThatRefuses : IActivityManager
{
    /// <inheritdoc />
#pragma warning disable CS0067 // Nothing here raises it, which is what an activity log this plugin only writes to looks like.
    public event EventHandler<GenericEventArgs<ActivityLogEntry>>? EntryCreated;
#pragma warning restore CS0067

    /// <inheritdoc />
    public Task CreateAsync(ActivityLog entry)
        => Task.FromException(new InvalidOperationException("The activity log could not be written to."));

    /// <inheritdoc />
    public Task<QueryResult<ActivityLogEntry>> GetPagedResultAsync(Jellyfin.Data.Queries.ActivityLogQuery query)
        => throw new NotSupportedException("Nothing in this plugin reads the activity log.");

    /// <inheritdoc />
    public Task CleanAsync(DateTime startDate)
        => throw new NotSupportedException("Nothing in this plugin cleans the activity log.");
}
