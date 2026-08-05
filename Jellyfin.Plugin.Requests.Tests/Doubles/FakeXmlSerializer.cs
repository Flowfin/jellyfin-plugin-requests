using System;
using System.Collections.Generic;
using System.IO;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// The host's XML serializer, holding what it was given in memory. Round trips through a real
/// serializer are a property of the persisted shape rather than of the host, so this double keeps
/// the object itself and a test that cares about serialisation asserts on the real thing instead.
/// </summary>
internal sealed class FakeXmlSerializer : IXmlSerializer
{
    private readonly Dictionary<string, object> byPath = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the number of times anything was written to a file.
    /// </summary>
    public int WriteCount { get; private set; }

    /// <inheritdoc />
    public object? DeserializeFromStream(Type type, Stream stream) => null;

    /// <inheritdoc />
    public object? DeserializeFromFile(Type type, string file)
        => byPath.TryGetValue(file, out var stored) ? stored : null;

    /// <inheritdoc />
    public object? DeserializeFromBytes(Type type, byte[] buffer) => null;

    /// <inheritdoc />
    public void SerializeToStream(object obj, Stream stream)
    {
        WriteCount++;
    }

    /// <inheritdoc />
    public void SerializeToFile(object obj, string file)
    {
        ArgumentNullException.ThrowIfNull(obj);
        byPath[file] = obj;
        WriteCount++;
    }
}
