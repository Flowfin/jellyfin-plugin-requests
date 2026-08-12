using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Configuration;
using Jellyfin.Plugin.Requests.Identity;
using Jellyfin.Plugin.Requests.Intake;
using Jellyfin.Plugin.Requests.Model;
using Jellyfin.Plugin.Requests.Storage;
using Jellyfin.Plugin.Requests.Time;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Requests.Seam;

/// <summary>
/// The call that does the work of the seam: one want from the sibling discover plugin, turned into a
/// request in this plugin's queue.
/// <para>
/// <b>A title this plugin has never seen is the ordinary case.</b> There is no pre-agreed catalogue
/// on this side and nothing here fetches one, so a want naming something absent from every request
/// ever made is a request being made for the first time and not an error. The title and the year are
/// stored as they arrived and are never refreshed, which is what lets the queue render on a server
/// where nothing outbound resolves.
/// </para>
/// <para>
/// <b>A refusal is a decision and reaches the caller as one bit.</b> The contract carries no field
/// for a reason, so what the other side learns is that the handover was not accepted, and the reason
/// is written to this server's log where an operator can find it. Answering <see langword="false"/>
/// rather than throwing is deliberate: an exception crossing a plugin boundary is a fault in the
/// caller's own code path for something that is an ordinary answer here.
/// </para>
/// <para>
/// <b>Whether an ask joins an existing request is not decided here.</b> It is
/// <see cref="RequestIntake"/>'s, which is the same object the HTTP endpoint asks, so two users
/// wanting the same film produce one request with both of them recorded however each of them asked.
/// The one thing this seam does not yet do is recognise the same want arriving twice by the
/// sibling's own identifier for it, which is #116: today a repeat is caught by the identity rule
/// over the provider identifiers, and a want carrying none has no identity to be caught by.
/// </para>
/// </summary>
public sealed class WantHandover : IWantHandover
{
    /// <summary>
    /// The contract version this plugin implements.
    /// <para>
    /// One number rather than a range, and the rule around it is in <c>docs/seam.md</c>. The
    /// sibling's board has not minted a version rule yet, so what this constant records is which
    /// version this side believes it implements rather than a number read off the contract. That is
    /// cheap to correct because this seam is an in-process call: it serialises nothing, nothing on
    /// disk carries it, and no caller outside this process can be pinned to it.
    /// </para>
    /// </summary>
    public const int KnownContractVersion = 1;

    private readonly RequestIntake _intake;
    private readonly IClock _clock;
    private readonly IIdentifierSource _identifiers;
    private readonly IInstallSettings _settings;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WantHandover"/> class.
    /// </summary>
    /// <param name="store">Where requests are kept.</param>
    /// <param name="clock">The injected clock, so a request's times are testable.</param>
    /// <param name="identifiers">Where a new request's identifier comes from.</param>
    /// <param name="settings">What this install is set to.</param>
    /// <param name="logger">Where a refusal is written.</param>
    /// <exception cref="ArgumentNullException">Where anything it needs is missing.</exception>
    public WantHandover(
        IRequestStore store,
        IClock clock,
        IIdentifierSource identifiers,
        IInstallSettings settings,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(identifiers);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _intake = new RequestIntake(store);
        _clock = clock;
        _identifiers = identifiers;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Where there is no want to accept.</exception>
    public async Task<bool> AcceptAsync(HandedOverWant want, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(want);

        // The version is read before any other field. A field set whose version this side does not
        // know is refused whole rather than read for the fields it recognises: reading what is
        // recognised and ignoring the rest makes a version that changed the meaning of a field
        // indistinguishable from one that added a field, and the first of those is a want filed
        // against the wrong thing rather than a want dropped.
        if (want.ContractVersion != KnownContractVersion)
        {
            return Refused(want, HandoverRefusal.ContractVersionNotKnown);
        }

        if (want.RequestedByUserId == Guid.Empty)
        {
            return Refused(want, HandoverRefusal.NoUserNamed);
        }

        if (string.IsNullOrWhiteSpace(want.Title))
        {
            return Refused(want, HandoverRefusal.NoTitle);
        }

        if (!Enum.IsDefined(want.Kind))
        {
            return Refused(want, HandoverRefusal.KindNotRecognised);
        }

        bool accepted;

        try
        {
            accepted = Accepts(want.Kind);
        }
        catch (InvalidConfigurationException)
        {
            return Refused(want, HandoverRefusal.ThisInstallCannotRun);
        }

        if (!accepted)
        {
            return Refused(want, HandoverRefusal.KindNotAccepted);
        }

        var asked = _clock.UtcNow;

        var incoming = new MediaRequest
        {
            Id = _identifiers.NewId(),
            RequestedByUserId = want.RequestedByUserId,
            RequestedAt = asked,
            StateChangedAt = asked,
            Kind = want.Kind,
            DisplayTitle = want.Title,
            DisplayYear = want.Year,
            ProviderIds = want.ProviderIds
        };

        IntakeResult intake;

        try
        {
            intake = await _intake.AskAsync(incoming, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestStoreLoadException)
        {
            // The store says which file and why in its own log line. What is added here is which
            // want was lost because of it, so the two can be put together.
            return Refused(want, HandoverRefusal.TheStoreCouldNotBeReached);
        }
        catch (RequestConcurrencyException)
        {
            // The request kept moving underneath the join for as many attempts as the intake makes.
            // That is a contended request rather than a fault, and the contract has no way to ask
            // the caller to try again, so it is refused and the other side's own repeat handling is
            // what brings the want back.
            return Refused(want, HandoverRefusal.TheStoreCouldNotBeReached);
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "The want {WantId} handed over the seam is request {RequestId}, which the handover {Outcome}.",
                want.WantId,
                intake.Request.Request.Id,
                intake.Outcome);
        }

        return true;
    }

    /// <summary>
    /// Whether this install accepts that kind of thing at all.
    /// <para>
    /// Asked of the settings on every handover rather than held, because an operator turning a kind
    /// off means the next want of that kind, not the next restart.
    /// </para>
    /// </summary>
    /// <param name="kind">What was wanted.</param>
    /// <returns><see langword="true"/> where this install takes requests for it.</returns>
    /// <exception cref="InvalidConfigurationException">
    /// Where what is stored is something this plugin cannot run on.
    /// </exception>
    private bool Accepts(RequestedItemKind kind)
    {
        var current = _settings.Current;

        return kind switch
        {
            RequestedItemKind.Movie => current.AcceptsMovies,
            RequestedItemKind.Series => current.AcceptsSeries,

            _ => false
        };
    }

    /// <summary>
    /// Writes down why a want was not turned into a request, and answers the caller.
    /// <para>
    /// The line carries the sibling's own identifier for the want, the reason, and the version that
    /// arrived, which is what an operator asked about a want by the other side's identifier needs to
    /// find it. It carries no title and no user, because a log is pasted into issue trackers and
    /// what somebody asked for is the thing in this plugin worth being careful with.
    /// </para>
    /// </summary>
    /// <param name="want">What arrived.</param>
    /// <param name="refusal">Why it is not becoming a request.</param>
    /// <returns>Always <see langword="false"/>, which is what the contract lets this side say.</returns>
    private bool Refused(HandedOverWant want, HandoverRefusal refusal)
    {
        _logger.LogWarning(
            "A want handed over the seam was not made into a request: {Refusal}. The other side calls it {WantId} and built it against contract version {ContractVersion}.",
            refusal,
            want.WantId,
            want.ContractVersion);

        return false;
    }
}
