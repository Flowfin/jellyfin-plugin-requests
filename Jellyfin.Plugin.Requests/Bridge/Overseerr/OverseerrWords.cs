using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Jellyfin.Plugin.Requests.Bridge.Overseerr;

/// <summary>
/// The step between the numbers the Overseerr form reports and the words the mapping table is keyed
/// on, and the one place it lives.
/// <para>
/// The form reports a request's status as a number, and <see cref="BackendStates"/> holds its rows
/// under words, because words are what a reader of <c>docs/bridge.md</c> can argue with and what two
/// adapters against two services can share. Something has to turn one into the other, and
/// <c>docs/bridge.md</c> records that where that step lives is a decision rather than a discovery:
/// an adapter turning <c>3</c> into <c>DECLINED</c> inside its own code would move the argument
/// about what a word means one layer below the table that exists to settle it. So it is a second
/// table, beside the first and read by nothing but this adapter, and the suite compares the two:
/// every word this table produces is a row the mapping holds, and every request-status row the
/// mapping holds has a number here.
/// </para>
/// <para>
/// <b>The numbers are read off the form's own implementation and not off its description.</b> The
/// description names three of them; the enumeration in the form's own source names five, and the
/// two extra are the ones that matter most here: <c>FAILED</c> is the one word that moves a request
/// on evidence only the service holds. <c>docs/bridge.md</c> quotes both, with the commands that
/// fetched them, and says how such a reading goes stale.
/// </para>
/// <para>
/// <b>A number nothing here knows is reported as its own digits.</b> The mapping table's rule for a
/// word it has never seen is to move nothing and say so, and handing it the digits is what lets that
/// rule fire on a number the form did not have when this was written, rather than this table guessing
/// at the nearest word. The log line the reconciliation writes then carries the number an operator
/// can look up.
/// </para>
/// </summary>
public static class OverseerrWords
{
    /// <summary>
    /// Gets what each request-status number the form reports means, as the word the mapping table
    /// holds it under. In the order the form's own enumeration declares them.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<int, string>> RequestStatuses { get; } =
        new ReadOnlyCollection<KeyValuePair<int, string>>(
            new[]
            {
                new KeyValuePair<int, string>(1, "PENDING"),
                new KeyValuePair<int, string>(2, "APPROVED"),
                new KeyValuePair<int, string>(3, "DECLINED"),
                new KeyValuePair<int, string>(4, "FAILED"),
                new KeyValuePair<int, string>(5, "COMPLETED")
            });

    /// <summary>
    /// The word for a request-status number the form reported.
    /// </summary>
    /// <param name="number">The number, as the form reported it.</param>
    /// <returns>
    /// The word the mapping table holds that number under, or the number's own digits where no word
    /// is known for it, so that the mapping table's rule for an unseen word is what answers.
    /// </returns>
    public static string RequestStatus(long number)
    {
        foreach (var row in RequestStatuses)
        {
            if (row.Key == number)
            {
                return row.Value;
            }
        }

        return number.ToString(CultureInfo.InvariantCulture);
    }
}
