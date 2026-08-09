namespace Jellyfin.Plugin.Requests.Storage;

/// <summary>
/// What a page of the queue is ordered by. Three values rather than every field on the record,
/// because an order the store offers is an order it has to keep stable across pages, and a column
/// nobody sorts by is a promise nobody needed.
/// <para>
/// Whatever is chosen here, requests that compare equal under it are ordered by their own
/// identifier, so two requests made in the same tick hold one position however the store happens to
/// be enumerated that moment. That is what makes the order total, and a total order is what lets a
/// reader turn a page without a row moving underneath them for a reason that has nothing to do with
/// the column they sorted by.
/// </para>
/// </summary>
public enum RequestQueryOrder
{
    /// <summary>
    /// When it was asked for. The order a queue is read in by default: an operator works through
    /// what has been waiting longest.
    /// </summary>
    RequestedAt = 0,

    /// <summary>
    /// When the state last moved. What an operator reads to see what has happened recently, which is
    /// a different question from what has been waiting longest.
    /// </summary>
    StateChangedAt = 1,

    /// <summary>
    /// The title as it read when the request was made. Compared as text a person reads rather than
    /// as bytes, so an accented title sorts beside its unaccented neighbour instead of after every
    /// unaccented title there is.
    /// </summary>
    DisplayTitle = 2
}
