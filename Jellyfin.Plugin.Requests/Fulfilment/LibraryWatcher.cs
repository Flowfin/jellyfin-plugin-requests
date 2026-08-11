using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Requests.Fulfilment;

/// <summary>
/// The event half of #42: what the server tells this plugin the moment the library gains or loses
/// something, turned into a look at the requests that name it.
/// <para>
/// The event is handled by putting it in a queue and returning. The server raises it on the thread
/// that is scanning the library, and doing the work there would hold up that scan behind a store
/// read and a store write per item; a scan of a library somebody has just added is thousands of
/// items. The queue has no bound because dropping a library event silently is the failure this
/// class exists to prevent, and what fills it is a scan that ends.
/// </para>
/// <para>
/// Stopping drains rather than abandons. The server's shutdown token bounds it, so a drain that
/// cannot finish in the time the server allows is cut off and the scheduled run catches what was
/// left. That is also what makes this testable without waiting: a test starts it, raises what it
/// wants, stops it, and the stop is the point at which everything raised has been handled.
/// </para>
/// </summary>
public sealed class LibraryWatcher : IHostedService, IDisposable
{
    private readonly Channel<LibraryChangeEventArgs> _queue =
        Channel.CreateUnbounded<LibraryChangeEventArgs>(new UnboundedChannelOptions { SingleReader = true });

    private readonly ILibrary _library;
    private readonly FulfilmentSweep _sweep;
    private readonly ILogger _logger;

    private Task? _draining;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryWatcher"/> class.
    /// </summary>
    /// <param name="library">What raises the change and answers what is held.</param>
    /// <param name="sweep">The one thing that looks at the library, shared with the scheduled path.</param>
    /// <param name="logger">The server's log, where a failure to handle one change is reported.</param>
    /// <exception cref="ArgumentNullException">Where anything it needs is missing.</exception>
    public LibraryWatcher(ILibrary library, FulfilmentSweep sweep, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(sweep);
        ArgumentNullException.ThrowIfNull(logger);

        _library = library;
        _sweep = sweep;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _library.Changed += OnChanged;
        _draining = DrainAsync();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _library.Changed -= OnChanged;

        // Completing the writer is what ends the drain. Unsubscribing first means nothing can be
        // written after it, so the loop below finishes what is already queued and then returns.
        _queue.Writer.TryComplete();

        if (_draining is null)
        {
            return;
        }

        await _draining.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _library.Changed -= OnChanged;
        _queue.Writer.TryComplete();
    }

    private void OnChanged(object? sender, LibraryChangeEventArgs change)
    {
        // TryWrite on an unbounded channel fails only after the writer has been completed, which is
        // a change raised while the server is stopping. There is nothing left to do with it and the
        // scheduled run will see the library as it ends up.
        _queue.Writer.TryWrite(change);
    }

    private async Task DrainAsync()
    {
        await foreach (var change in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await _sweep.ItemChangedAsync(change, CancellationToken.None).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception reason)
#pragma warning restore CA1031
            {
                // One library item that could not be handled must not end the loop, because the loop
                // is the only thing handling every other item. What went wrong is written where an
                // operator reads it, and the scheduled run looks at the same request again.
                _logger.LogError(
                    reason,
                    "Looking at the library for a request that changed did not finish. The scheduled run will look again.");
            }
        }
    }
}
