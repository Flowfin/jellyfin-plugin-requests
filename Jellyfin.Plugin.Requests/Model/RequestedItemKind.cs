namespace Jellyfin.Plugin.Requests.Model;

/// <summary>
/// What sort of thing was asked for. The kind decides how fulfilment is recognised later, because
/// a film is one item in the library and a series is a set of them, so it is carried on the record
/// rather than inferred from the title.
/// <para>
/// Which kinds an install accepts is configuration and is #95; decision 7 on #113 is where the set
/// shipped at 1.0 is settled. This enum is what the record can express, not what any install
/// allows, and the two are different questions.
/// </para>
/// </summary>
public enum RequestedItemKind
{
    /// <summary>
    /// One film.
    /// </summary>
    Movie = 0,

    /// <summary>
    /// A series. Which seasons were asked for is on the request rather than here, in
    /// <see cref="MediaRequest.Seasons"/>, where empty means the whole series.
    /// </summary>
    Series = 1
}
