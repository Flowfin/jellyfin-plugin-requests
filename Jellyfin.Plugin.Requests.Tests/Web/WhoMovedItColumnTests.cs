using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Jellyfin.Plugin.Requests.Api;
using Jellyfin.Plugin.Requests.Localisation;
using Xunit;

using PluginUnderTest = global::Jellyfin.Plugin.Requests.Plugin;

namespace Jellyfin.Plugin.Requests.Tests.Web;

/// <summary>
/// The column the operator's queue draws about who last moved a request, and the rule that a
/// deleted account is concluded from a user list this page was given rather than from one it never
/// received.
/// <para>
/// The failure this exists against is a sentence about a person manufactured out of a network
/// error. Both readings leave the same thing behind - an identifier the page cannot find a name
/// for - and only one of them means the account is gone. A page that collapses them tells an
/// operator that an administrator was deleted every time a call does not answer, and nothing about
/// that looks like a fault: the queue draws, the row is filled, and the sentence is wrong.
/// </para>
/// <para>
/// The bound is the one every check over these assets carries and it is stated rather than left to
/// be found: this reads the page as the assembly ships it and runs none of it. Nothing here proves
/// a cell appeared on a screen. What it proves is that the page names four different answers, that
/// what it reads them from is a field the queue answer carries, and that the one answer which is a
/// claim about a person is behind the flag saying the list arrived.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class WhoMovedItColumnTests
{
    /// <summary>
    /// The two answers that are words rather than something read off the request. The other two are
    /// a name out of the user list and the identifier itself, neither of which is a catalogue key.
    /// </summary>
    private static readonly string[] Words =
    [
        "queue.movedBy.nobody",
        "queue.movedBy.deleted"
    ];

    /// <summary>
    /// The column has a heading of its own, and the two sentences it can hold are two keys the
    /// shipped catalogue carries.
    /// <para>
    /// A request nobody has moved is said in words rather than left blank. An empty cell there is
    /// indistinguishable from a cell the page failed to fill, and it is the state every open
    /// request is in, so it is the cell an operator reads most often.
    /// </para>
    /// </summary>
    [Fact]
    public void TheAnswersThisColumnCanHoldAreWordsRatherThanBlanks()
    {
        var body = QueuePage();
        var shipped = StringCatalogue.Shipped.For(culture: null);

        Assert.Contains("queue.column.movedBy", body, StringComparison.Ordinal);
        Assert.True(shipped.ContainsKey("queue.column.movedBy"));

        foreach (var key in Words)
        {
            Assert.Contains(key, body, StringComparison.Ordinal);
            Assert.True(shipped.ContainsKey(key), key);
        }

        Assert.Equal(Words.Length, Words.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// What the page reads the mover from is a field the queue answer declares.
    /// <para>
    /// The near-miss is silent in the worst way. A page reading a name the endpoint does not answer
    /// gets <c>undefined</c>, which is falsy, so every row reads as one nobody has moved - on a
    /// server where an operator has decided hundreds of them. Nothing would be red and nothing
    /// would be logged.
    /// </para>
    /// </summary>
    [Fact]
    public void WhatThePageReadsTheMoverFromIsAFieldTheQueueAnswerCarries()
    {
        var body = QueuePage();

        Assert.Contains("request.StateChangedByUserId", body, StringComparison.Ordinal);

        var declared = typeof(QueuedRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();

        Assert.Contains("StateChangedByUserId", declared, StringComparer.Ordinal);
    }

    /// <summary>
    /// A deleted account is concluded only from a user list this page was given.
    /// <para>
    /// This is the leg the column exists for. Inside the function, the flag is consulted before the
    /// sentence about a deleted person is reached, so the answer on a list that never arrived is the
    /// identifier - which is what the requester cell shows in the same circumstance - rather than a
    /// claim that somebody's account is gone.
    /// </para>
    /// <para>
    /// The one-character mistake this is aimed at is the flag starting <c>true</c>, which turns
    /// every row on a server whose user list did not answer into a row about a deleted person and
    /// breaks nothing else.
    /// </para>
    /// </summary>
    [Fact]
    public void ADeletedAccountIsConcludedOnlyFromAUserListThisPageWasGiven()
    {
        var body = QueuePage();

        Assert.Contains("var peopleRead = false;", body, StringComparison.Ordinal);

        var answering = FunctionBody(body, "movedBy");

        var guarded = answering.IndexOf("if (!peopleRead)", StringComparison.Ordinal);
        var deleted = answering.IndexOf("queue.movedBy.deleted", StringComparison.Ordinal);

        Assert.True(guarded >= 0, "The answer about a deleted person is not behind the flag that says the user list arrived.");
        Assert.True(deleted >= 0, "The function saying who moved a request never says that the person is gone.");
        Assert.True(guarded < deleted, "The page decides the account is gone before it asks whether it was given a user list.");
    }

    /// <summary>
    /// A user list that could not be read is not recorded as one that was.
    /// <para>
    /// The flag and the list are written in the same two places, and the failure this catches is the
    /// one where only the list is. A catch that empties the list and leaves the flag alone is a page
    /// that read a list once, lost it, and goes on concluding deletions from what it no longer has.
    /// </para>
    /// </summary>
    [Fact]
    public void AUserListThatCouldNotBeReadIsNotRecordedAsOneThatWas()
    {
        var reading = FunctionBody(QueuePage(), "known");

        Assert.Contains("peopleRead = true;", reading, StringComparison.Ordinal);
        Assert.Contains("peopleRead = false;", reading, StringComparison.Ordinal);
        Assert.Contains("people = {};", reading, StringComparison.Ordinal);
        Assert.Contains("people[user.Id] = user.Name;", reading, StringComparison.Ordinal);

        // The flag is set after the list has been walked, so a list half copied when something threw
        // is not recorded as one this page was given.
        var copied = reading.IndexOf("people[user.Id] = user.Name;", StringComparison.Ordinal);
        var read = reading.IndexOf("peopleRead = true;", StringComparison.Ordinal);

        Assert.True(read > copied, "The list is recorded as read before it has been read.");
    }

    /// <summary>
    /// Every cell a row carries has a column heading above it.
    /// <para>
    /// This is the near-miss of adding a column, and the symptom is not the cell that was added. A
    /// row with one more cell than the header has slides every column after it one place, so the
    /// page reads as if the note were the decision and the decision were the handover, and every one
    /// of those cells is filled with something plausible.
    /// </para>
    /// <para>
    /// The decisions are counted as a column because the header names one for them and the row
    /// appends one cell of buttons for them.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryCellARowCarriesHasAColumnHeadingAboveIt()
    {
        var body = QueuePage();

        var headings = Between(body, "<thead>", "</thead>")
            .Split("<th ", StringSplitOptions.RemoveEmptyEntries)
            .Length - 1;

        var drawing = FunctionBody(body, "draw");

        var cells = drawing.Split("cell(row, ", StringSplitOptions.RemoveEmptyEntries).Length - 1;
        var decisions = drawing.Split("decide(row, ", StringSplitOptions.RemoveEmptyEntries).Length - 1;

        Assert.True(headings > 0, "The queue table names no columns.");
        Assert.Equal(1, decisions);
        Assert.Equal(headings, cells + decisions);
    }

    /// <summary>
    /// What lies between two markers in the page.
    /// </summary>
    /// <param name="body">The page as it is embedded.</param>
    /// <param name="opens">The marker the region begins at.</param>
    /// <param name="closes">The marker the region ends at.</param>
    /// <returns>The region, markers excluded.</returns>
    private static string Between(string body, string opens, string closes)
    {
        var at = body.IndexOf(opens, StringComparison.Ordinal);
        Assert.True(at >= 0, "The page carries nothing beginning " + opens + ".");

        var start = at + opens.Length;
        var end = body.IndexOf(closes, start, StringComparison.Ordinal);
        Assert.True(end >= 0, "What begins " + opens + " is never closed.");

        return body[start..end];
    }

    /// <summary>
    /// The body of one of the page's own functions, taken by counting braces rather than by looking
    /// for an indented closing one, so nothing here depends on how the file is wrapped or on which
    /// line ending it carries.
    /// </summary>
    /// <param name="body">The page as it is embedded.</param>
    /// <param name="name">The function's name.</param>
    /// <returns>The body, its outermost braces excluded.</returns>
    private static string FunctionBody(string body, string name)
    {
        var declared = "function " + name + "(";

        var at = body.IndexOf(declared, StringComparison.Ordinal);
        Assert.True(at >= 0, "The page declares no function called " + name + ".");

        var opens = body.IndexOf('{', at);
        Assert.True(opens >= 0, "The function " + name + " has no body.");

        var depth = 0;

        for (var reading = opens; reading < body.Length; reading++)
        {
            if (body[reading] == '{')
            {
                depth++;
            }
            else if (body[reading] == '}')
            {
                depth--;

                if (depth == 0)
                {
                    return body[(opens + 1)..reading];
                }
            }
        }

        Assert.Fail("The function " + name + " is never closed.");

        return string.Empty;
    }

    /// <summary>
    /// The queue page as the built assembly carries it.
    /// </summary>
    /// <returns>The page, inline script and all.</returns>
    private static string QueuePage()
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
