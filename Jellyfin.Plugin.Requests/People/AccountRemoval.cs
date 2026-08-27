using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Notify;
using Jellyfin.Plugin.Requests.Storage;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Requests.People;

/// <summary>
/// What this plugin does with its own records when a Jellyfin account is deleted.
/// <para>
/// <b>Doing nothing is not one of the answers.</b> A request record says that a named person asked
/// for a named title on a date, which is more revealing than most of what a media server holds, and
/// it outlives the account it names unless something removes it. The retention sweep is about age
/// and reaches only finished requests; this is about a person and reaches every request they are on.
/// </para>
/// <para>
/// <b>The two rules, and they are not the same rule.</b> A request the deleted person asked for is
/// theirs and goes. A request somebody else asked for that they had joined is not theirs, so the
/// request stays and they come off its list of joiners. That split is the decision recorded on #49.
/// </para>
/// <para>
/// <b>What this deliberately does not touch.</b>
/// <see cref="Jellyfin.Plugin.Requests.Model.MediaRequest.StateChangedByUserId"/> on a request that
/// stays keeps whatever identifier it held, including a deleted administrator's. Clearing it would
/// say something false rather than nothing, because an empty value there means no person moved the
/// request, and a surface showing an identifier nothing resolves is where a deleted person is shown
/// as one. That is the answer taken on #49 on 27 August; the stored half is here and the rendering half
/// is #307. The history needs no rule at all any more: an entry says what its mover was and never
/// which person, which is the shape the store carries from version 2 onward.
/// </para>
/// <para>
/// <b>The queue is not the only file that names a person.</b> Somebody who turned their own notices
/// off is an identifier in the notices file, which is the fifth place a deleted account is named and
/// the least revealing of them: it says that somebody on this server once said no, and nothing more.
/// It goes with the rest. A switch belonging to an account that no longer exists is a preference
/// nobody can change and a record of a person who is gone, and leaving it because it is small is the
/// reasoning that leaves data lying about.
/// </para>
/// <para>
/// <b>The case nobody has decided, stated rather than hidden.</b> A request the deleted person asked
/// for and somebody else joined is removed, because it is theirs, and the joiner loses it. Whether it
/// should instead pass to the earliest joiner, who did ask for the same title, is a call no issue on
/// this board has taken. Removal is what the decision as written says, and passing it on would make
/// the record say somebody asked at a moment they did not.
/// </para>
/// </summary>
public sealed class AccountRemoval
{
    /// <summary>
    /// How many times one request is tried before it is left for the log.
    /// <para>
    /// A removal that loses a race is not like a retention sweep dropping one: the record it failed
    /// to reach is the record this exists to remove, and nothing comes back for it later. So it is
    /// retried, and the number is small because the contention it is against is somebody deciding on
    /// a queue while an account is being deleted, which resolves in one write.
    /// </para>
    /// </summary>
    public const int Attempts = 5;

    private readonly IRequestStore _store;
    private readonly INoticePreferences _notices;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AccountRemoval"/> class.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="notices">Where the switch a person set about being told is kept.</param>
    /// <param name="logger">Where what was done is written.</param>
    /// <exception cref="ArgumentNullException">Where anything it needs is missing.</exception>
    public AccountRemoval(IRequestStore store, INoticePreferences notices, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(notices);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _notices = notices;
        _logger = logger;
    }

    /// <summary>
    /// What happened to one request.
    /// </summary>
    private enum Outcome
    {
        /// <summary>
        /// It was theirs and it is gone.
        /// </summary>
        Removed,

        /// <summary>
        /// It was somebody else's and they are off it.
        /// </summary>
        Detached,

        /// <summary>
        /// It stopped naming them without this call doing anything, which is not a failure.
        /// </summary>
        Gone,

        /// <summary>
        /// It still names them, because it kept moving for as many attempts as this makes.
        /// </summary>
        Left
    }

    /// <summary>
    /// Takes one person out of every request this plugin holds.
    /// </summary>
    /// <param name="userId">The account the server has deleted.</param>
    /// <param name="cancellationToken">Cancels the sweep.</param>
    /// <returns>What it did, so a caller can report it and a test can assert it.</returns>
    /// <exception cref="ArgumentException">Where no account is named.</exception>
    /// <exception cref="RequestStoreLoadException">Where the store cannot be read at all.</exception>
    public async Task<AccountRemovalReport> RemoveAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "A removal has to name an account. An empty identifier names everybody and nobody.",
                nameof(userId));
        }

        var removed = 0;
        var detached = 0;
        var left = 0;

        // One read of what names them rather than a walk of the store, and it answers both lists in
        // one question, which is what the store contract promises of it.
        foreach (var stored in await _store.FindForUserAsync(userId, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outcome = await TakeThemOffAsync(stored, userId, cancellationToken).ConfigureAwait(false);

            switch (outcome)
            {
                case Outcome.Removed:
                    removed++;
                    break;

                case Outcome.Detached:
                    detached++;
                    break;

                default:
                    left++;
                    break;
            }
        }

        // The switch in the other file, taken last so a failure there cannot stop the queue being
        // swept. Setting it back to the shipping value is what takes the identifier out of the list,
        // because the list holds the people who said no and nobody else.
        await _notices.SetAsync(userId, tellsThem: true, cancellationToken).ConfigureAwait(false);

        var report = new AccountRemovalReport(removed, detached, left);

        // At information rather than debug where anything happened: this is the plugin deleting
        // somebody's records without anybody asking it to, and an operator answering for what is
        // held should find that it happened in the log they already read.
        if ((removed > 0 || detached > 0) && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "A deleted account left {Removed} request(s) of their own, which were removed, and {Detached} request(s) of other people's, which they were taken off.",
                removed,
                detached);
        }

        if (left > 0)
        {
            // The one outcome an operator has to act on, so it is not folded into the line above.
            // Nothing comes back for these on its own: no scheduled run looks for records of an
            // account that no longer exists, because the account is what such a run would have to
            // start from.
            _logger.LogWarning(
                "{Left} request(s) naming a deleted account were left as they were, because they kept moving while this ran. Nothing looks at them again on its own, so they stay until somebody acts on them.",
                left);
        }

        return report;
    }

    /// <summary>
    /// One request, tried until it is taken or until the attempts are spent.
    /// </summary>
    /// <param name="stored">The request as it was read.</param>
    /// <param name="userId">The account being removed.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What happened to it.</returns>
    private async Task<Outcome> TakeThemOffAsync(
        StoredRequest stored,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var current = stored;

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (current.Request.RequestedByUserId == userId)
                {
                    return await _store.RemoveAsync(current.Request.Id, current.Revision, cancellationToken).ConfigureAwait(false)
                        ? Outcome.Removed
                        : Outcome.Gone;
                }

                if (!current.Request.JoinedByUserIds.Contains(userId))
                {
                    // Somebody moved it out from under the read and they are no longer on it. There
                    // is nothing to do and nothing to report as failed.
                    return Outcome.Gone;
                }

                await _store.ReplaceAsync(
                    current.Request with
                    {
                        JoinedByUserIds = [.. current.Request.JoinedByUserIds.Where(joined => joined != userId)]
                    },
                    current.Revision,
                    cancellationToken).ConfigureAwait(false);

                return Outcome.Detached;
            }
            catch (RequestConcurrencyException)
            {
                // Somebody decided on this request between the read and the write. Re-read and decide
                // again against what the store now holds, which is what the contract asks a refused
                // writer to do.
                var again = await _store.GetAsync(current.Request.Id, cancellationToken).ConfigureAwait(false);

                if (again is not StoredRequest moved)
                {
                    // It was removed while this ran, which is the outcome this call wanted anyway.
                    return Outcome.Gone;
                }

                current = moved;
            }
        }

        return Outcome.Left;
    }
}
