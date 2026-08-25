using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Requests.Notify;

/// <summary>
/// What this plugin keeps about who does not want to be told, in one small file in the plugin's own
/// data directory beside the requests.
/// <para>
/// <b>It keeps the refusals and never a row per person.</b> The default is on, so a person who has
/// never touched this has nothing here, and an install nobody has touched has no file at all. That
/// is what makes the third condition of #287 a property of the shape rather than a default value
/// somebody has to remember to set.
/// </para>
/// <para>
/// <b>It is not the plugin configuration and it may not be.</b> The configuration file is rewritten
/// whole by the dashboard whenever an administrator saves the page, and this is a list that grows
/// with the number of people on the server; <c>docs/storage.md</c> gives both reasons for requests
/// and they are the same ones here. It also belongs to a different owner: what is in the
/// configuration is the operator's to change, and this is not.
/// </para>
/// <para>
/// <b>Whole or not at all, the same way the request store writes.</b> A write serialises the whole
/// set into <see cref="PendingFileName"/>, flushes it, and only then replaces <see cref="FileName"/>
/// in one step, so a process that dies mid-write leaves either the set before it or the set after
/// it. A file that cannot be read is refused with <see cref="NoticePreferencesException"/> rather
/// than treated as empty, because an empty set here reads as everybody wanting to be told.
/// </para>
/// </summary>
public sealed class FileNoticePreferences : INoticePreferences, IDisposable
{
    /// <summary>
    /// The file the loader reads. It is only ever created by replacing it whole.
    /// </summary>
    public const string FileName = "notices.json";

    /// <summary>
    /// The file a write is built in before it replaces <see cref="FileName"/>. This is the only file
    /// that can be found half written, and nothing ever reads it.
    /// </summary>
    public const string PendingFileName = "notices.json.writing";

    /// <summary>
    /// The shape this writes, and the highest it can read. A file carrying a higher number is
    /// refused rather than read as if the fields it has never heard of were absent, which is the
    /// rule the request store keeps and the reason a downgraded server leaves a newer file alone.
    /// </summary>
    public const int OnDiskVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
    {
        WriteIndented = false
    };

    private readonly string _directoryPath;
    private readonly string _filePath;
    private readonly string _pendingFilePath;

    /// <summary>
    /// Held for the whole of a write, so no two writes build the pending file at once and no write
    /// decides against a set another write has already replaced.
    /// </summary>
    private readonly SemaphoreSlim _writes = new SemaphoreSlim(1, 1);

    /// <summary>
    /// Who has turned it off, or null before the first call has read the file. Replaced whole and
    /// never edited, so a reader takes the current one without waiting and never sees half a change.
    /// </summary>
    private HashSet<Guid>? _quiet;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileNoticePreferences"/> class.
    /// </summary>
    /// <param name="directoryPath">The plugin's own data directory.</param>
    /// <exception cref="ArgumentNullException">Where there is no directory to keep the file in.</exception>
    public FileNoticePreferences(string directoryPath)
    {
        ArgumentNullException.ThrowIfNull(directoryPath);

        _directoryPath = directoryPath;
        _filePath = Path.Combine(directoryPath, FileName);
        _pendingFilePath = Path.Combine(directoryPath, PendingFileName);
    }

    /// <inheritdoc />
    public async Task<bool> TellsThemAsync(Guid userId, CancellationToken cancellationToken)
    {
        var quiet = await HeldAsync(cancellationToken).ConfigureAwait(false);

        return !quiet.Contains(userId);
    }

    /// <inheritdoc />
    public async Task<bool> SetAsync(Guid userId, bool tellsThem, CancellationToken cancellationToken)
    {
        await _writes.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var quiet = new HashSet<Guid>(LoadedWhileHoldingTheWriteLock());

            // Nothing is written where nothing changes. A person opening their page and leaving the
            // control alone should not touch a file, and a write replacing a file with the same
            // bytes is a write that can fail for a reason nobody asked for.
            if (tellsThem ? !quiet.Remove(userId) : !quiet.Add(userId))
            {
                return tellsThem;
            }

            await PersistAsync(quiet, cancellationToken).ConfigureAwait(false);

            // After the disk has taken it and never before, so what this plugin acts on and what
            // survives a restart cannot disagree.
            Volatile.Write(ref _quiet, quiet);

            return tellsThem;
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
    /// The refusal, with the reason in it and never the path: the sentence reaches an operator
    /// through the log and a caller through a status code, and neither of them is served by a path
    /// on somebody else's disk.
    /// </summary>
    /// <param name="because">Why the file was refused.</param>
    /// <param name="reason">What the runtime raised underneath, where anything did.</param>
    /// <returns>The refusal to throw.</returns>
    private static NoticePreferencesException Refusing(string because, Exception? reason = null)
    {
        var said = "What this plugin keeps about who wants to be told about their own requests could not be read, because " + because;

        return reason is null ? new NoticePreferencesException(said) : new NoticePreferencesException(said, reason);
    }

    /// <summary>
    /// The set, reading the file on the first call that needs it.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait for the first read.</param>
    /// <returns>Who has turned it off.</returns>
    private async Task<HashSet<Guid>> HeldAsync(CancellationToken cancellationToken)
    {
        var quiet = Volatile.Read(ref _quiet);

        if (quiet is not null)
        {
            return quiet;
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
    /// <see cref="_writes"/>.
    /// </summary>
    /// <returns>Who has turned it off.</returns>
    private HashSet<Guid> LoadedWhileHoldingTheWriteLock()
    {
        var quiet = _quiet;

        if (quiet is not null)
        {
            return quiet;
        }

        quiet = Read();
        Volatile.Write(ref _quiet, quiet);
        return quiet;
    }

    /// <summary>
    /// Reads the file, refusing anything it cannot read whole.
    /// </summary>
    /// <returns>Who has turned it off, and nobody where there is no file yet.</returns>
    private HashSet<Guid> Read()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        PersistedNotices? persisted;

        try
        {
            using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            persisted = JsonSerializer.Deserialize<PersistedNotices>(stream, SerializerOptions);
        }
        catch (JsonException reason)
        {
            throw Refusing(
                "it is not the document this keeps, which is what an interruption in the middle of the file would leave if a write were made in place.",
                reason);
        }

        if (persisted is null)
        {
            throw Refusing("it holds nothing at all, and an empty file here would read as everybody wanting to be told.");
        }

        if (persisted.Version > OnDiskVersion)
        {
            throw Refusing("it was written by a later version of this plugin, so what its fields mean is not something this version can know.");
        }

        var quiet = new HashSet<Guid>();

        foreach (var userId in persisted.Quiet ?? [])
        {
            if (userId == Guid.Empty)
            {
                throw Refusing("one of the entries names nobody, and a setting kept against nobody silences whoever the empty identifier is next taken for.");
            }

            quiet.Add(userId);
        }

        return quiet;
    }

    /// <summary>
    /// Puts the set on the disk, whole or not at all. Every failure here reaches the caller, so a
    /// full disk is reported rather than absorbed and the in-memory set is left as it was.
    /// </summary>
    /// <param name="quiet">Who has turned it off.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the set is on the disk.</returns>
    private async Task PersistAsync(HashSet<Guid> quiet, CancellationToken cancellationToken)
    {
        var persisted = new PersistedNotices
        {
            Version = OnDiskVersion,
            Quiet = [.. quiet.OrderBy(userId => userId)]
        };

        Directory.CreateDirectory(_directoryPath);

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
    }

    /// <summary>
    /// The document on the disk: a version and the identifiers of the people who said no.
    /// </summary>
    private sealed record PersistedNotices
    {
        /// <summary>
        /// Gets the shape the file is in.
        /// </summary>
        public int Version { get; init; }

        /// <summary>
        /// Gets who has turned it off.
        /// </summary>
        public IReadOnlyList<Guid>? Quiet { get; init; }
    }
}
