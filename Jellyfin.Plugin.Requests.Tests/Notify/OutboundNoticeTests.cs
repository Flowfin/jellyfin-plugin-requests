using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Notify;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Notify;

/// <summary>
/// The document that leaves the server, held to the shape somebody else's machine reads.
/// <para>
/// The expected field list below is written out by hand rather than read off the type. A list
/// derived from the type agrees with the type on the day a field is added to it, which is the only
/// day this page is worth having: the reader of this document is a script an operator wrote and this
/// repository will never see, and it breaks silently at their end rather than loudly at this one.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class OutboundNoticeTests
{
    private static readonly Guid Asker = new Guid("7e000000-0000-0000-0000-000000000001");
    private static readonly Guid Operator = new Guid("7e000000-0000-0000-0000-000000000002");
    private static readonly Guid Joiner = new Guid("7e000000-0000-0000-0000-000000000003");
    private static readonly DateTimeOffset Asked = new DateTimeOffset(2026, 8, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Answered = new DateTimeOffset(2026, 8, 14, 17, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// Every field the document carries, as a reader of it would write them down.
    /// </summary>
    /// <returns>The whole document, in the order it is declared.</returns>
    public static IReadOnlyList<string> TheFieldsAReaderGets()
        =>
        [
            "version",
            "event",
            "requestId",
            "at",
            "state",
            "requestedByUserId",
            "movedByUserId",
            "kind",
            "title",
            "year"
        ];

    /// <summary>
    /// The document is exactly those fields. Both halves matter: a field that disappeared breaks a
    /// reader that used it, and a field that appeared is something that now leaves the server
    /// without anybody having argued for it.
    /// </summary>
    [Fact]
    public void TheDocumentIsExactlyTheFieldsWrittenDown()
    {
        Assert.Equal(TheFieldsAReaderGets(), FieldsOf(Declined()));
    }

    /// <summary>
    /// Every document says which shape it is, including the ones where nothing optional was set. A
    /// version that only appears sometimes is a version a reader cannot branch on.
    /// </summary>
    [Fact]
    public void EveryDocumentCarriesTheVersionThisPluginWrites()
    {
        foreach (var notice in new[] { Asked_(), Declined(), Fulfilled() })
        {
            using var read = JsonDocument.Parse(JsonSerializer.Serialize(notice));

            Assert.Equal(OutboundNotice.CurrentVersion, read.RootElement.GetProperty("version").GetInt32());
        }
    }

    /// <summary>
    /// What happened and what state it is in are words rather than numbers.
    /// <para>
    /// A number is what the enumeration happens to be ordered as today, and a value inserted in the
    /// middle of either one would silently change what every past document meant. A reader comparing
    /// against a word is unharmed by the same change.
    /// </para>
    /// </summary>
    [Fact]
    public void WhatHappenedAndWhatStateItIsInAreWordsRatherThanNumbers()
    {
        using var read = JsonDocument.Parse(JsonSerializer.Serialize(Declined()));

        Assert.Equal("Declined", read.RootElement.GetProperty("event").GetString());
        Assert.Equal("Declined", read.RootElement.GetProperty("state").GetString());
        Assert.Equal("Movie", read.RootElement.GetProperty("kind").GetString());
    }

    /// <summary>
    /// Neither note leaves the server, and neither do the provider identifiers or the people who
    /// joined.
    /// <para>
    /// The request this is built from carries all four, so the assertion is about what the document
    /// leaves behind rather than about a record that never had them. Both notes are free text
    /// somebody typed, and either can hold a name, an address or anything else that this plugin has
    /// no business posting to an address on somebody's behalf.
    /// </para>
    /// </summary>
    [Fact]
    public void NoNoteNoProviderIdentifierAndNobodyElseWaitingLeavesTheServer()
    {
        var written = JsonSerializer.Serialize(Declined());

        Assert.DoesNotContain("please get this one", written, StringComparison.Ordinal);
        Assert.DoesNotContain("we already have it in another cut", written, StringComparison.Ordinal);
        Assert.DoesNotContain("tt0111161", written, StringComparison.Ordinal);
        Assert.DoesNotContain(Joiner.ToString(), written, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A request that has just arrived names nobody as having moved it, and it is stamped with when
    /// it was asked for rather than with a state change that has not happened.
    /// </summary>
    [Fact]
    public void ArrivingNamesNobodyAsHavingMovedItAndIsStampedWithWhenItWasAsked()
    {
        var notice = Asked_();

        Assert.Null(notice.MovedByUserId);
        Assert.Equal(Asked, notice.At);
    }

    /// <summary>
    /// A movement names whoever made it and is stamped with when the request moved, which is what a
    /// reader delivering a late message needs so it does not read as having just happened.
    /// </summary>
    [Fact]
    public void AMovementNamesWhoMadeItAndWhenTheRequestMoved()
    {
        var notice = Declined();

        Assert.Equal(Operator, notice.MovedByUserId);
        Assert.Equal(Answered, notice.At);
    }

    /// <summary>
    /// Fulfilment is decided by the library rather than by anybody, so the field for a mover is
    /// absent rather than filled with whoever last touched the request.
    /// </summary>
    [Fact]
    public void FulfilmentNamesNobodyBecauseNobodyDidIt()
    {
        Assert.Null(Fulfilled().MovedByUserId);
    }

    private static OutboundNotice Asked_() => OutboundNotice.For(Requested(), NoticeEvent.Asked);

    private static OutboundNotice Declined()
        => OutboundNotice.For(
            Requested() with
            {
                State = RequestState.Declined,
                StateChangedAt = Answered,
                StateChangedByUserId = Operator,
                DeclineReason = DeclineReason.AlreadyInTheLibrary,
                DeclineNote = "we already have it in another cut"
            },
            NoticeEvent.Declined);

    private static OutboundNotice Fulfilled()
        => OutboundNotice.For(
            Requested() with
            {
                State = RequestState.Fulfilled,
                StateChangedAt = Answered,
                StateChangedByUserId = null
            },
            NoticeEvent.Fulfilled);

    private static MediaRequest Requested()
        => new MediaRequest
        {
            Id = new Guid("7e000000-0000-0000-0000-0000000000aa"),
            RequestedByUserId = Asker,
            RequestedAt = Asked,
            Kind = RequestedItemKind.Movie,
            DisplayTitle = "The Shawshank Redemption",
            DisplayYear = 1994,
            ProviderIds = new Dictionary<string, string> { ["Imdb"] = "tt0111161" },
            JoinedByUserIds = [Joiner],
            RequesterNote = "please get this one",
            StateChangedAt = Asked
        };

    private static IReadOnlyList<string> FieldsOf(OutboundNotice notice)
    {
        using var read = JsonDocument.Parse(JsonSerializer.Serialize(notice));

        return [.. read.RootElement.EnumerateObject().Select(field => field.Name)];
    }
}
