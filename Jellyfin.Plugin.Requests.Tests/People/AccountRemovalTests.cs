using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.People;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Tests.Doubles;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.People;

/// <summary>
/// What a deleted Jellyfin account leaves behind in this plugin, which is #49's first condition.
/// <para>
/// <b>The two rules are tested apart, because they are two rules.</b> A request the deleted person
/// asked for is theirs and goes; a request somebody else asked for that they had joined stays, with
/// them off its list. A test that only counted records would pass for a sweep that removed both, and
/// removing somebody else's request because a third party was on it is the worse of the two failures.
/// </para>
/// <para>
/// <b>One field is deliberately left standing and is asserted here for that reason.</b> A deleted
/// administrator's identifier on <see cref="MediaRequest.StateChangedByUserId"/> stays. Clearing it
/// would say something false rather than nothing, because an empty value there means no person moved
/// the request. That is the answer taken on #49 on 27 August, and it is written as a test so that a
/// later change removing the value has to argue with a red suite rather than with a document.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public sealed class AccountRemovalTests
{
    private static readonly Guid TheDeletedPerson = new Guid("11111111-1111-4111-8111-111111111111");
    private static readonly Guid SomebodyElse = new Guid("22222222-2222-4222-8222-222222222222");
    private static readonly Guid AnAdministrator = new Guid("33333333-3333-4333-8333-333333333333");

    private static readonly DateTimeOffset Asked = new DateTimeOffset(2026, 8, 27, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A request the deleted person asked for is gone.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ARequestTheDeletedPersonAskedForIsRemoved()
    {
        var store = new InMemoryRequestStore();
        await store.AddAsync(ARequest(1, TheDeletedPerson), CancellationToken.None).ConfigureAwait(true);

        var report = await Removal(store).RemoveAsync(TheDeletedPerson, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, report.Removed);
        Assert.Equal(0, report.Detached);
        Assert.Equal(0, report.Left);
        Assert.Empty(await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true));
    }

    /// <summary>
    /// A request somebody else asked for stays, and the deleted person comes off its list of joiners.
    /// <para>
    /// The request staying is half the assertion and the more important half: the person who asked
    /// for it did not delete their account, and taking their request away because a third party did
    /// is a worse answer to a narrower problem.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ARequestSomebodyElseAskedForStaysAndTheDeletedPersonComesOffIt()
    {
        var store = new InMemoryRequestStore();

        await store.AddAsync(
            ARequest(1, SomebodyElse) with { JoinedByUserIds = [TheDeletedPerson] },
            CancellationToken.None).ConfigureAwait(true);

        var report = await Removal(store).RemoveAsync(TheDeletedPerson, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(0, report.Removed);
        Assert.Equal(1, report.Detached);

        var held = Assert.Single(await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true));

        Assert.Equal(SomebodyElse, held.Request.RequestedByUserId);
        Assert.Empty(held.Request.JoinedByUserIds);
    }

    /// <summary>
    /// One other person on the same request keeps their place. A sweep that emptied the list rather
    /// than removing one entry would pass the test above.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task EverybodyElseOnThatListKeepsTheirPlace()
    {
        var store = new InMemoryRequestStore();

        await store.AddAsync(
            ARequest(1, SomebodyElse) with { JoinedByUserIds = [TheDeletedPerson, AnAdministrator] },
            CancellationToken.None).ConfigureAwait(true);

        await Removal(store).RemoveAsync(TheDeletedPerson, CancellationToken.None).ConfigureAwait(true);

        var held = Assert.Single(await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true));

        Assert.Equal([AnAdministrator], held.Request.JoinedByUserIds);
    }

    /// <summary>
    /// A request the deleted person is not on at all is untouched, including its revision. A sweep
    /// that rewrote every request would move a revision under somebody holding one and refuse their
    /// next write for a reason that has nothing to do with them.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ARequestTheyAreNotOnIsNotTouchedAtAll()
    {
        var store = new InMemoryRequestStore();
        var untouched = await store.AddAsync(ARequest(1, SomebodyElse), CancellationToken.None).ConfigureAwait(true);

        var report = await Removal(store).RemoveAsync(TheDeletedPerson, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(new AccountRemovalReport(0, 0, 0, 0), report);

        var held = Assert.Single(await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true));

        Assert.Equal(untouched.Revision, held.Revision);
    }

    /// <summary>
    /// Nothing in the queue names the deleted person afterwards, over a store holding every shape at
    /// once. This is the condition itself rather than one of the rules under it, and it is asserted
    /// over every field that can hold a person rather than over a count of records.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task NoRequestNamesThemAfterwards()
    {
        var store = new InMemoryRequestStore();

        await store.AddAsync(ARequest(1, TheDeletedPerson), CancellationToken.None).ConfigureAwait(true);
        await store.AddAsync(
            ARequest(2, SomebodyElse) with { JoinedByUserIds = [TheDeletedPerson] },
            CancellationToken.None).ConfigureAwait(true);
        await store.AddAsync(ARequest(3, SomebodyElse), CancellationToken.None).ConfigureAwait(true);

        // A finished one of theirs, which is the record that now SURVIVES the removal. Without it
        // this property would be asserted only over shapes that are removed or detached, which is
        // the half of the store the tombstone was added for.
        await store.AddAsync(
            ARequest(4, TheDeletedPerson) with { State = RequestState.Fulfilled },
            CancellationToken.None).ConfigureAwait(true);

        await Removal(store).RemoveAsync(TheDeletedPerson, CancellationToken.None).ConfigureAwait(true);

        var held = await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(3, held.Count);
        Assert.All(held, one => Assert.NotEqual(TheDeletedPerson, one.Request.RequestedByUserId));
        Assert.All(held, one => Assert.DoesNotContain(TheDeletedPerson, one.Request.JoinedByUserIds));
        Assert.All(held, one => Assert.NotEqual(TheDeletedPerson, one.Request.StateChangedByUserId));

        // The history is where an identifier used to be able to hide, and from the second on-disk
        // shape it holds a role instead. Asserted here as well, because this test is the one a reader
        // opens to find out what "no record names them" covers.
        Assert.All(
            held,
            one => Assert.All(one.Request.History, entry => Assert.True(Enum.IsDefined(entry.By))));
    }

    /// <summary>
    /// A deleted administrator's identifier on a request that stays is left exactly as it was.
    /// <para>
    /// This is the field the decision of 27 August keeps, and the reason is that an empty value there
    /// means nobody moved the request, so clearing it says something false. What an operator is shown
    /// instead of a name is #307 on the queue surface and is not this.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheIdentifierOfADeletedAdministratorOnARequestThatStaysIsLeftStanding()
    {
        var store = new InMemoryRequestStore();

        await store.AddAsync(
            ARequest(1, SomebodyElse) with
            {
                State = RequestState.Approved,
                StateChangedByUserId = TheDeletedPerson,
                History =
                [
                    new RequestHistoryEntry
                    {
                        From = RequestState.Open,
                        To = RequestState.Approved,
                        At = Asked,
                        By = RequestActor.Administrator
                    }
                ]
            },
            CancellationToken.None).ConfigureAwait(true);

        var report = await Removal(store).RemoveAsync(TheDeletedPerson, CancellationToken.None).ConfigureAwait(true);

        // Nothing was done to it: the person is not the requester and not a joiner, so the store's
        // own answer to "what names them" does not return it.
        Assert.Equal(new AccountRemovalReport(0, 0, 0, 0), report);

        var held = Assert.Single(await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true));

        Assert.Equal(TheDeletedPerson, held.Request.StateChangedByUserId);
        Assert.Equal(RequestActor.Administrator, Assert.Single(held.Request.History).By);
    }

    /// <summary>
    /// A removal that names nobody is refused rather than run. An empty identifier matches no request
    /// today and would match every one under any change to how the store answers, which is not a
    /// failure worth leaving available.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task ARemovalThatNamesNobodyIsRefused()
    {
        var store = new InMemoryRequestStore();

        await Assert.ThrowsAsync<ArgumentException>(
            () => Removal(store).RemoveAsync(Guid.Empty, CancellationToken.None)).ConfigureAwait(true);
    }

    /// <summary>
    /// A request that moves under the removal is re-read and removed against what the store now
    /// holds. A sweep that gave up on the first refusal would leave the record it exists to remove.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ARequestThatMovesUnderTheRemovalIsStillRemoved()
    {
        var inner = new InMemoryRequestStore();
        await inner.AddAsync(ARequest(1, TheDeletedPerson), CancellationToken.None).ConfigureAwait(true);

        var store = new StoreThatMovesARequestUnderTheRemoval(
            inner,
            moving => moving with { RequesterNote = "Somebody typed something while this ran." });

        var report = await Removal(store).RemoveAsync(TheDeletedPerson, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, report.Removed);
        Assert.Equal(0, report.Left);
        Assert.Empty(await inner.GetAllAsync(CancellationToken.None).ConfigureAwait(true));
    }

    /// <summary>
    /// A request that keeps moving for longer than the removal will try is left standing, counted as
    /// left, and said so in the log at a level an operator sees.
    /// <para>
    /// The three are one property. Nothing looks at such a record again on its own, because the
    /// account it names no longer exists for anything to start a search from, so a removal that gave
    /// up quietly would leave a person's records held with nobody able to find out.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ARequestThatNeverStopsMovingIsLeftAndTheOperatorIsTold()
    {
        var inner = new InMemoryRequestStore();

        await inner.AddAsync(
            ARequest(1, SomebodyElse) with { JoinedByUserIds = [TheDeletedPerson] },
            CancellationToken.None).ConfigureAwait(true);

        var store = new AStoreThatNeverSettles(inner);
        var log = new RecordingLogger();

        var report = await new AccountRemoval(store, new InMemoryNoticePreferences(), log).RemoveAsync(TheDeletedPerson, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, report.Left);
        Assert.Equal(0, report.Detached);
        Assert.Equal(AccountRemoval.Attempts, store.Refusals);

        Assert.Contains(
            log.At(LogLevel.Warning),
            line => line.Message.Contains("still names", StringComparison.Ordinal)
                || line.Message.Contains("naming a deleted account", StringComparison.Ordinal));
    }

    /// <summary>
    /// A removal that reached records says so at a level an operator reading the server's log sees.
    /// This plugin deletes somebody's records without anybody asking it to, and an operator answering
    /// for what is held has to be able to find that it happened.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task WhatWasRemovedIsWrittenWhereAnOperatorReads()
    {
        var store = new InMemoryRequestStore();
        await store.AddAsync(ARequest(1, TheDeletedPerson), CancellationToken.None).ConfigureAwait(true);

        var log = new RecordingLogger();
        await new AccountRemoval(store, new InMemoryNoticePreferences(), log).RemoveAsync(TheDeletedPerson, CancellationToken.None).ConfigureAwait(true);

        Assert.Contains(
            log.At(LogLevel.Information),
            line => line.Message.Contains("deleted account", StringComparison.Ordinal));
    }

    /// <summary>
    /// A removal over a store holding nothing about that person writes nothing and says nothing.
    /// Without this the log assertions above pass for a sweep that announces every deletion whether
    /// or not it touched anything, which is a log an operator learns to skip.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ARemovalThatTouchedNothingIsQuiet()
    {
        var store = new InMemoryRequestStore();
        await store.AddAsync(ARequest(1, SomebodyElse), CancellationToken.None).ConfigureAwait(true);

        var log = new RecordingLogger();
        await new AccountRemoval(store, new InMemoryNoticePreferences(), log).RemoveAsync(TheDeletedPerson, CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(log.At(LogLevel.Information));
        Assert.Empty(log.At(LogLevel.Warning));
    }

    /// <summary>
    /// The switch a deleted person set about being told goes with their records.
    /// <para>
    /// It is in the other file this plugin writes, it is the least revealing thing either file holds,
    /// and it is still an identifier for somebody who is gone. A sweep that reached only the queue
    /// would leave a preference nobody can ever change again.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheSwitchTheyTurnedOffGoesWithThem()
    {
        var store = new InMemoryRequestStore();
        var notices = new InMemoryNoticePreferences();

        await notices.SetAsync(TheDeletedPerson, tellsThem: false, CancellationToken.None).ConfigureAwait(true);
        await notices.SetAsync(SomebodyElse, tellsThem: false, CancellationToken.None).ConfigureAwait(true);

        await new AccountRemoval(store, notices, new RecordingLogger())
            .RemoveAsync(TheDeletedPerson, CancellationToken.None).ConfigureAwait(true);

        Assert.True(await notices.TellsThemAsync(TheDeletedPerson, CancellationToken.None).ConfigureAwait(true));

        // And nobody else is touched, which is the half a sweep that emptied the file would fail.
        Assert.False(await notices.TellsThemAsync(SomebodyElse, CancellationToken.None).ConfigureAwait(true));
    }

    /// <summary>
    /// A finished request the deleted person asked for stays, with the tombstone where they were.
    /// <para>
    /// The record surviving is the half this is for. The decision of 2026-08-28 on #49 refuses
    /// deletion-by-record because an administrator's history of what was asked and answered would go
    /// with the person, so a test that only asserted the identifier had changed would pass for a
    /// sweep that removed the row and is not what this asks.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AFinishedRequestTheDeletedPersonAskedForStaysWithTheTombstoneWhereTheyWere()
    {
        var store = new InMemoryRequestStore();

        await store.AddAsync(
            ARequest(1, TheDeletedPerson) with { State = RequestState.Fulfilled },
            CancellationToken.None).ConfigureAwait(true);

        var report = await Removal(store).RemoveAsync(TheDeletedPerson, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(new AccountRemovalReport(0, 1, 0, 0), report);

        var held = Assert.Single(await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true));

        Assert.Equal(DeletedPerson.Tombstone, held.Request.RequestedByUserId);
        Assert.NotEqual(TheDeletedPerson, held.Request.RequestedByUserId);

        // What the record still says, which is the reason it was kept rather than removed.
        Assert.Equal(RequestState.Fulfilled, held.Request.State);
        Assert.Equal("Something somebody wanted", held.Request.DisplayTitle);
        Assert.Equal(Asked, held.Request.RequestedAt);
    }

    /// <summary>
    /// An unfinished request of theirs is still removed, which is this change's interim and is
    /// asserted rather than left to be discovered.
    /// <para>
    /// The ruling asks for such a request to be closed as withdrawn instead, and there is no state
    /// for that: a withdrawn-shaped value was considered and refused on #113. Which of the two
    /// decisions stands is #337. This test is what a later change answering it has to argue with.
    /// </para>
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task AnUnfinishedRequestOfTheirsIsStillRemoved()
    {
        var store = new InMemoryRequestStore();

        await store.AddAsync(
            ARequest(1, TheDeletedPerson) with { State = RequestState.Approved },
            CancellationToken.None).ConfigureAwait(true);

        var report = await Removal(store).RemoveAsync(TheDeletedPerson, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(new AccountRemovalReport(1, 0, 0, 0), report);
        Assert.Empty(await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true));
    }

    /// <summary>
    /// Every finished state is tombstoned and every unfinished one is removed, over the whole of
    /// <see cref="RequestState"/> rather than over the two values the tests above happen to name.
    /// <para>
    /// The partition is <see cref="RetentionSweep.IsFinished"/>'s, and reading it here rather than
    /// listing states is what stops this test and the sweep meaning different things by the word on
    /// the day a state is added.
    /// </para>
    /// </summary>
    /// <param name="state">The state the request is in.</param>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Theory]
    [InlineData(RequestState.Open)]
    [InlineData(RequestState.Approved)]
    [InlineData(RequestState.Declined)]
    [InlineData(RequestState.Fulfilled)]
    [InlineData(RequestState.Failed)]
    public async Task WhichOnesStayIsTheSamePartitionTheSweepDraws(RequestState state)
    {
        var store = new InMemoryRequestStore();
        var asked = ARequest(1, TheDeletedPerson) with { State = state };

        await store.AddAsync(asked, CancellationToken.None).ConfigureAwait(true);

        var report = await Removal(store).RemoveAsync(TheDeletedPerson, CancellationToken.None).ConfigureAwait(true);
        var held = await store.GetAllAsync(CancellationToken.None).ConfigureAwait(true);

        if (RetentionSweep.IsFinished(asked))
        {
            Assert.Equal(1, report.Tombstoned);
            Assert.Equal(DeletedPerson.Tombstone, Assert.Single(held).Request.RequestedByUserId);
        }
        else
        {
            Assert.Equal(1, report.Removed);
            Assert.Empty(held);
        }
    }

    /// <summary>
    /// The tombstone is not an identifier a server could have minted, and is not the empty one.
    /// <para>
    /// Two different failures. A value a server could mint could collide with a real account, and
    /// then a tombstoned record would name somebody who never asked for anything. The empty
    /// identifier already means "nobody" in this plugin, and reusing it would make "a person who is
    /// gone asked for this" and "no person is named here" the same value.
    /// </para>
    /// </summary>
    [Fact]
    public void TheTombstoneIsNotAnIdentifierAServerCouldMint()
    {
        Assert.NotEqual(Guid.Empty, DeletedPerson.Tombstone);

        // Jellyfin mints version 4 identifiers. The version is the high nibble of the seventh byte,
        // read off the value rather than off its text.
        Assert.NotEqual(4, DeletedPerson.Tombstone.ToByteArray()[7] >> 4);

        Assert.True(DeletedPerson.Is(DeletedPerson.Tombstone));
        Assert.False(DeletedPerson.Is(TheDeletedPerson));
    }

    /// <summary>
    /// A finished request of the deleted person, an unfinished one, and one of somebody else's they
    /// had joined are answered by three different rules in one run, and the counts say which was
    /// which.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task TheThreeRulesAreCountedApartInOneRun()
    {
        var store = new InMemoryRequestStore();

        await store.AddAsync(
            ARequest(1, TheDeletedPerson) with { State = RequestState.Fulfilled },
            CancellationToken.None).ConfigureAwait(true);
        await store.AddAsync(ARequest(2, TheDeletedPerson), CancellationToken.None).ConfigureAwait(true);
        await store.AddAsync(
            ARequest(3, SomebodyElse) with { JoinedByUserIds = [TheDeletedPerson] },
            CancellationToken.None).ConfigureAwait(true);

        var report = await Removal(store).RemoveAsync(TheDeletedPerson, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(new AccountRemovalReport(1, 1, 1, 0), report);
    }

    /// <summary>
    /// The removal over a store, with a log nobody reads.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <returns>The removal.</returns>
    private static AccountRemoval Removal(IRequestStore store)
        => new AccountRemoval(store, new InMemoryNoticePreferences(), new RecordingLogger());

    /// <summary>
    /// A request somebody asked for.
    /// </summary>
    /// <param name="number">Which one, so identifiers are readable in a failure.</param>
    /// <param name="asker">Who asked.</param>
    /// <returns>The request.</returns>
    private static MediaRequest ARequest(int number, Guid asker)
        => new MediaRequest
        {
            Id = new Guid($"00000000-0000-4000-8000-00000000000{number}"),
            RequestedByUserId = asker,
            RequestedAt = Asked,
            Kind = RequestedItemKind.Movie,
            DisplayTitle = "Something somebody wanted",
            ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = number.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            StateChangedAt = Asked
        };

    /// <summary>
    /// A store that refuses every write against the revision it just handed out, so a caller that
    /// re-reads and tries again never gets anywhere. It stands for a request being decided on over
    /// and over while an account is deleted, which is the case the attempt bound exists for.
    /// </summary>
    private sealed class AStoreThatNeverSettles : IRequestStore
    {
        private readonly IRequestStore _inner;
        private long _drift;

        /// <summary>
        /// Initializes a new instance of the <see cref="AStoreThatNeverSettles"/> class.
        /// </summary>
        /// <param name="inner">The store underneath.</param>
        public AStoreThatNeverSettles(IRequestStore inner)
        {
            _inner = inner;
        }

        /// <summary>
        /// Gets how many writes it refused, so a test can say the bound was reached rather than
        /// guessing that it was.
        /// </summary>
        public int Refusals { get; private set; }

        /// <inheritdoc />
        public DateTimeOffset? LastWrittenAt => _inner.LastWrittenAt;

        /// <inheritdoc />
        public Task<StoredRequest?> GetAsync(Guid id, CancellationToken cancellationToken)
            => _inner.GetAsync(id, cancellationToken);

        /// <inheritdoc />
        public Task<IReadOnlyList<StoredRequest>> GetAllAsync(CancellationToken cancellationToken)
            => _inner.GetAllAsync(cancellationToken);

        /// <inheritdoc />
        public Task<IReadOnlyList<StoredRequest>> FindForUserAsync(Guid userId, CancellationToken cancellationToken)
            => _inner.FindForUserAsync(userId, cancellationToken);

        /// <inheritdoc />
        public Task<IReadOnlyList<StoredRequest>> FindByProviderIdentifierAsync(
            RequestedItemKind kind,
            string source,
            string value,
            CancellationToken cancellationToken)
            => _inner.FindByProviderIdentifierAsync(kind, source, value, cancellationToken);

        /// <inheritdoc />
        public Task<StoredRequest?> FindByWantAsync(Guid wantId, CancellationToken cancellationToken)
            => _inner.FindByWantAsync(wantId, cancellationToken);

        /// <inheritdoc />
        public Task<RequestPage> PageAsync(RequestQuery query, CancellationToken cancellationToken)
            => _inner.PageAsync(query, cancellationToken);

        /// <inheritdoc />
        public Task<StoredRequest> AddAsync(MediaRequest request, CancellationToken cancellationToken)
            => _inner.AddAsync(request, cancellationToken);

        /// <inheritdoc />
        public Task<StoredRequest> ReplaceAsync(
            MediaRequest request,
            long expectedRevision,
            CancellationToken cancellationToken)
        {
            Refusals++;

            throw new RequestConcurrencyException(
                request.Id,
                expectedRevision,
                new StoredRequest(request, expectedRevision + ++_drift));
        }

        /// <inheritdoc />
        public async Task<bool> RemoveAsync(Guid id, long expectedRevision, CancellationToken cancellationToken)
        {
            Refusals++;

            var current = await _inner.GetAsync(id, cancellationToken).ConfigureAwait(false);

            throw new RequestConcurrencyException(
                id,
                expectedRevision,
                current is StoredRequest held ? held with { Revision = held.Revision + ++_drift } : null);
        }
    }
}
