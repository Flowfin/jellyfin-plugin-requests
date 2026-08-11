using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Bridge;
using Jellyfin.Plugin.Requests.Model;
using Xunit;

namespace Jellyfin.Plugin.Requests.Tests.Bridge;

/// <summary>
/// The bridge most servers run. It is the shipping default rather than a placeholder, so it is
/// tested for what it answers rather than left to be covered by whatever calls it later.
/// <para>
/// Two mistakes are what these tests exist against. Reporting that something answered, which would
/// put "the external service is up" on an operator's page for a server that has none. And handing
/// back an invented reference, which would leave rows claiming a handover that nothing can ever be
/// asked about.
/// </para>
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public classes only, so internal would silently run nothing.")]
public class NoRequestBackendTests
{
    /// <summary>
    /// Nothing configured is its own answer. Neither of the other two values is true of a server
    /// with no service behind it, and an operator reading either would be told something about a
    /// system they do not run.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task NothingIsConfiguredRatherThanReachableOrDown()
    {
        var reachability = await new NoRequestBackend()
            .CheckReachableAsync(CancellationToken.None);

        Assert.Equal(BackendReachability.NotConfigured, reachability);
    }

    /// <summary>
    /// Submitting hands nothing over and says so by returning no reference. It does not throw: an
    /// approval on a server with no service is the ordinary case rather than a failure, and a
    /// caller that had to catch something here would be a caller with a branch for the absence.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task SubmittingKeepsNothingAndHandsBackNoReference()
    {
        var reference = await new NoRequestBackend()
            .SubmitAsync(Asked(), CancellationToken.None);

        Assert.Null(reference);
    }

    /// <summary>
    /// Asked about any reference, it knows nothing. Answered rather than refused, because a
    /// reference issued by a service an operator has since removed is a fact about the install.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task NothingIsKnownAboutAReferenceThisBridgeNeverIssued()
    {
        var report = await new NoRequestBackend()
            .ReportAsync(SomeoneElsesReference(), CancellationToken.None);

        Assert.Null(report);
    }

    /// <summary>
    /// Withdrawing something it never accepted completes. The state the caller wanted is the state
    /// the server is already in, and raising an error for that would make every caller handle a
    /// case where nothing went wrong.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task WithdrawingSomethingItNeverAcceptedIsNotAFailure()
    {
        await new NoRequestBackend().WithdrawAsync(SomeoneElsesReference(), CancellationToken.None);
    }

    /// <summary>
    /// Every operation observes a cancelled token. The default runs on most installs, so a caller
    /// that cancels is checked against this one long before it meets an adapter, and one that
    /// silently ignored the token would let that caller ship looking correct and hang on the first
    /// bridge that honours one.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task EveryOperationObservesACancelledToken()
    {
        var backend = new NoRequestBackend();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => backend.CheckReachableAsync(cancelled.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => backend.SubmitAsync(Asked(), cancelled.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => backend.ReportAsync(SomeoneElsesReference(), cancelled.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => backend.WithdrawAsync(SomeoneElsesReference(), cancelled.Token));
    }

    /// <summary>
    /// One request, as a person would have asked for it.
    /// </summary>
    /// <returns>The request.</returns>
    private static MediaRequest Asked()
    {
        var asked = new DateTimeOffset(2026, 3, 14, 21, 5, 0, TimeSpan.Zero);

        return new MediaRequest
        {
            Id = new Guid("b8f0a5c7-1d94-4e63-9a2f-7c5b0e83d146"),
            RequestedByUserId = new Guid("3f8c1d05-2e69-4b74-a018-9d5e7c4b6a12"),
            RequestedAt = asked,
            StateChangedAt = asked,
            Kind = RequestedItemKind.Movie,
            DisplayTitle = "Sorcerer",
            ProviderIds = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tmdb"] = "11631" }
        };
    }

    /// <summary>
    /// A reference from a service this bridge is not. Nothing issued it here, which is the point of
    /// asking about it.
    /// </summary>
    /// <returns>The reference.</returns>
    private static BackendReference SomeoneElsesReference()
        => new() { Service = "a service this server does not run", Id = "418" };
}
