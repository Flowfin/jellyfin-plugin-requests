namespace Jellyfin.Plugin.Requests.Localisation;

/// <summary>
/// The words the channel names that no asset draws, named by the key each one is under in the
/// catalogue.
/// <para>
/// <b>Why a class and not a literal at the call site.</b> Everything else this plugin shows a
/// person is drawn by a page, and a page names its keys as literals because it is markup with
/// nothing else to name them with, so <c>PageWordsTests</c> finds them by reading the assets. The
/// channel draws nothing: it hands the server a folder tree and the server renders it on whatever
/// client somebody is holding. A key it names is therefore invisible to that reading, and a key
/// invisible to that reading is one nobody notices has been left behind after the sentence that
/// used it is gone.
/// </para>
/// <para>
/// So the keys the channel names and no page draws are declared here, the same way
/// <see cref="Sentences"/> declares the one outcome that is met while asking rather than while
/// reading. The rest of what the channel says is already named by a page and is looked up by the
/// same key on both surfaces on purpose, so the two cannot drift into two answers.
/// </para>
/// </summary>
public static class ChannelWords
{
    /// <summary>
    /// What the channel is, in the one line a client shows under its name. It says that nothing
    /// here plays, because a folder tree beside somebody's libraries looks like media and the rows
    /// in it are records that somebody asked for something.
    /// </summary>
    public const string Description = "mine.channel.description";

    /// <summary>
    /// The one folder every caller is answered with, which says where a person reads their own
    /// requests. The channel stopped answering them itself when #67 measured that an answer does
    /// not stay one person's on a running server, and <see cref="Surface.RequestsChannel"/> carries
    /// that reading and what follows from it.
    /// </summary>
    public const string WhereToLook = "mine.channel.whereToLook";
}
