using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using MediaBrowser.Controller.Events;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Requests.People;

/// <summary>
/// The one place this plugin is told that a Jellyfin account has been deleted.
/// <para>
/// <b>It is the server's own event rather than a poll.</b> A scheduled task comparing the store
/// against the user list would have to ask the server about every identifier it holds, on a schedule,
/// and would leave a deleted person's records standing until the next run. The server raises this the
/// moment the account goes, and there is nothing here that has to know how the server keeps users.
/// </para>
/// <para>
/// <b>Nothing at all leaves this call.</b> What is on the other end of it is the server deleting a
/// user, and a fault of this plugin's arriving there is an administrator's gesture failing for a
/// reason nobody on that side can act on. Every failure is written where an operator reads it, and
/// the sweep says on its own what it could not finish.
/// </para>
/// <para>
/// <b>This is a wire and holds no rule.</b> What happens to which record is
/// <see cref="AccountRemoval"/>, which is where the decision is argued and which is what the suite
/// drives; this exists so that the rule is reached by a real server and not only by a test.
/// </para>
/// </summary>
public sealed class RemovedAccounts : IEventConsumer<UserDeletedEventArgs>
{
    private readonly AccountRemoval _removal;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RemovedAccounts"/> class.
    /// </summary>
    /// <param name="removal">What to do with the records the account leaves.</param>
    /// <param name="logger">Where a failure is written.</param>
    /// <exception cref="ArgumentNullException">Where anything it needs is missing.</exception>
    public RemovedAccounts(AccountRemoval removal, ILogger<RemovedAccounts> logger)
    {
        ArgumentNullException.ThrowIfNull(removal);
        ArgumentNullException.ThrowIfNull(logger);

        _removal = removal;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task OnEvent(UserDeletedEventArgs eventArgs)
    {
        if (eventArgs?.Argument is null)
        {
            _logger.LogWarning(
                "The server said an account was deleted and named none, so no request could be looked at. Any records that account left are still held.");

            return;
        }

        try
        {
            await _removal.RemoveAsync(eventArgs.Argument.Id, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception reason)
#pragma warning restore CA1031
        {
            // The account is already gone by the time this runs, so there is nothing to undo and
            // nothing to hand back. What matters is that the failure is visible: nothing looks at
            // these records again on its own, because the account they name no longer exists to
            // start a search from.
            _logger.LogError(
                reason,
                "Taking a deleted account out of this plugin's requests did not finish. The records it names are still held and nothing looks at them again on its own.");
        }
    }
}
