using System;
using System.Buffers;
using System.Text.Json;
using Jellyfin.Plugin.Requests.Model;

namespace Jellyfin.Plugin.Requests.Storage;

/// <summary>
/// The read-time migration from the first on-disk shape to the second: a history entry stops naming
/// the person who moved a request and says what kind of caller they were instead.
/// <para>
/// <b>Why this is a shape version rather than a field added beside the old one.</b> The decision on
/// #49 is that every entry means the same thing, so there is no entry carrying an identifier and no
/// reader having to know which of two formats it is holding. That makes the older bytes wrong rather
/// than incomplete, which is the rule in <c>docs/storage.md</c> for when a number has to go up.
/// </para>
/// <para>
/// <b>It is a read and it writes nothing.</b> What comes back is a second document in memory. The
/// file on the disk is untouched until some later write replaces it whole, so a server opened by
/// this version and then put back to the older one finds the file it left.
/// </para>
/// <para>
/// <b>What the role is derived from, and what it cannot recover.</b> The older shape recorded an
/// identifier or nothing, so three cases are all it can distinguish: an arrival entry is the ask and
/// its only possible caller is the person the request is filed against; an entry with no identifier
/// is a move the plugin made after looking at the library; and any other entry is a move, which no
/// cell of the transition table admits from anybody but an administrator. What is lost is the one
/// distinction the older shape never held either: an administrator acting on a request they asked
/// for themselves reads as <see cref="RequestActor.Administrator"/> here, where this version writing
/// the same move fresh would record both roles. That is stated rather than guessed around, because a
/// migration inventing the second role would be putting a fact into a record that never carried one.
/// </para>
/// </summary>
internal static class HistoryWithoutPeople
{
    /// <summary>
    /// The property the older shape carried on each history entry.
    /// </summary>
    private const string TheOldPersonField = "ByUserId";

    /// <summary>
    /// The property this shape carries instead.
    /// </summary>
    private const string TheRoleField = "By";

    /// <summary>
    /// Rewrites one parsed store document so every history entry carries a role instead of a person.
    /// </summary>
    /// <param name="root">The document as it was read, either a versioned object or the bare array
    /// the shape before the version was.</param>
    /// <returns>The same requests with the history entries migrated. The caller owns the returned
    /// document and disposes it.</returns>
    public static JsonDocument Migrated(JsonElement root)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            if (root.ValueKind == JsonValueKind.Array)
            {
                WriteEntries(writer, root);
            }
            else
            {
                WriteDocument(writer, root);
            }
        }

        return JsonDocument.Parse(buffer.WrittenMemory);
    }

    /// <summary>
    /// Copies a versioned document, migrating the requests inside it.
    /// </summary>
    /// <param name="writer">Where the migrated document is built.</param>
    /// <param name="document">The document as it was read.</param>
    private static void WriteDocument(Utf8JsonWriter writer, JsonElement document)
    {
        writer.WriteStartObject();

        foreach (var property in document.EnumerateObject())
        {
            if (string.Equals(property.Name, "Requests", StringComparison.Ordinal)
                && property.Value.ValueKind == JsonValueKind.Array)
            {
                writer.WritePropertyName(property.Name);
                WriteEntries(writer, property.Value);

                continue;
            }

            property.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Copies the list of stored requests, migrating the request inside each entry.
    /// </summary>
    /// <param name="writer">Where the migrated list is built.</param>
    /// <param name="entries">The list as it was read.</param>
    private static void WriteEntries(Utf8JsonWriter writer, JsonElement entries)
    {
        writer.WriteStartArray();

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                // Not something this migration understands, and not something it may drop either:
                // it is copied through so the loader refuses it for what it is rather than for a
                // shape this rewrite gave it.
                entry.WriteTo(writer);

                continue;
            }

            writer.WriteStartObject();

            foreach (var property in entry.EnumerateObject())
            {
                if (string.Equals(property.Name, "Request", StringComparison.Ordinal)
                    && property.Value.ValueKind == JsonValueKind.Object)
                {
                    writer.WritePropertyName(property.Name);
                    WriteRequest(writer, property.Value);

                    continue;
                }

                property.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// Copies one request, migrating its history.
    /// </summary>
    /// <param name="writer">Where the migrated request is built.</param>
    /// <param name="request">The request as it was read.</param>
    private static void WriteRequest(Utf8JsonWriter writer, JsonElement request)
    {
        writer.WriteStartObject();

        foreach (var property in request.EnumerateObject())
        {
            if (string.Equals(property.Name, "History", StringComparison.Ordinal)
                && property.Value.ValueKind == JsonValueKind.Array)
            {
                writer.WritePropertyName(property.Name);
                WriteHistory(writer, property.Value);

                continue;
            }

            property.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Copies a history, replacing the person on each entry with what that person was.
    /// </summary>
    /// <param name="writer">Where the migrated history is built.</param>
    /// <param name="history">The history as it was read.</param>
    private static void WriteHistory(Utf8JsonWriter writer, JsonElement history)
    {
        writer.WriteStartArray();

        foreach (var entry in history.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                entry.WriteTo(writer);

                continue;
            }

            writer.WriteStartObject();

            foreach (var property in entry.EnumerateObject())
            {
                // The identifier does not survive the migration. That is the whole point of the
                // shape change and it is why this is a read-time rewrite rather than a field added:
                // an entry that still carried it would be an entry still naming a person.
                if (string.Equals(property.Name, TheOldPersonField, StringComparison.Ordinal)
                    || string.Equals(property.Name, TheRoleField, StringComparison.Ordinal))
                {
                    continue;
                }

                property.WriteTo(writer);
            }

            writer.WriteNumber(TheRoleField, (int)RoleOn(entry));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// What the mover of one older history entry was, derived from what that entry held.
    /// </summary>
    /// <param name="entry">The entry as the older shape wrote it.</param>
    /// <returns>The role that entry records, under the reading set out on this class.</returns>
    private static RequestActor RoleOn(JsonElement entry)
    {
        if (entry.TryGetProperty("Arrival", out var arrival) && arrival.ValueKind != JsonValueKind.Null)
        {
            return RequestActor.Requester;
        }

        if (!entry.TryGetProperty(TheOldPersonField, out var mover) || mover.ValueKind == JsonValueKind.Null)
        {
            return RequestActor.Plugin;
        }

        return RequestActor.Administrator;
    }
}
