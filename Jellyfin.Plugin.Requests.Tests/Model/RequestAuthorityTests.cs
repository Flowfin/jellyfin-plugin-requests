using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.Requests.Model;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Model;

/// <summary>
/// Who may make each move, and what happens to everybody else. Legality and authority are two
/// questions about one move: approving a request that is open is a legal transition, and an ordinary
/// user approving their own is the thing this is here to refuse.
/// <para>
/// Everything below calls the model directly, with no endpoint and no page in between. That is the
/// second condition on #40: a check that only exists at a calling surface is one the next calling
/// surface will not have.
/// </para>
/// </summary>
public class RequestAuthorityTests
{
    private static readonly Guid Requester = new("7a4e2f18-6b0c-4d39-95e7-1f8a3c6d2b40");

    private static readonly Guid Operator = new("2d8f4b16-3a5c-4d97-8e21-7c6b5a4f3e29");

    private static readonly Guid Stranger = new("9c3b7e05-1d42-4a86-b0f9-5e2c8a7d6134");

    private static readonly DateTimeOffset MovedAt = new(2026, 8, 9, 14, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Every cell says who may make it, asserted against a list written here rather than read back
    /// from the table. A derived expectation would agree with whatever the table said, including on
    /// the day a cell is widened by accident, and a permission widened by accident is the failure
    /// this whole issue is about.
    /// </summary>
    /// <param name="from">The state being moved out of.</param>
    /// <param name="to">The state being moved into.</param>
    /// <param name="expected">The callers that cell admits.</param>
    [Theory]

    // A decision is an administrator's. An observation is the plugin's. A refused cell admits
    // nobody, which is what makes an illegal move and a move nobody may make one row rather than
    // two rules that can drift apart.
    [InlineData(RequestState.Open, RequestState.Open, RequestActor.None)]
    [InlineData(RequestState.Open, RequestState.Approved, RequestActor.Administrator)]
    [InlineData(RequestState.Open, RequestState.Declined, RequestActor.Administrator)]
    [InlineData(RequestState.Open, RequestState.Fulfilled, RequestActor.Plugin)]
    [InlineData(RequestState.Open, RequestState.Failed, RequestActor.None)]

    [InlineData(RequestState.Approved, RequestState.Open, RequestActor.None)]
    [InlineData(RequestState.Approved, RequestState.Approved, RequestActor.None)]
    [InlineData(RequestState.Approved, RequestState.Declined, RequestActor.Administrator)]
    [InlineData(RequestState.Approved, RequestState.Fulfilled, RequestActor.Plugin)]
    [InlineData(RequestState.Approved, RequestState.Failed, RequestActor.Plugin)]

    [InlineData(RequestState.Declined, RequestState.Open, RequestActor.None)]
    [InlineData(RequestState.Declined, RequestState.Approved, RequestActor.Administrator)]
    [InlineData(RequestState.Declined, RequestState.Declined, RequestActor.None)]
    [InlineData(RequestState.Declined, RequestState.Fulfilled, RequestActor.None)]
    [InlineData(RequestState.Declined, RequestState.Failed, RequestActor.None)]

    [InlineData(RequestState.Fulfilled, RequestState.Open, RequestActor.None)]
    [InlineData(RequestState.Fulfilled, RequestState.Approved, RequestActor.None)]
    [InlineData(RequestState.Fulfilled, RequestState.Declined, RequestActor.None)]
    [InlineData(RequestState.Fulfilled, RequestState.Fulfilled, RequestActor.None)]
    [InlineData(RequestState.Fulfilled, RequestState.Failed, RequestActor.None)]

    [InlineData(RequestState.Failed, RequestState.Open, RequestActor.None)]
    [InlineData(RequestState.Failed, RequestState.Approved, RequestActor.Administrator)]
    [InlineData(RequestState.Failed, RequestState.Declined, RequestActor.Administrator)]
    [InlineData(RequestState.Failed, RequestState.Fulfilled, RequestActor.Plugin)]
    [InlineData(RequestState.Failed, RequestState.Failed, RequestActor.None)]
    public void EveryCellAdmitsWhatThisListSays(RequestState from, RequestState to, RequestActor expected)
    {
        Assert.Equal(expected, RequestLifecycle.Cell(from, to).Permitted);
    }

    /// <summary>
    /// A refused cell admits nobody and a legal cell admits somebody. Either half failing is a cell
    /// that says two things at once: a move nobody may make is a refusal written twice, and a legal
    /// move nobody may make is a row that can never be exercised and that a reader will take for a
    /// working one.
    /// </summary>
    [Fact]
    public void ARefusedCellAdmitsNobodyAndALegalCellAdmitsSomebody()
    {
        var disagreeing = RequestLifecycle.Table
            .Where(cell => cell.IsLegal != (cell.Permitted != RequestActor.None))
            .Select(cell => string.Format(CultureInfo.InvariantCulture, "{0} to {1}", cell.From, cell.To))
            .ToArray();

        Assert.Empty(disagreeing);
    }

    /// <summary>
    /// What a caller turns out to be depends on the request, and the five cases are the whole rule.
    /// The one worth naming is the fourth: an ordinary user holds nothing at all on somebody else's
    /// request, so a surface that hands the wrong request to the wrong session is refused here
    /// rather than at whatever was supposed to have filtered it.
    /// </summary>
    [Fact]
    public void ACallerIsWhatTheRequestMakesThem()
    {
        var request = ARequest();

        Assert.Equal(RequestActor.Plugin, RequestCaller.Plugin.RolesOn(request));
        Assert.Equal(RequestActor.Administrator, RequestCaller.Administrator(Operator).RolesOn(request));
        Assert.Equal(RequestActor.Requester, RequestCaller.User(Requester).RolesOn(request));
        Assert.Equal(RequestActor.None, RequestCaller.User(Stranger).RolesOn(request));
        Assert.Equal(
            RequestActor.Administrator | RequestActor.Requester,
            RequestCaller.Administrator(Requester).RolesOn(request));
    }

    /// <summary>
    /// The sentence the issue leads with. An ordinary user asking for something may not then approve
    /// it, and the move they are attempting is one the table allows, so nothing about the states
    /// refuses it and only the caller does.
    /// </summary>
    [Fact]
    public void AUserCannotApproveTheirOwnRequest()
    {
        var request = ARequest();

        Assert.True(RequestLifecycle.IsLegal(RequestState.Open, RequestState.Approved));

        var refusal = Assert.Throws<RequestMoveNotPermittedException>(
            () => RequestLifecycle.Move(request, RequestState.Approved, MovedAt, RequestCaller.User(Requester)));

        Assert.Equal(RequestState.Open, refusal.From);
        Assert.Equal(RequestState.Approved, refusal.To);
        Assert.Equal(RequestActor.Administrator, refusal.Permitted);
    }

    /// <summary>
    /// An administrator who asked for something themselves may still decide it. They hold both roles
    /// on that one request, and the cell admits one of them. Whether a server should allow that is a
    /// configuration question left to M12, and it is answered by building the caller with
    /// <see cref="RequestCaller.User"/> for that call rather than by changing the table.
    /// </summary>
    [Fact]
    public void AnAdministratorMayDecideOnTheirOwnRequest()
    {
        var theirs = ARequest() with { RequestedByUserId = Operator };

        var approved = RequestLifecycle.Move(theirs, RequestState.Approved, MovedAt, RequestCaller.Administrator(Operator));

        Assert.Equal(RequestState.Approved, approved.State);
        Assert.Equal(Operator, approved.StateChangedByUserId);

        Assert.Throws<RequestMoveNotPermittedException>(
            () => RequestLifecycle.Move(theirs, RequestState.Approved, MovedAt, RequestCaller.User(Operator)));
    }

    /// <summary>
    /// Nobody but the plugin says a request is fulfilled, and nobody but an administrator decides
    /// one. This walks every legal cell against every kind of caller there is, so a permission that
    /// widened shows up here whichever cell it widened, rather than only in the cells somebody
    /// thought to write a test for.
    /// </summary>
    [Fact]
    public void EveryLegalCellIsMadeByTheCallersItAdmitsAndByNoOthers()
    {
        var wrong = new List<string>();

        foreach (var cell in RequestLifecycle.Table.Where(entry => entry.IsLegal))
        {
            var request = ARequest() with { State = cell.From };

            foreach (var (caller, holds) in EveryKindOfCaller())
            {
                var admitted = (cell.Permitted & holds) != RequestActor.None;
                var moved = TryMove(request, cell.To, caller);

                if (moved != admitted)
                {
                    wrong.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} to {1} by {2}: {3}",
                        cell.From,
                        cell.To,
                        holds,
                        moved ? "allowed and should not be" : "refused and should not be"));
                }
            }
        }

        Assert.Empty(wrong);
    }

    /// <summary>
    /// A move the table refuses is refused for being refused, whoever asks. The distinction is worth
    /// a test because the two exceptions are read differently: telling an operator that a fulfilled
    /// request "may not be approved by you" would send them looking for a permission to grant, and
    /// there is none, because that move is refused to everybody.
    /// </summary>
    [Fact]
    public void ARefusedMoveIsRefusedBeforeAnybodysAuthorityIsLookedAt()
    {
        var fulfilled = ARequest() with { State = RequestState.Fulfilled };

        Assert.Throws<IllegalRequestTransitionException>(
            () => RequestLifecycle.Move(fulfilled, RequestState.Approved, MovedAt, RequestCaller.User(Stranger)));
    }

    /// <summary>
    /// The refusal says enough to act on and nothing about the request it was thrown for. This is
    /// the third condition on #40: a refusal is the one message a caller can make the plugin produce
    /// about a request they were not allowed to touch, so the title, the identifiers and the text a
    /// person typed must not be in it.
    /// </summary>
    [Fact]
    public void TheRefusalNamesTheMoveAndNothingAboutTheRequest()
    {
        var request = ARequest() with { RequesterNote = "The one Clouzot made, not the remake." };

        var refusal = Assert.Throws<RequestMoveNotPermittedException>(
            () => RequestLifecycle.Move(request, RequestState.Approved, MovedAt, RequestCaller.User(Stranger)));

        // Enough to act on: which move, and who makes it.
        Assert.Contains("Open", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Approved", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(RequestActor.Administrator), refusal.Message, StringComparison.Ordinal);

        // And nothing that would tell the caller about a request they may not see. The requester's
        // identifier is checked as well as the title, because an identifier handed back is how a
        // caller learns whose request it is without ever being shown a name.
        foreach (var secret in new[]
        {
            request.DisplayTitle,
            request.RequesterNote!,
            request.Id.ToString(),
            request.RequestedByUserId.ToString()
        })
        {
            Assert.DoesNotContain(secret, refusal.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// A caller who is refused leaves nothing behind. A refusal that had already appended to the
    /// history would put a move in the record that nobody was allowed to make, which is worse than
    /// the move itself: the record is what an operator answers a complaint from.
    /// </summary>
    [Fact]
    public void ARefusedCallerChangesNothing()
    {
        var request = ARequest();

        Assert.Throws<RequestMoveNotPermittedException>(
            () => RequestLifecycle.Move(request, RequestState.Approved, MovedAt, RequestCaller.User(Requester)));

        Assert.Equal(RequestState.Open, request.State);
        Assert.Empty(request.History);
    }

    /// <summary>
    /// Every kind of caller there is, with what each of them holds on the request below. A fifth
    /// kind would be a fourth value in <see cref="RequestActor"/>, which is a change to the
    /// enumeration and to this list together.
    /// </summary>
    /// <returns>Each caller and the roles it holds on a request asked for by <c>Requester</c>.</returns>
    private static IEnumerable<(RequestCaller Caller, RequestActor Holds)> EveryKindOfCaller()
    {
        yield return (RequestCaller.Plugin, RequestActor.Plugin);
        yield return (RequestCaller.Administrator(Operator), RequestActor.Administrator);
        yield return (RequestCaller.User(Requester), RequestActor.Requester);
        yield return (RequestCaller.User(Stranger), RequestActor.None);
    }

    /// <summary>
    /// Makes a move through whichever of the two doors takes it, and says whether it was made. Only
    /// the authority refusal is turned into <see langword="false"/>; anything else is a defect in
    /// the walk rather than an answer, and it is left to throw.
    /// </summary>
    /// <param name="request">The request to move.</param>
    /// <param name="to">The state to move it into.</param>
    /// <param name="by">The caller making the move.</param>
    /// <returns><see langword="true"/> where the move was made.</returns>
    private static bool TryMove(MediaRequest request, RequestState to, RequestCaller by)
    {
        try
        {
            _ = to == RequestState.Declined
                ? RequestLifecycle.Decline(request, DeclineReason.NotWanted, note: null, MovedAt, by)
                : RequestLifecycle.Move(request, to, MovedAt, by);

            return true;
        }
        catch (RequestMoveNotPermittedException)
        {
            return false;
        }
    }

    /// <summary>
    /// A request asked for by <c>Requester</c>, in the state a new one is created in.
    /// </summary>
    /// <returns>A newly asked-for request.</returns>
    private static MediaRequest ARequest()
    {
        var asked = new DateTimeOffset(2026, 8, 9, 10, 0, 0, TimeSpan.Zero);

        return new MediaRequest
        {
            Id = new Guid("41d7c0b2-9e3a-4f58-b6d1-8c2f5a0e7b93"),
            RequestedByUserId = Requester,
            RequestedAt = asked,
            StateChangedAt = asked,
            Kind = RequestedItemKind.Movie,
            DisplayTitle = "Wages of Fear"
        };
    }
}
