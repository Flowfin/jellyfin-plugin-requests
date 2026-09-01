using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Jellyfin.Plugin.Requests.Api;
using Jellyfin.Plugin.Requests.Model;
using Microsoft.AspNetCore.Mvc;
using Xunit;

using PluginUnderTest = global::Jellyfin.Plugin.Requests.Plugin;

namespace Jellyfin.Plugin.Requests.Tests.Web;

/// <summary>
/// What an operator may do to a row from the queue page, held against the two things that decide
/// it: the transition table, which says which moves exist, and the API, which says how one is made.
/// <para>
/// The page carries a copy of both, and it has to: the queue answer says what state a request is in
/// and not which moves are open on it, so the buttons are drawn from a table written in the page.
/// A second copy of a rule is the thing that drifts, and the drift is silent in both directions. A
/// cell that opens in the model and not here leaves a move nobody can make from the surface built
/// for making it; a cell that closes in the model and not here leaves a button whose only outcome
/// is a refusal, which reads to the operator as the plugin being broken.
/// </para>
/// <para>
/// The bound is the one every check over these assets carries, and it is stated rather than left to
/// be discovered: this reads the page as the assembly carries it and drives nothing. Whether a
/// browser draws the buttons, and what happens when one is pressed, is behaviour, and running a
/// browser to ask is what <c>docs/testing.md</c> refuses.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class DecideFromTheQueueTests
{
    /// <summary>
    /// The one place the two vocabularies meet: a state the table moves a request into, and the
    /// endpoint that makes that move.
    /// <para>
    /// It is here rather than in the page because the page names endpoints and the table names
    /// states, and nothing in either says they are the same six moves. Both sides of it are checked
    /// below: every name in it is a route the controller declares, and every state an administrator
    /// may move a request into is a key of it, so a decision added to the table with no endpoint is
    /// a red suite rather than a button that posts to nothing.
    /// </para>
    /// </summary>
    private static readonly Dictionary<RequestState, string> MadeBy = new()
    {
        [RequestState.Approved] = "Approve",
        [RequestState.Declined] = "Decline"
    };

    /// <summary>
    /// Every decision an operator may make is offered by the queue, and no other.
    /// <para>
    /// Both halves matter and they fail differently. A move the table allows and the page omits is
    /// work that has no surface; a move the page offers and the table refuses is a button that
    /// spends a call to be told no. The expected side is built from the table itself rather than
    /// listed here, so the day a cell moves this reds without anybody remembering to come back.
    /// </para>
    /// <para>
    /// Fulfilment is not among them and its absence is the table's answer rather than a gap in the
    /// page: arriving in the library is an observation the plugin makes, and no cell into
    /// <see cref="RequestState.Fulfilled"/> admits an administrator.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryDecisionAnOperatorMayMakeIsOfferedByTheQueue()
    {
        var offered = DecisionsIn(Queue());

        var expected = Enum.GetValues<RequestState>().ToDictionary(
            from => from.ToString(),
            from => Decisions(from),
            StringComparer.Ordinal);

        Assert.NotEmpty(expected.Values.SelectMany(moves => moves));
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), offered.Keys.Order(StringComparer.Ordinal));

        foreach (var (state, moves) in expected)
        {
            Assert.Equal(moves, offered[state], StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Every state an administrator may move a request into has an endpoint that makes the move.
    /// <para>
    /// Without this the leg above compares the page against a table filtered through a map that
    /// silently drops what it does not know, and a decision added to the model would be missing from
    /// both sides at once.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryStateAnOperatorMayMoveARequestIntoHasAnEndpoint()
    {
        var reachable = RequestLifecycle.Table
            .Where(IsADecision)
            .Select(cell => cell.To)
            .Distinct()
            .ToArray();

        // A state an operator may move a request into that nothing above names an endpoint for. The
        // map is what the leg before this one filters the table through, so a state missing from it
        // would drop out of both sides of that comparison at once and leave it green.
        var withNoEndpoint = reachable.Where(state => !MadeBy.ContainsKey(state)).ToArray();

        Assert.NotEmpty(reachable);
        Assert.Empty(withNoEndpoint);
    }

    /// <summary>
    /// Every decision the queue posts is a route this API declares.
    /// <para>
    /// The page builds the address it posts to out of the words it names a move by, so a word that
    /// is not a route is a button that answers with a not-found the operator cannot act on. The
    /// routes are reflected off the controller rather than written here, so renaming one on the
    /// server side reds this rather than the page.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryDecisionTheQueuePostsIsARouteTheApiDeclares()
    {
        var declared = typeof(RequestsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(method => method.GetCustomAttributes<HttpPostAttribute>())
            .Select(route => route.Template ?? string.Empty)
            .ToArray();

        var posted = DecisionsIn(Queue())
            .Values
            .SelectMany(moves => moves)
            .Distinct(StringComparer.Ordinal)
            .Concat(MadeBy.Values)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(posted);

        var unroutable = posted
            .Where(move => !declared.Contains("Requests/{id}/" + move, StringComparer.Ordinal))
            .ToArray();

        Assert.Empty(unroutable);
    }

    /// <summary>
    /// Every reason an operator may give is one the page offers, every reason they may not is one it
    /// does not, and the list opens on none of them.
    /// <para>
    /// The reasons are a closed set in the model and a list of options on the page, which is the
    /// same second-copy problem the decisions above have. A reason added to the model and not here
    /// is one no operator can give; an option here that the model does not know is a decline the
    /// endpoint refuses after the operator has written the sentence beside it.
    /// </para>
    /// <para>
    /// <b>The set is not the enumeration any more, and it is asked of the lifecycle rather than
    /// listed here.</b> <see cref="DeclineReason.TheRequesterIsGone"/> is a fact the plugin
    /// establishes when the server reports a deleted account, and
    /// <see cref="RequestLifecycle.Decline"/> refuses it from an administrator, so offering it would
    /// be an option that reds the endpoint after the operator has chosen it. Naming it here as an
    /// exception would put the rule in two places; asking which reasons an administrator may
    /// actually give keeps one, and a seventh reason arrives on the page or is refused from it by
    /// the same reading.
    /// </para>
    /// <para>
    /// The option that chooses nothing is required to be the selected one. A list that opens on a
    /// real reason is a page where the fastest decline carries whatever was at the top, which is the
    /// arbitrary decline a required reason exists to prevent.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryReasonADeclineMayCarryIsOfferedByThePage()
    {
        var options = OptionsOf(Queue(), "RequestsQueueDeclineReason");

        var offered = options
            .Where(option => option.Value.Length > 0)
            .Select(option => option.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var choosable = Enum.GetValues<DeclineReason>()
            .Where(AnOperatorMayGive)
            .Select(reason => reason.ToString())
            .Order(StringComparer.Ordinal)
            .ToArray();

        // The reading is worth nothing if it answers yes to everything, so the two sets are known to
        // differ before they are compared.
        Assert.NotEqual(Enum.GetNames<DeclineReason>().Length, choosable.Length);

        Assert.Equal(choosable, offered, StringComparer.Ordinal);

        var opensOn = options.Where(option => option.Selected).ToArray();

        Assert.Single(opensOn);
        Assert.Empty(opensOn[0].Value);
    }

    /// <summary>
    /// Whether an administrator may decline an open request for this reason, asked of the lifecycle
    /// rather than decided here.
    /// </summary>
    /// <param name="reason">The reason.</param>
    /// <returns><see langword="true"/> where the move is made rather than refused.</returns>
    private static bool AnOperatorMayGive(DeclineReason reason)
    {
        var open = new MediaRequest
        {
            Id = new Guid("00000000-0000-4000-8000-0000000000aa"),
            RequestedByUserId = new Guid("00000000-0000-4000-8000-0000000000bb"),
            RequestedAt = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero),
            Kind = RequestedItemKind.Movie,
            DisplayTitle = "Something somebody wanted",
            StateChangedAt = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero)
        };

        try
        {
            // A note beside every reason, because one of them requires it and this reading is not
            // about that rule.
            _ = RequestLifecycle.Decline(
                open,
                reason,
                note: "Why this was declined.",
                new DateTimeOffset(2026, 8, 31, 13, 0, 0, TimeSpan.Zero),
                RequestCaller.Administrator(new Guid("00000000-0000-4000-8000-0000000000cc")));

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// A refused decision puts back what the store holds now, and reads the queue again for nothing.
    /// <para>
    /// This is the condition of #61 that gets cut for time. The refusal from the endpoint carries the
    /// request as the store holds it, so the operator can be shown what they tried to do and what is
    /// actually there; a page that answered a refusal by reading the queue again would throw that
    /// away and would move the operator as well, because a row that no longer matches the filter it
    /// was read under takes every row under it up one line.
    /// </para>
    /// <para>
    /// The same reading holds the fourth condition, which has no other witness here: the filter, the
    /// order and the offset are the question the page asks, and the decision path never asks it.
    /// </para>
    /// </summary>
    [Fact]
    public void ARefusedDecisionShowsWhatTheStoreHoldsRatherThanReadingTheQueueAgain()
    {
        var body = Queue();

        var sending = Between(body, "function send(", "\n                    }");
        var refusal = Between(body, "function refused(", "\n                    }");
        var drawing = Between(body, "function draw(", "\n                    }");

        Assert.Contains("replace(answer)", sending, StringComparison.Ordinal);
        Assert.Contains("failure.Current", refusal, StringComparison.Ordinal);
        Assert.Contains("replace(failure.Current)", refusal, StringComparison.Ordinal);

        // The buttons exist on the rows at all. A refusal shown by a page that offers no decision is
        // a property of nothing.
        Assert.Contains("decide(row, request)", drawing, StringComparison.Ordinal);

        // The one-character version of the mistake: `read()` where the answer in hand was meant. It
        // is spelled with its brackets, so the prose above these functions, which says what reading
        // again would cost, is not what is being matched.
        Assert.DoesNotContain("read()", sending, StringComparison.Ordinal);
        Assert.DoesNotContain("read()", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// The moves one state admits, as the endpoints that make them.
    /// </summary>
    /// <param name="from">The state a request is in.</param>
    /// <returns>The moves, in the order the page is required to write them.</returns>
    private static string[] Decisions(RequestState from)
        => [.. RequestLifecycle.Table
            .Where(cell => cell.From == from)
            .Where(IsADecision)
            .Where(cell => MadeBy.ContainsKey(cell.To))
            .Select(cell => MadeBy[cell.To])
            .Order(StringComparer.Ordinal)];

    /// <summary>
    /// Whether one cell is a move an administrator may make.
    /// </summary>
    /// <param name="cell">The cell.</param>
    /// <returns><see langword="true"/> where the table allows it and admits an administrator.</returns>
    private static bool IsADecision(RequestTransition cell)
        => cell.IsLegal && (cell.Permitted & RequestActor.Administrator) != RequestActor.None;

    /// <summary>
    /// The table the page draws its buttons from, read out of the page.
    /// </summary>
    /// <param name="body">The page as it is embedded.</param>
    /// <returns>The moves the page offers, by the state it offers them in.</returns>
    private static Dictionary<string, string[]> DecisionsIn(string body)
    {
        const string Marker = "var decisions = {";

        var at = body.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(at >= 0, "The page does not say which decisions a row admits.");

        var start = at + Marker.Length;
        var written = body[start..body.IndexOf("};", start, StringComparison.Ordinal)];

        var offered = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var line in written.Split('\n'))
        {
            var named = line.IndexOf(':', StringComparison.Ordinal);
            var opens = line.IndexOf('[', StringComparison.Ordinal);
            var closes = line.IndexOf(']', StringComparison.Ordinal);

            if (named < 0 || opens < named || closes < opens)
            {
                continue;
            }

            var moves = line[(opens + 1)..closes]
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(move => move.Trim().Trim('"'))
                .Where(move => move.Length > 0)
                .Order(StringComparer.Ordinal);

            offered[line[..named].Trim()] = [.. moves];
        }

        return offered;
    }

    /// <summary>
    /// The options one select on the page carries.
    /// </summary>
    /// <param name="body">The page as it is embedded.</param>
    /// <param name="id">The select's identifier.</param>
    /// <returns>The value of each option and whether the browser opens on it.</returns>
    private static (string Value, bool Selected)[] OptionsOf(string body, string id)
    {
        var at = body.IndexOf("id=\"" + id + "\"", StringComparison.Ordinal);
        Assert.True(at >= 0, "The page carries no control with the identifier " + id + ".");

        var end = body.IndexOf("</select>", at, StringComparison.Ordinal);
        Assert.True(end >= 0, "The control " + id + " is not a select the page closes.");

        var options = body[at..end].Split("<option ", StringSplitOptions.RemoveEmptyEntries);

        return [.. options
            .Skip(1)
            .Select(option => (ValueOf(option), option[..option.IndexOf('>', StringComparison.Ordinal)].Contains("selected", StringComparison.Ordinal)))];
    }

    /// <summary>
    /// What one option carries as its value.
    /// </summary>
    /// <param name="option">The option, from its attributes onwards.</param>
    /// <returns>The value, which is empty for the option that chooses nothing.</returns>
    private static string ValueOf(string option)
    {
        const string Marker = "value=\"";

        var at = option.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(at >= 0, "An option of a select on this page carries no value.");

        var start = at + Marker.Length;

        return option[start..option.IndexOf('"', start)];
    }

    /// <summary>
    /// One block of the page, from a marker to the end of what it opens.
    /// </summary>
    /// <param name="body">The page.</param>
    /// <param name="opens">What the block starts with.</param>
    /// <param name="closes">What ends it.</param>
    /// <returns>The block, without either marker.</returns>
    private static string Between(string body, string opens, string closes)
    {
        var at = body.IndexOf(opens, StringComparison.Ordinal);
        Assert.True(at >= 0, "The page carries nothing beginning " + opens);

        var start = at + opens.Length;
        var end = body.IndexOf(closes, start, StringComparison.Ordinal);
        Assert.True(end >= 0, "What begins " + opens + " is never closed.");

        return body[start..end];
    }

    /// <summary>
    /// The queue page as the built assembly carries it, which is the copy a server serves.
    /// </summary>
    /// <returns>The page, inline script and all.</returns>
    private static string Queue()
    {
        var assembly = typeof(PluginUnderTest).Assembly;

        var resource = assembly
            .GetManifestResourceNames()
            .Single(name => name.EndsWith("Web.queue.html", StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The assembly carries no resource named {resource}.");

        using var reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd();
    }
}
