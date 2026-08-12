using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.Requests.Model;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Model;

/// <summary>
/// What the request record must keep true. Two of these are about the shape of the record rather
/// than about a value, because the failures they stand for are additions somebody makes later
/// without reading why the record looks the way it does.
/// </summary>
public class MediaRequestTests
{
    /// <summary>
    /// The names every public field on the record carries. Written out rather than derived, so an
    /// addition is a deliberate edit to this list and its reason is in the commit that made it. A
    /// field that arrives without one reds this test, which is the only reason the list is here.
    /// </summary>
    private static readonly string[] ExpectedFields =
    [
        "Availability",
        "AvailabilityCheckedAt",
        "DeclineNote",
        "DeclineReason",
        "DisplayTitle",
        "DisplayYear",
        "History",
        "Id",
        "JoinedByUserIds",
        "Kind",
        "ProviderIds",
        "RequestedAt",
        "RequestedByUserId",
        "RequesterNote",
        "Seasons",
        "State",
        "StateChangedAt",
        "StateChangedByUserId",
        "WantIds"
    ];

    /// <summary>
    /// The vocabulary of the thing that fetches media. None of it belongs on this record: those
    /// settings are the external service's, and a copy of them here is the beginning of a second,
    /// worse one. The fragments are matched against field names, so <c>QualityProfileId</c> and
    /// <c>DefaultRootFolder</c> are both caught.
    /// </summary>
    private static readonly string[] FetchVocabulary =
    [
        "client",
        "download",
        "folder",
        "indexer",
        "path",
        "profile",
        "quality",
        "seed",
        "torrent",
        "tracker",
        "url",
        "usenet"
    ];

    /// <summary>
    /// The case the whole separation exists for. An operator approves something on Tuesday and the
    /// file arrives on Friday, and for three days the request is approved and the server does not
    /// have it. A model that expressed approval and availability as one value would have to call
    /// those three days something, and every candidate is a lie.
    /// </summary>
    [Fact]
    public void ARequestCanBeApprovedAndNotYetAvailable()
    {
        var request = ARequest() with
        {
            State = RequestState.Approved,
            Availability = LibraryAvailability.Absent,
            AvailabilityCheckedAt = new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero)
        };

        Assert.Equal(RequestState.Approved, request.State);
        Assert.Equal(LibraryAvailability.Absent, request.Availability);
    }

    /// <summary>
    /// The two fields do not read each other. This is the near miss the test above does not catch:
    /// a later change could make availability follow from the state, or the state follow from
    /// availability, and the single approved-and-absent value above would still hold while every
    /// other pairing quietly stopped being representable.
    /// </summary>
    [Fact]
    public void ApprovalAndAvailabilityMoveIndependently()
    {
        var request = ARequest();

        foreach (var state in Enum.GetValues<RequestState>())
        {
            foreach (var availability in Enum.GetValues<LibraryAvailability>())
            {
                var moved = request with { State = state, Availability = availability };

                Assert.Equal(state, moved.State);
                Assert.Equal(availability, moved.Availability);
            }
        }
    }

    /// <summary>
    /// A request nothing has decided and nothing has looked at says exactly that. The failure this
    /// stands for is a default of <c>Absent</c>, which reads as "the server does not have it" when
    /// what happened is that no check has run, and which would make a fresh queue look like a list
    /// of things the server is missing.
    /// </summary>
    [Fact]
    public void ANewRequestIsOpenAndItsAvailabilityHasNotBeenLookedAt()
    {
        var request = ARequest();

        Assert.Equal(RequestState.Open, request.State);
        Assert.Equal(LibraryAvailability.Unknown, request.Availability);
        Assert.Null(request.AvailabilityCheckedAt);
        Assert.Null(request.StateChangedByUserId);
        Assert.Empty(request.ProviderIds);
    }

    /// <summary>
    /// A want named twice on one request is refused. These are the key a repeat of the same want is
    /// recognised by, and a request that recorded one want as two facts is a record that cannot be
    /// counted or reasoned about afterwards.
    /// </summary>
    [Fact]
    public void AWantNamedTwiceOnOneRequestIsRefused()
    {
        var want = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => ARequest() with { WantIds = [want, want] });
    }

    /// <summary>
    /// A want identifier that names nothing is refused rather than stored. It would match nothing on
    /// the way back in, so keeping it would be a request claiming to have absorbed a want that no
    /// lookup can ever find.
    /// </summary>
    [Fact]
    public void AWantIdentifierThatNamesNothingIsRefused()
    {
        Assert.Throws<ArgumentException>(() => ARequest() with { WantIds = [Guid.Empty] });
    }

    /// <summary>
    /// The record holds what it says it holds and nothing else. This is the guard that makes the
    /// documented field list something other than a comment: a field added to the record without a
    /// line here fails, and so does one removed from the record while the list still names it.
    /// </summary>
    [Fact]
    public void TheFieldSetIsExactlyTheOneThisRecordDocuments()
    {
        Assert.Equal(ExpectedFields, PublicFieldNames());
    }

    /// <summary>
    /// No field describes how media is fetched. The exact list above would already catch such an
    /// addition, but it would catch it as "an unexpected field" and say nothing about why the field
    /// is unwelcome, and somebody repairing a red test reads the reason before the assertion. This
    /// one names the reason, and it also refuses the case where somebody adds the field to both
    /// places at once.
    /// </summary>
    [Fact]
    public void NoFieldDescribesHowMediaIsFetched()
    {
        var offending = PublicFieldNames()
            .Where(name => FetchVocabulary.Any(fragment =>
                name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.Empty(offending);
    }

    /// <summary>
    /// Every field is a plain value. This is what makes the display snapshot a snapshot: a record
    /// whose fields are numbers, strings, identifiers, times and enumerations has nothing to ask,
    /// so a title on it can only be the one that was written when the request was made. A field
    /// typed as a manager, a client or a factory would let a later reader resolve the title at read
    /// time, which is the behaviour this record exists to refuse.
    /// </summary>
    [Fact]
    public void EveryFieldIsAPlainValueSoTheRecordCannotFetchAnything()
    {
        var offending = typeof(MediaRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => !IsAPlainValue(property.PropertyType))
            .Select(property => string.Format(
                CultureInfo.InvariantCulture,
                "{0} is {1}",
                property.Name,
                property.PropertyType.Name))
            .ToArray();

        Assert.Empty(offending);
    }

    private static bool IsAPlainValue(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying.IsPrimitive || underlying.IsEnum)
        {
            return true;
        }

        if (underlying == typeof(string) || underlying == typeof(Guid) || underlying == typeof(DateTimeOffset))
        {
            return true;
        }

        // The four collections on the record, and only in the shapes they are declared in. A
        // read-only map of string to string carries no behaviour a caller could reach through; the
        // seasons and the people who joined are read-only lists of numbers and identifiers, which
        // are plain values by the lines above; and a read-only list of history entries carries only
        // entries whose own fields are checked by TheHistoryEntryIsAPlainValueToo below. None of
        // them gives a reader anything to resolve a title through, which is the property this test
        // is about.
        return underlying == typeof(IReadOnlyDictionary<string, string>)
            || underlying == typeof(IReadOnlyList<int>)
            || underlying == typeof(IReadOnlyList<Guid>)
            || underlying == typeof(IReadOnlyList<RequestHistoryEntry>);
    }

    /// <summary>
    /// The history entry is held to the same rule as the record that carries it. Without this, the
    /// widening above would have let a field of any type at all onto the record by way of an entry,
    /// which is the hole a reader of the widened line would not see.
    /// </summary>
    [Fact]
    public void TheHistoryEntryIsAPlainValueToo()
    {
        var offending = typeof(RequestHistoryEntry)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => !IsAPlainValue(property.PropertyType))
            .Select(property => string.Format(
                CultureInfo.InvariantCulture,
                "{0} is {1}",
                property.Name,
                property.PropertyType.Name))
            .ToArray();

        Assert.Empty(offending);
    }

    private static string[] PublicFieldNames()
        => typeof(MediaRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// A request with only the fields that have no default. Every test above starts here and moves
    /// what it is about, so a test says what it is testing and nothing else.
    /// </summary>
    /// <returns>A newly asked-for request.</returns>
    private static MediaRequest ARequest()
    {
        var asked = new DateTimeOffset(2026, 8, 6, 8, 0, 0, TimeSpan.Zero);

        return new MediaRequest
        {
            Id = new Guid("6f2a1c34-8f0a-4c0e-9a3d-2c9a5b7e1d40"),
            RequestedByUserId = new Guid("b31d0f9a-5c2e-4a71-8f6b-0d4c3e2a1b58"),
            RequestedAt = asked,
            StateChangedAt = asked,
            Kind = RequestedItemKind.Movie,
            DisplayTitle = "The Conversation"
        };
    }
}
