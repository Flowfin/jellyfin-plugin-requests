using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Requests.Model;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Model;

/// <summary>
/// The two pieces of free text a person writes on a request, and the reason a decline is required
/// to carry.
/// <para>
/// Both notes are typed by a person and read back on a page, so what is asserted here is what the
/// model does with text it did not write: it refuses what is too long instead of shortening it, it
/// holds nothing where there was nothing, and it changes not one character of what is left.
/// </para>
/// </summary>
public class RequestNotesTests
{
    /// <summary>
    /// A note carrying every character a renderer might mistake for markup. Kept as one value so
    /// the tests below all use the same payload, and so somebody widening it widens every one of
    /// them at once.
    /// </summary>
    private const string LooksLikeMarkup = "<script>alert('hi')</script> & \"quoted\" 'apostrophed' <b>bold</b>";

    /// <summary>
    /// The operator making every decision below. One value, so a test says what it is about and not
    /// who was holding the mouse.
    /// </summary>
    private static readonly Guid AnOperator = new("c5b1a739-4e82-4d16-9f70-3a2b6c8d4e51");

    /// <summary>
    /// That operator as a caller. Every decision below is a decision, so every one of them is made
    /// by an administrator; who may make which move is <c>RequestAuthorityTests</c>.
    /// </summary>
    private static readonly RequestCaller ByTheOperator = RequestCaller.Administrator(AnOperator);

    /// <summary>
    /// A fresh request has neither note and no reason. The failure this stands for is a default of
    /// empty string, which reads on a page as a note somebody wrote and left blank.
    /// </summary>
    [Fact]
    public void ANewRequestCarriesNoNotesAndNoReason()
    {
        var request = ARequest();

        Assert.Null(request.RequesterNote);
        Assert.Null(request.DeclineReason);
        Assert.Null(request.DeclineNote);
    }

    /// <summary>
    /// A decline carries the reason it was given for, and the note beside it.
    /// </summary>
    [Fact]
    public void ADeclineCarriesItsReasonAndWhateverTheOperatorWroteBesideIt()
    {
        var declined = RequestLifecycle.Decline(
            ARequest(),
            DeclineReason.NoRoomForIt,
            "The disk is at ninety-five percent until the archive move finishes.",
            At(14),
            ByTheOperator);

        Assert.Equal(RequestState.Declined, declined.State);
        Assert.Equal(DeclineReason.NoRoomForIt, declined.DeclineReason);
        Assert.Equal(
            "The disk is at ninety-five percent until the archive move finishes.",
            declined.DeclineNote,
            StringComparer.Ordinal);
        Assert.Equal(AnOperator, declined.StateChangedByUserId);
    }

    /// <summary>
    /// A decline with no reason cannot be expressed through the place moves are made. This is what
    /// makes the requirement a rule rather than a sentence in an issue: the door into declined takes
    /// a reason, and the general one refuses that destination and says where to go instead.
    /// </summary>
    [Fact]
    public void ADeclineWithNoReasonCannotBeMadeThroughMove()
    {
        var refusal = Assert.Throws<ArgumentException>(
            () => RequestLifecycle.Move(ARequest(), RequestState.Declined, At(14), ByTheOperator));

        Assert.Equal("to", refusal.ParamName, StringComparer.Ordinal);
        Assert.Contains("Decline", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The escape hatch cannot be used to give no reason. A reason of Other says only that the list
    /// did not cover it, so the text beside it is the whole reason and is required.
    /// </summary>
    /// <param name="note">What the operator wrote, or did not.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AReasonOffTheListNeedsTheTextThatSaysWhy(string? note)
    {
        var refusal = Assert.Throws<ArgumentException>(
            () => RequestLifecycle.Decline(ARequest(), DeclineReason.Other, note, At(14), ByTheOperator));

        Assert.Equal("note", refusal.ParamName, StringComparer.Ordinal);
    }

    /// <summary>
    /// The same reason with the text is accepted. Without this the test above would pass equally
    /// well against a model that refused Other outright, which is a different rule.
    /// </summary>
    [Fact]
    public void AReasonOffTheListIsAcceptedWithTheTextThatSaysWhy()
    {
        var declined = RequestLifecycle.Decline(
            ARequest(),
            DeclineReason.Other,
            "The rights holder asked us to take it down.",
            At(14),
            ByTheOperator);

        Assert.Equal(DeclineReason.Other, declined.DeclineReason);
        Assert.Equal(
            "The rights holder asked us to take it down.",
            declined.DeclineNote,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A reason describes the decline it was given for, and goes when the decline goes. A request
    /// that has been approved carrying "there is no room for it" is a sentence that contradicts the
    /// state next to it, and an operator reading the queue has no way to tell which one is current.
    /// </summary>
    [Fact]
    public void TakingBackADeclineTakesTheReasonWithIt()
    {
        var declined = RequestLifecycle.Decline(
            ARequest(),
            DeclineReason.NoRoomForIt,
            "The disk is full.",
            At(14),
            ByTheOperator);

        var approved = RequestLifecycle.Move(declined, RequestState.Approved, At(15), ByTheOperator);

        Assert.Equal(RequestState.Approved, approved.State);
        Assert.Null(approved.DeclineReason);
        Assert.Null(approved.DeclineNote);
    }

    /// <summary>
    /// The requester's note survives a move untouched. It describes the asking rather than the
    /// deciding, so nothing about a decision should reach it.
    /// </summary>
    [Fact]
    public void TheRequesterNoteSurvivesEveryMove()
    {
        var request = ARequest() with { RequesterNote = "The 1974 one, not the remake." };

        var declined = RequestLifecycle.Decline(request, DeclineReason.CannotBeObtained, null, At(14), ByTheOperator);
        var approved = RequestLifecycle.Move(declined, RequestState.Approved, At(15), ByTheOperator);

        Assert.Equal("The 1974 one, not the remake.", approved.RequesterNote, StringComparer.Ordinal);
    }

    /// <summary>
    /// Text exactly as long as the cap is accepted. The near miss this is here for is the
    /// off-by-one: a check written with the wrong comparison refuses the last legal length, and
    /// nothing else in the suite would notice.
    /// </summary>
    [Fact]
    public void TextExactlyAsLongAsTheCapIsAccepted()
    {
        var atTheCap = new string('x', MediaRequest.NoteMaximumLength);

        var request = ARequest() with { RequesterNote = atTheCap, DeclineNote = atTheCap };

        Assert.Equal(MediaRequest.NoteMaximumLength, request.RequesterNote?.Length);
        Assert.Equal(MediaRequest.NoteMaximumLength, request.DeclineNote?.Length);
    }

    /// <summary>
    /// One character over is refused, and the refusal says which field, how long it may be and how
    /// long it was. Refused rather than shortened: the person who wrote it is not told about a
    /// truncation, the sentence that mattered is usually the last one, and what would be stored is
    /// something nobody wrote.
    /// </summary>
    [Fact]
    public void TextOneCharacterOverTheCapIsRefusedRatherThanShortened()
    {
        var tooLong = new string('x', MediaRequest.NoteMaximumLength + 1);

        var onTheRequesterNote = Assert.Throws<RequestTextTooLongException>(
            () => ARequest() with { RequesterNote = tooLong });

        Assert.Equal("RequesterNote", onTheRequesterNote.Field, StringComparer.Ordinal);
        Assert.Equal(MediaRequest.NoteMaximumLength, onTheRequesterNote.MaximumLength);
        Assert.Equal(MediaRequest.NoteMaximumLength + 1, onTheRequesterNote.ActualLength);

        var onTheDeclineNote = Assert.Throws<RequestTextTooLongException>(
            () => ARequest() with { DeclineNote = tooLong });

        Assert.Equal("DeclineNote", onTheDeclineNote.Field, StringComparer.Ordinal);
    }

    /// <summary>
    /// A decline whose note is too long does not half happen. The refusal comes from the record, so
    /// the request the caller handed in is the request they still have, and nothing was declined
    /// with the reason and then failed to keep the text.
    /// </summary>
    [Fact]
    public void ADeclineWithTooLongANoteLeavesTheRequestWhereItWas()
    {
        var request = ARequest();

        Assert.Throws<RequestTextTooLongException>(
            () => RequestLifecycle.Decline(
                request,
                DeclineReason.Other,
                new string('x', MediaRequest.NoteMaximumLength + 1),
                At(14),
                ByTheOperator));

        Assert.Equal(RequestState.Open, request.State);
        Assert.Null(request.DeclineReason);
    }

    /// <summary>
    /// Nothing and whitespace are the same as no note. Two representations of one fact mean every
    /// reader has to test for both, and one of them eventually will not, which is how an empty box
    /// with a heading above it ends up on a page.
    /// </summary>
    /// <param name="text">The text as it arrives.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void TextWithNothingInItIsHeldAsNoNote(string? text)
    {
        var request = ARequest() with { RequesterNote = text, DeclineNote = text };

        Assert.Null(request.RequesterNote);
        Assert.Null(request.DeclineNote);
    }

    /// <summary>
    /// The model changes not one character of what a person typed, markup included. Two failures
    /// are refused at once here. Escaping at the model would double-escape at the surface that
    /// escapes properly, so a note would read with visible ampersands in it. Stripping at the model
    /// would silently alter what somebody wrote and would leave the surface believing its input is
    /// safe, which is the belief that puts unescaped text on a page.
    /// <para>
    /// What this does not do is refuse a surface rendering the text as markup. That needs a surface,
    /// and there is none in the tree; #41 carries the condition and the surfaces are #61, #66 and
    /// #69. Storing the payload unchanged is what makes such a test possible to write there.
    /// </para>
    /// </summary>
    [Fact]
    public void ANoteIsHeldAsTextAndNeverTouched()
    {
        var declined = RequestLifecycle.Decline(
            ARequest() with { RequesterNote = LooksLikeMarkup },
            DeclineReason.Other,
            LooksLikeMarkup,
            At(14),
            ByTheOperator);

        Assert.Equal(LooksLikeMarkup, declined.RequesterNote, StringComparer.Ordinal);
        Assert.Equal(LooksLikeMarkup, declined.DeclineNote, StringComparer.Ordinal);
    }

    /// <summary>
    /// Both notes are plain strings and nothing on the record is typed as markup. A property typed
    /// as anything that claims to be already-safe markup is how a surface is told it may skip
    /// escaping, and the record has no way to make that claim true.
    /// </summary>
    [Fact]
    public void TheNotesAreStringsRatherThanAnythingClaimingToBeMarkup()
    {
        Assert.Equal(typeof(string), typeof(MediaRequest).GetProperty(nameof(MediaRequest.RequesterNote))?.PropertyType);
        Assert.Equal(typeof(string), typeof(MediaRequest).GetProperty(nameof(MediaRequest.DeclineNote))?.PropertyType);
    }

    private static DateTimeOffset At(int hour) => new(2026, 8, 9, hour, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A request with only the fields that have no default, in the state a new one is created in.
    /// </summary>
    /// <returns>A newly asked-for request.</returns>
    private static MediaRequest ARequest()
    {
        var asked = At(13);

        return new MediaRequest
        {
            Id = new Guid("9c3e6a41-2b7d-4f05-8e19-6d4a7b2c5f83"),
            RequestedByUserId = new Guid("18f5d92c-7a34-4b60-8d2e-5c9f1a3b7e06"),
            RequestedAt = asked,
            StateChangedAt = asked,
            Kind = RequestedItemKind.Movie,
            DisplayTitle = "The Conversation",
            ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = "592" }
        };
    }
}
