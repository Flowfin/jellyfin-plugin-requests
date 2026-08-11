// Fixture for no-call-to-a-metadata-source. This file is in no project and is
// never compiled; it exists so the rule can be watched refusing the mistake it
// names.
//
// The near-miss is the one somebody writes while fixing a real complaint. An
// operator says the queue shows a film under its old name, or with no year, and
// the shortest repair is to ask the server to look the title up again. It works
// on the machine it was written on, it puts this plugin under a metadata
// source's terms, and it makes the queue unreadable on the day nothing outbound
// resolves.

namespace Jellyfin.Plugin.Requests.Fixtures;

internal sealed class AsksAMetadataSourceForATitle
{
    // Legal neighbour, left here on purpose: this is where a title comes from,
    // and the rule has to stay quiet on it.
    public static string Title(MediaRequest request) => request.DisplayTitle;

    // The regression as it arrives through the host: the plugin asks the server
    // to fetch or refresh the record.
    public static async Task<string> FreshTitleAsync(IProviderManager providers, MediaRequest request)
    {
        var results = await providers.GetRemoteSearchResults(request.ProviderIds).ConfigureAwait(false);

        return results[0].Name;
    }

    // The same regression written against the source directly, which is what
    // somebody reaches for when the host's route looks like too much work.
    public static Uri LookUp(string tmdbId)
        => new Uri("https://api.themoviedb.org/3/movie/" + tmdbId);
}
