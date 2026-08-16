using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Model.Activity;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Requests.Notify;

/// <summary>
/// The server's own activity log, as the one call this plugin makes to it.
/// <para>
/// This is the only place that names the host's activity manager, so the reach exists once and
/// everything above it takes <see cref="IActivityJournal"/> instead. The shape of the host call is
/// the same on both claimed lines: <c>CreateAsync</c> takes the entity below and the entity is
/// constructed from a name, a type and a user.
/// </para>
/// <para>
/// <b>The entity's <c>ItemId</c> is deliberately left unset.</b> It is a library item's identifier
/// on the server's side and the dashboard offers it as a link, so putting a request's identifier
/// there would give an operator a link to an item that does not exist. The identifier is in the
/// text instead, where it is something to search for rather than something to click.
/// </para>
/// </summary>
public sealed class ServerActivityJournal : IActivityJournal
{
    private readonly IActivityManager _activity;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerActivityJournal"/> class.
    /// </summary>
    /// <param name="activity">The server's activity log.</param>
    /// <param name="logger">Where a refused write is reported.</param>
    /// <exception cref="ArgumentNullException">Where there is nothing to write to or nothing to report on.</exception>
    public ServerActivityJournal(IActivityManager activity, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(logger);

        _activity = activity;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task WriteAsync(ActivityNote note, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(note);

        var entry = new ActivityLog(note.Name, note.Type, note.UserId)
        {
            ShortOverview = note.ShortOverview,
            LogSeverity = LogLevel.Information
        };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _activity.CreateAsync(entry).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception failed)
#pragma warning restore CA1031
        {
            // The decision this describes is already in the store. Raising here would answer the
            // operator that their approval failed when it did not, and the failure that actually
            // happened is one the operator can do nothing about from the queue page. So it lands in
            // the log they would go to next, with the move in it, and the call carries on.
            //
            // Every exception rather than a named set: the host writes to a database this plugin
            // does not own, on two server generations, and the set it can raise is not knowable
            // from here. A named set would turn the unknown member into the thing this comment says
            // must not happen.
            _logger.LogWarning(
                failed,
                "A request moved and the entry for it could not be written to the server's activity log. The move itself was written and stands; what is lost is the line an operator would have read in the dashboard. {Entry}",
                note.ShortOverview);
        }
    }
}
