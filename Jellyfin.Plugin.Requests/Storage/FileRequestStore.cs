using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Time;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Requests.Storage;

/// <summary>
/// The store this plugin ships: every request in one file in the plugin's own data directory,
/// serialised with the JSON support the runtime already provides. The medium and the two that were
/// rejected are in <c>docs/storage.md</c>.
/// <para>
/// <b>What an interruption may cost.</b> A server is stopped, a container is killed, a disk fills.
/// After any of those the store loads, and at most the one record being written is lost. That is a
/// property of how a write is made rather than of how carefully the caller shuts down: nothing is
/// ever written into the file the loader reads. A write serialises the whole set into
/// <see cref="PendingFileName"/>, flushes it to the disk, and only then replaces
/// <see cref="FileName"/> with it in one step. So the file the loader reads is a file some write
/// finished, and a process that dies at any moment leaves either the set before the write or the
/// set after it.
/// </para>
/// <para>
/// Two things that step rests on are the platform's and not this file's, and neither is measured
/// here. The replace is atomic because the runtime maps it onto the operating system's own rename,
/// and a rename is not durable until the directory entry reaches the disk, for which the runtime
/// offers no call. What that leaves is a window in which a completed write can be lost by a power
/// cut, which is the ordinary bound for this medium. It does not widen what an interruption can
/// cost: whichever of the two sets survives, it is a whole one.
/// </para>
/// <para>
/// <b>A leftover pending file is ignored, never read.</b> It is the wreckage of an interrupted
/// write, it is the one file that can be half a document, and the loader does not look at it. It is
/// not deleted on load either, because loading is a read: the next write truncates it.
/// </para>
/// <para>
/// <b>What is not survivable.</b> If the file the loader reads cannot be parsed as a set of
/// requests, the store refuses to open with <see cref="RequestStoreLoadException"/> rather than
/// returning the part of it that did parse. A short read that looks like a successful one is how a
/// queue silently loses records and then has the loss written back over the file that still held
/// them. Every such refusal is written to the log before it is thrown, because the caller that sees
/// the exception is a request somebody made and the operator who can act on it is reading the
/// server's log.
/// </para>
/// <para>
/// <b>The file says which shape it is in.</b> Every document carries <see cref="OnDiskVersion"/>.
/// A version this plugin does not know is refused and nothing is written, so a server downgraded to
/// an older plugin leaves the newer file alone rather than reading it as if the fields it has never
/// heard of were absent. An older version is migrated forward as it is read, and the file itself is
/// left as it was until some later write replaces it whole. What may change inside a version and
/// what needs a new one is in <c>docs/storage.md</c>.
/// </para>
/// <para>
/// <b>Reads do not wait for writes.</b> The set is held in memory as one value that is replaced
/// whole, so a reader takes the current one and never blocks and never sees half of a change. It is
/// replaced only after the disk has taken the same set, so what the plugin reports and what survives
/// a restart cannot disagree.
/// </para>
/// <para>
/// <b>The three reads the surfaces make are not walks of the set.</b> One person's requests and one
/// external identifier are answered out of two lookups built beside the set each time it is
/// replaced, so neither grows with how much the store holds. The queue page is a walk, because a
/// filter and an order chosen at the call cannot be served by a lookup built before it. What each
/// costs, at what size, and the run behind those numbers are in <c>docs/storage.md</c>.
/// </para>
/// </summary>
public sealed class FileRequestStore : IRequestStore, IDisposable
{
    /// <summary>
    /// The file the loader reads. It is only ever created by replacing it whole.
    /// </summary>
    public const string FileName = "requests.json";

    /// <summary>
    /// The file a write is built in before it replaces <see cref="FileName"/>. This is the only file
    /// that can be found half written, and nothing ever reads it.
    /// </summary>
    public const string PendingFileName = "requests.json.writing";

    /// <summary>
    /// The shape this plugin writes, and the highest it can read. It goes up when a change to the
    /// bytes would be read wrongly by the version before it, which is the rule stated in
    /// <c>docs/storage.md</c> rather than restated here.
    /// </summary>
    public const int OnDiskVersion = 2;

    /// <summary>
    /// The shape written before there was a version field: a bare array of entries, with no
    /// document around it. It is not a number any file carries; it is the number this store gives
    /// that shape so the migration has something to migrate from.
    /// </summary>
    private const int UnversionedShape = 0;

    private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
    {
        WriteIndented = false
    };

    private readonly string _directoryPath;
    private readonly string _filePath;
    private readonly string _pendingFilePath;
    private readonly ILogger _logger;
    private readonly IClock _clock;

    /// <summary>
    /// Held for the whole of a write, so no two writes are building the pending file at once and no
    /// write decides against a set another write has already replaced.
    /// </summary>
    private readonly SemaphoreSlim _writes = new SemaphoreSlim(1, 1);

    /// <summary>
    /// What the store holds and the two lookups over it, or null before the first call has read the
    /// file. Replaced whole and never edited, which is what lets a reader take it without waiting
    /// for anything.
    /// </summary>
    private Snapshot? _held;

    /// <summary>
    /// When the file this store keeps was last replaced. Written under the write lock and read
    /// without one, which is why it is a field rather than something derived: a reader that caught
    /// it between two writes reads one of the two moments and never a half of either.
    /// </summary>
    private DateTimeOffset? _lastWrittenAt;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileRequestStore"/> class.
    /// </summary>
    /// <param name="directoryPath">
    /// The directory the file lives in. Nothing is created until the first write, so constructing a
    /// store against a directory that does not exist yet is not an error.
    /// </param>
    /// <param name="logger">
    /// Where a refusal to open is written. It is required rather than optional, because a logger
    /// that may be absent is one that is absent on the machine where the refusal happened.
    /// </param>
    /// <param name="clock">
    /// What the moment of a write is read from. It is injected rather than read off the machine for
    /// the same reason every other moment in this tree is: a store that read the wall clock would be
    /// a store whose behaviour cannot be put under a test that decides what time it is.
    /// </param>
    public FileRequestStore(string directoryPath, ILogger logger, IClock clock)
    {
        ArgumentException.ThrowIfNullOrEmpty(directoryPath);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(clock);

        _directoryPath = directoryPath;
        _filePath = Path.Combine(directoryPath, FileName);
        _pendingFilePath = Path.Combine(directoryPath, PendingFileName);
        _logger = logger;
        _clock = clock;
    }

    /// <inheritdoc />
    public DateTimeOffset? LastWrittenAt => _lastWrittenAt;

    /// <summary>
    /// Gets the file the requests are kept in.
    /// </summary>
    public string FilePath => _filePath;

    /// <summary>
    /// Gets the file a write is built in before it replaces <see cref="FilePath"/>.
    /// </summary>
    public string PendingFilePath => _pendingFilePath;

    /// <inheritdoc />
    public async Task<StoredRequest?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var held = await HeldAsync(cancellationToken).ConfigureAwait(false);
        return held.Held.TryGetValue(id, out var stored) ? stored : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredRequest>> GetAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var held = await HeldAsync(cancellationToken).ConfigureAwait(false);
        return held.Held.Values.ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredRequest>> FindForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var held = await HeldAsync(cancellationToken).ConfigureAwait(false);

        // The stored array is handed back rather than copied. It was built when the snapshot was
        // built and nothing ever writes into it, so a caller holding it holds a value the same way
        // a caller holding a request does.
        return held.ByUser.TryGetValue(userId, out var theirs) ? theirs : [];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredRequest>> FindByProviderIdentifierAsync(
        RequestedItemKind kind,
        string provider,
        string value,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        cancellationToken.ThrowIfCancellationRequested();

        var held = await HeldAsync(cancellationToken).ConfigureAwait(false);
        var key = new ProviderKey(kind, provider, value);

        return held.ByProviderIdentifier.TryGetValue(key, out var carrying) ? carrying : [];
    }

    /// <inheritdoc />
    public async Task<StoredRequest?> FindByWantAsync(Guid wantId, CancellationToken cancellationToken)
    {
        if (wantId == Guid.Empty)
        {
            throw new ArgumentException(
                "A want identifier that names nothing cannot be looked up, because it is not one any request can have absorbed.",
                nameof(wantId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var held = await HeldAsync(cancellationToken).ConfigureAwait(false);

        return held.ByWant.TryGetValue(wantId, out var carrying) ? carrying : null;
    }

    /// <inheritdoc />
    public async Task<RequestPage> PageAsync(RequestQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var held = await HeldAsync(cancellationToken).ConfigureAwait(false);

        // One walk of the set, and the count and the page come out of it together. The filter and
        // the order are the query's own, so this store and the surface that pages one person's own
        // requests answer under one rule rather than under two that agree today.
        return query.PageOf(held.Held.Values);
    }

    /// <inheritdoc />
    public async Task<StoredRequest> AddAsync(MediaRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        await _writes.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var held = LoadedWhileHoldingTheWriteLock();

            if (held.Held.ContainsKey(request.Id))
            {
                throw new DuplicateRequestException(request.Id);
            }

            var stored = new StoredRequest(request, 1);
            var next = new Dictionary<Guid, StoredRequest>(held.Held) { [request.Id] = stored };

            await PersistAsync(next, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _held, new Snapshot(next));
            return stored;
        }
        finally
        {
            _writes.Release();
        }
    }

    /// <inheritdoc />
    public async Task<StoredRequest> ReplaceAsync(MediaRequest request, long expectedRevision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        await _writes.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var held = LoadedWhileHoldingTheWriteLock();
            var current = held.Held.TryGetValue(request.Id, out var stored) ? stored : (StoredRequest?)null;

            if (current is null || current.Value.Revision != expectedRevision)
            {
                throw new RequestConcurrencyException(request.Id, expectedRevision, current);
            }

            var written = new StoredRequest(request, expectedRevision + 1);
            var next = new Dictionary<Guid, StoredRequest>(held.Held) { [request.Id] = written };

            await PersistAsync(next, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _held, new Snapshot(next));
            return written;
        }
        finally
        {
            _writes.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(Guid id, long expectedRevision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _writes.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var held = LoadedWhileHoldingTheWriteLock();

            if (!held.Held.TryGetValue(id, out var stored))
            {
                return false;
            }

            if (stored.Revision != expectedRevision)
            {
                throw new RequestConcurrencyException(id, expectedRevision, stored);
            }

            var next = new Dictionary<Guid, StoredRequest>(held.Held);
            next.Remove(id);

            await PersistAsync(next, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _held, new Snapshot(next));
            return true;
        }
        finally
        {
            _writes.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _writes.Dispose();
    }

    /// <summary>
    /// Reads the file into a set, refusing anything it cannot read whole.
    /// </summary>
    /// <param name="filePath">The file to read.</param>
    /// <returns>What the file holds, and an empty set where there is no file yet.</returns>
    private Dictionary<Guid, StoredRequest> Read(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        JsonElement root;

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var document = JsonDocument.Parse(stream);
            root = document.RootElement.Clone();
        }
        catch (JsonException reason)
        {
            throw Refusing(
                filePath,
                "it is not the document this store writes, which is what an interruption in the middle of the file would leave if a write were made in place.",
                reason);
        }

        var persisted = EntriesOf(filePath, root);
        var held = new Dictionary<Guid, StoredRequest>();

        foreach (var entry in persisted)
        {
            if (entry?.Request is null)
            {
                throw Refusing(filePath, "one of the entries carries no request.");
            }

            if (entry.Revision < 1)
            {
                throw Refusing(filePath, "one of the entries carries a revision below the one an added request starts at.");
            }

            if (!held.TryAdd(entry.Request.Id, new StoredRequest(entry.Request, entry.Revision)))
            {
                throw Refusing(filePath, "two entries carry one identifier, so which of them the store holds is not decidable.");
            }
        }

        return held;
    }

    /// <summary>
    /// The entries a parsed file holds, having decided which shape it is in.
    /// <para>
    /// The shape is read off the root rather than off a field, because the version field is exactly
    /// what an older document does not have. An array is the shape written before the version
    /// existed and is migrated forward; an object is a versioned document and its number decides
    /// whether this plugin may read it.
    /// </para>
    /// <para>
    /// Migrating forward is a read and not a write. The file is left exactly as it was, and the
    /// document this plugin writes reaches the disk when some later write replaces the file whole,
    /// which is the one step this store ever makes to it. So a plugin that reads an older file and
    /// is then replaced by the older plugin again finds the file it left.
    /// </para>
    /// </summary>
    /// <param name="filePath">The file, for the refusal that names it.</param>
    /// <param name="root">What the file parsed to.</param>
    /// <returns>The entries, in the order the file holds them.</returns>
    private PersistedRequest?[] EntriesOf(string filePath, JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            // Guarded, because the two version numbers are boxed on the way into the call and this
            // is the one line here that a server may have switched off.
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "The requests in {FilePath} are in the shape written before the store carried a version. They are read as version {From} and migrated to version {To}. The file is not changed until the next write.",
                    filePath,
                    UnversionedShape,
                    OnDiskVersion);
            }

            using var older = MigrateForward(filePath, root);

            return Entries(filePath, older.RootElement);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Refusing(filePath, "it holds a JSON null where the list of requests should be.");
        }

        // The version is read off the bytes before anything else is, because what the rest of the
        // document means depends on it. Deserialising first would refuse an older file for missing a
        // field the version says it never carried, which reads to an operator as a damaged queue
        // rather than as an upgrade.
        var shape = ShapeOf(filePath, root);

        if (shape > OnDiskVersion)
        {
            // The one refusal that is not damage. The file is whole and was written by a later
            // version of this plugin, which is what an operator sees after a downgrade. Reading it
            // would mean guessing what a field this version has never heard of means, and a guess
            // that is wrong is written back over the file on the first write.
            _logger.LogError(
                "The requests in {FilePath} are version {Found} and this plugin reads at most version {Known}. They were written by a later version of this plugin, so nothing is read and nothing is written. Install the newer version again, or move the file aside to start an empty queue.",
                filePath,
                shape,
                OnDiskVersion);

            throw new RequestStoreLoadException(
                filePath,
                FormattableString.Invariant(
                    $"it is version {shape} and this plugin reads at most version {OnDiskVersion}, so it was written by a later version of this plugin."));
        }

        if (shape < OnDiskVersion)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "The requests in {FilePath} are version {From}. They are read as version {To} and migrated to it. The file is not changed until the next write.",
                    filePath,
                    shape,
                    OnDiskVersion);
            }

            using var older = MigrateForward(filePath, root);

            return RequestsOf(filePath, older.RootElement);
        }

        return RequestsOf(filePath, root);
    }

    /// <summary>
    /// The shape number a versioned document declares.
    /// </summary>
    /// <param name="filePath">The file, for the refusal that names it.</param>
    /// <param name="root">The document.</param>
    /// <returns>The number on it.</returns>
    /// <exception cref="RequestStoreLoadException">Where it carries none this store recognises.</exception>
    private int ShapeOf(string filePath, JsonElement root)
    {
        if (!root.TryGetProperty(nameof(PersistedDocument.Version), out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var shape)
            || shape < 1)
        {
            throw Refusing(
                filePath,
                "it carries no version this store recognises, so what its fields mean is not decidable.");
        }

        return shape;
    }

    /// <summary>
    /// The stored requests out of a document already known to be this store's shape.
    /// </summary>
    /// <param name="filePath">The file, for the refusal that names it.</param>
    /// <param name="root">The document.</param>
    /// <returns>The entries, in the order the document holds them.</returns>
    /// <exception cref="RequestStoreLoadException">Where it is not the document this store writes.</exception>
    private PersistedRequest?[] RequestsOf(string filePath, JsonElement root)
    {
        PersistedDocument? document;

        try
        {
            document = root.Deserialize<PersistedDocument>(SerializerOptions);
        }
        catch (JsonException reason)
        {
            throw Refusing(filePath, "it is not the document this store writes.", reason);
        }

        if (document?.Requests is null)
        {
            throw Refusing(filePath, "it holds a JSON null where the list of requests should be.");
        }

        return document.Requests;
    }

    /// <summary>
    /// Brings an older shape up to <see cref="OnDiskVersion"/> in memory.
    /// <para>
    /// It is a read: nothing here touches the file, and what comes back is a second document the
    /// caller disposes. What the step does and why the number had to go up is in
    /// <see cref="HistoryWithoutPeople"/> and in <c>docs/storage.md</c>.
    /// </para>
    /// </summary>
    /// <param name="filePath">The file, for the refusal that names it.</param>
    /// <param name="root">The document as it was read.</param>
    /// <returns>The migrated document, which the caller disposes.</returns>
    /// <exception cref="RequestStoreLoadException">Where the older bytes cannot be walked at all.</exception>
    private JsonDocument MigrateForward(string filePath, JsonElement root)
    {
        try
        {
            return HistoryWithoutPeople.Migrated(root);
        }
        catch (JsonException reason)
        {
            throw Refusing(filePath, "it is not the document this store writes.", reason);
        }
    }

    /// <summary>
    /// The entries of a bare array, which is the shape written before the version existed.
    /// </summary>
    /// <param name="filePath">The file, for the refusal that names it.</param>
    /// <param name="root">The array.</param>
    /// <returns>The entries.</returns>
    private PersistedRequest?[] Entries(string filePath, JsonElement root)
    {
        try
        {
            return root.Deserialize<PersistedRequest?[]>(SerializerOptions)
                ?? throw Refusing(filePath, "it holds a JSON null where the list of requests should be.");
        }
        catch (JsonException reason)
        {
            throw Refusing(filePath, "it is not the document this store writes.", reason);
        }
    }

    /// <summary>
    /// The refusal to open, written to the log on its way to the caller. Every refusal goes through
    /// here, so a file the store would not read leaves a line an operator can act on rather than
    /// only an exception in whatever call happened to be the first one.
    /// </summary>
    /// <param name="filePath">The file that was refused.</param>
    /// <param name="detail">What is wrong with it.</param>
    /// <param name="reason">What the reader threw, where there was one.</param>
    /// <returns>The exception for the caller to throw.</returns>
    private RequestStoreLoadException Refusing(string filePath, string detail, Exception? reason = null)
    {
        _logger.LogError(
            reason,
            "The stored requests in {FilePath} could not be read: {Detail} Nothing has been changed on disk.",
            filePath,
            detail);

        return reason is null
            ? new RequestStoreLoadException(filePath, detail)
            : new RequestStoreLoadException(filePath, detail, reason);
    }

    /// <summary>
    /// The set, reading the file on the first call that needs it. A caller that finds it already
    /// read takes it without waiting for anything, which is what
    /// <see cref="IRequestStore.GetAsync"/> promises.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait for the first read.</param>
    /// <returns>What the store holds.</returns>
    private async Task<Snapshot> HeldAsync(CancellationToken cancellationToken)
    {
        var held = Volatile.Read(ref _held);

        if (held is not null)
        {
            return held;
        }

        await _writes.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return LoadedWhileHoldingTheWriteLock();
        }
        finally
        {
            _writes.Release();
        }
    }

    /// <summary>
    /// The set, read from the file if this is the first call. The caller has to be holding
    /// <see cref="_writes"/>, which is what stops two of them reading the file at once and what
    /// stops a write deciding against a set that was read before another write replaced it.
    /// </summary>
    /// <returns>What the store holds.</returns>
    private Snapshot LoadedWhileHoldingTheWriteLock()
    {
        var held = _held;

        if (held is not null)
        {
            return held;
        }

        held = new Snapshot(Read(_filePath));
        Volatile.Write(ref _held, held);
        return held;
    }

    /// <summary>
    /// Puts a set on the disk, whole or not at all.
    /// <para>
    /// Every failure here reaches the caller. There is no catch in this method and none around its
    /// call sites, so a disk that is full, a directory that cannot be written to and a file the
    /// platform refuses to replace are all reported rather than absorbed, and the in-memory set is
    /// left as it was because it is only replaced after this returns.
    /// </para>
    /// </summary>
    /// <param name="held">The set to write.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the set is on the disk.</returns>
    private async Task PersistAsync(Dictionary<Guid, StoredRequest> held, CancellationToken cancellationToken)
    {
        var persisted = new PersistedDocument
        {
            Version = OnDiskVersion,
            Requests = [.. held.Values.Select(stored => new PersistedRequest { Revision = stored.Revision, Request = stored.Request })]
        };

        Directory.CreateDirectory(_directoryPath);

        // Write-through, so the bytes are asked to reach the device rather than a cache the replace
        // below would then overtake. What that is worth against a power cut is the platform's
        // answer and is not measured by this repository's suite; what the suite does measure is the
        // property that does not depend on it, which is that the file the loader reads is never a
        // partial one.
        var pending = new FileStream(
            _pendingFilePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough);

        await using (pending.ConfigureAwait(false))
        {
            await JsonSerializer.SerializeAsync(pending, persisted, SerializerOptions, cancellationToken).ConfigureAwait(false);
            await pending.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(_pendingFilePath, _filePath, overwrite: true);

        // After the move rather than before it. What this answers is when the file a later reader
        // would open last changed, so a write that serialised and then failed to land must not
        // advance it: an operator reading a fresh timestamp beside a store that never took the
        // write is being told the opposite of what happened.
        _lastWrittenAt = _clock.UtcNow;
    }

    /// <summary>
    /// One external identifier under one kind, as the identifier lookup is keyed by it. The kind is
    /// part of the key because a film and a series can carry the same number under the same
    /// provider and be two different works, which is the rule
    /// <see cref="RequestIdentity"/> is written to.
    /// </summary>
    /// <param name="Kind">What sort of thing the identifier names.</param>
    /// <param name="Provider">The provider's name.</param>
    /// <param name="Value">The identifier under that provider.</param>
    private readonly record struct ProviderKey(RequestedItemKind Kind, string Provider, string Value);

    /// <summary>
    /// What the store holds, and the lookups the surfaces read it through.
    /// <para>
    /// Both lookups are built once, here, out of the set that is about to be published, and neither
    /// is edited afterwards. That is what makes them safe to hand out: a reader that took this
    /// snapshot holds a value nothing will change under it, which is the same promise the set
    /// itself carries.
    /// </para>
    /// <para>
    /// Building them costs one pass over the set on every write. That is the same order as
    /// serialising the set, which the write has just done, so a write does not change shape for
    /// having two lookups to rebuild. Keeping them up to date incrementally instead would mean an
    /// edit in place, and an edit in place is what the snapshot exists to avoid.
    /// </para>
    /// </summary>
    private sealed class Snapshot
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Snapshot"/> class over a set that is about
        /// to be published.
        /// </summary>
        /// <param name="held">The set. It may not be written to after this call.</param>
        public Snapshot(Dictionary<Guid, StoredRequest> held)
        {
            Held = held;

            var byUser = new Dictionary<Guid, List<StoredRequest>>();
            var byProviderIdentifier = new Dictionary<ProviderKey, List<StoredRequest>>(ProviderKeyComparer.Instance);
            var byWant = new Dictionary<Guid, StoredRequest>();

            foreach (var stored in held.Values)
            {
                // Distinct, and the person who asked appended to the people who joined rather than
                // assumed to be absent from them. The record says the two lists never overlap and
                // nothing in the type refuses one that does, so a request that carried a person
                // twice would otherwise put it in their list twice.
                foreach (var user in stored.Request.JoinedByUserIds.Append(stored.Request.RequestedByUserId).Distinct())
                {
                    Under(byUser, user).Add(stored);
                }

                // Distinct under the same comparison the lookup uses, because a request may carry
                // `Tmdb` and `tmdb` as two entries of its own map and they are one identifier here.
                foreach (var identifier in stored.Request.ProviderIds
                    .Select(entry => new ProviderKey(stored.Request.Kind, entry.Key, entry.Value))
                    .Distinct(ProviderKeyComparer.Instance))
                {
                    Under(byProviderIdentifier, identifier).Add(stored);
                }

                // One request per want and not a list. A want is absorbed once, so two requests
                // claiming one would be a defect rather than a case to serve, and the last writer
                // winning here would hide it. The set on the record refuses a repeat inside one
                // request; nothing refuses two records claiming the same want, and this is where
                // that would show as a wrong answer rather than as a crash.
                foreach (var want in stored.Request.WantIds)
                {
                    byWant[want] = stored;
                }
            }

            ByWant = byWant;
            ByUser = byUser.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray());
            ByProviderIdentifier = byProviderIdentifier.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.ToArray(),
                ProviderKeyComparer.Instance);
        }

        /// <summary>
        /// Gets every request, by its own identifier.
        /// </summary>
        public Dictionary<Guid, StoredRequest> Held { get; }

        /// <summary>
        /// Gets the requests each person is waiting for, whether they asked first or joined.
        /// </summary>
        public Dictionary<Guid, StoredRequest[]> ByUser { get; }

        /// <summary>
        /// Gets the requests carrying each external identifier.
        /// </summary>
        public Dictionary<ProviderKey, StoredRequest[]> ByProviderIdentifier { get; }

        /// <summary>
        /// Gets the request that absorbed each want the sibling handed over.
        /// </summary>
        public Dictionary<Guid, StoredRequest> ByWant { get; }

        /// <summary>
        /// The list under a key, made if it is the first one.
        /// </summary>
        /// <typeparam name="TKey">What the lookup is keyed by.</typeparam>
        /// <param name="lookup">The lookup being built.</param>
        /// <param name="key">The key.</param>
        /// <returns>The list to add to.</returns>
        private static List<StoredRequest> Under<TKey>(Dictionary<TKey, List<StoredRequest>> lookup, TKey key)
            where TKey : notnull
        {
            if (!lookup.TryGetValue(key, out var under))
            {
                under = [];
                lookup[key] = under;
            }

            return under;
        }
    }

    /// <summary>
    /// How two external identifiers are compared, which is the comparison
    /// <see cref="RequestIdentity"/> makes and not the one a dictionary makes by default.
    /// <para>
    /// The provider name matches without case, because the same provider is spelled <c>Tmdb</c> and
    /// <c>tmdb</c> by different callers and neither is wrong. The value matches exactly, because it
    /// is somebody else's identifier and this plugin does not get to decide that two of them that
    /// differ are the same. A store comparing either differently from
    /// <see cref="RequestIdentity"/> would let a fulfilment sweep and a duplicate check disagree
    /// about whether two things are one thing.
    /// </para>
    /// </summary>
    private sealed class ProviderKeyComparer : IEqualityComparer<ProviderKey>
    {
        /// <summary>
        /// Gets the one instance. It holds nothing, so a second would be a second object with the
        /// same answers.
        /// </summary>
        public static ProviderKeyComparer Instance { get; } = new ProviderKeyComparer();

        /// <inheritdoc />
        public bool Equals(ProviderKey x, ProviderKey y)
            => x.Kind == y.Kind
                && string.Equals(x.Provider, y.Provider, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Value, y.Value, StringComparison.Ordinal);

        /// <inheritdoc />
        public int GetHashCode(ProviderKey obj)
            => HashCode.Combine(
                obj.Kind,
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Provider),
                StringComparer.Ordinal.GetHashCode(obj.Value));
    }
}
