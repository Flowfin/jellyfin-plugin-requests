namespace Jellyfin.Plugin.Requests.Localisation;

/// <summary>
/// The three sentences a person is given when the answer is no, or not yet, named by the key each
/// one is under in the catalogue.
/// <para>
/// <b>Three outcomes rather than three surfaces.</b> Waiting, declined and at their quota are what
/// most people meet, and each of them is easy to leave as a state name or a status code. A person
/// who reads <c>Open</c> and nothing else asks the operator what it means, which is the message
/// this plugin exists to remove; a person who meets a 409 with no sentence tries again, because
/// nothing told them that trying again is not what unblocks it.
/// </para>
/// <para>
/// <b>The keys are declared here so the sentence is written once.</b> A page names a key as a
/// literal because it is markup and has nothing else to name it with, and everything on this side
/// of the API names it from here. What that buys is one place a reader can enumerate the three
/// from, which is what <c>PageWordsTests</c> holds the catalogue against: a sentence added to
/// <c>en.json</c> and named nowhere, or named here and absent from the catalogue, is a red suite
/// rather than a blank line on somebody's screen.
/// </para>
/// <para>
/// What is deliberately not here is a fourth entry per state. A person's own page draws a word for
/// every state it can show, out of the <c>mine.state</c> group, and those are labels for a column.
/// These three are sentences, they say what happens next rather than where a thing stands, and
/// mixing the two sets would leave nobody able to say which of them a surface owes a person.
/// </para>
/// </summary>
public static class Sentences
{
    /// <summary>
    /// Nobody has answered this yet. The one most likely to send somebody to the operator, because
    /// waiting looks the same as nothing having happened.
    /// </summary>
    public const string Waiting = "outcome.waiting";

    /// <summary>
    /// The answer was no. The reason and whatever the operator wrote are separate values a surface
    /// draws beside it, so this sentence carries neither and does not have to be rewritten when a
    /// reason is added to the model.
    /// </summary>
    public const string Declined = "outcome.declined";

    /// <summary>
    /// The person is already waiting for as many things as this install allows. It carries two
    /// placeholders, how many they hold and how many they are allowed, in the numbered form the
    /// pages use, so the same string serves a surface that formats it in a browser and one that
    /// formats it here.
    /// </summary>
    public const string AtTheirQuota = "outcome.atQuota";
}
