using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Entities.Security;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Session;
using MediaBrowser.Model.SyncPlay;

namespace Jellyfin.Plugin.Requests.Tests.Doubles;

/// <summary>
/// The server's session manager with exactly one method that works.
/// <para>
/// <b>Every other way of sending anything raises, and that is what this double is for.</b> The
/// interface offers eleven ways to push something at somebody, and three of them reach people the
/// plugin was not talking about: an administrator broadcast, a device broadcast, and the two
/// remote-control commands that take a session identifier rather than a user. A double that
/// answered all of them politely could not tell a test that only the requester was reached from one
/// where everybody was. So the only member with a body is the one this plugin is allowed to call,
/// and a change that reaches for another one fails as a raised exception naming the member.
/// </para>
/// <para>
/// The recorded call keeps the arguments as they were passed, the user list included, because the
/// claim under test is about who is in that list.
/// </para>
/// </summary>
#pragma warning disable CS0067 // The event is never used: the host raises these, and nothing here does.
internal sealed class ASessionManagerThatOnlyDelivers : ISessionManager
{
    private readonly List<Delivery> _delivered = [];

    /// <inheritdoc />
    public event EventHandler<PlaybackProgressEventArgs>? PlaybackStart;

    /// <inheritdoc />
    public event EventHandler<PlaybackProgressEventArgs>? PlaybackProgress;

    /// <inheritdoc />
    public event EventHandler<PlaybackStopEventArgs>? PlaybackStopped;

    /// <inheritdoc />
    public event EventHandler<SessionEventArgs>? SessionStarted;

    /// <inheritdoc />
    public event EventHandler<SessionEventArgs>? SessionEnded;

    /// <inheritdoc />
    public event EventHandler<SessionEventArgs>? SessionActivity;

    /// <inheritdoc />
    public event EventHandler<SessionEventArgs>? SessionControllerConnected;

    /// <inheritdoc />
    public event EventHandler<SessionEventArgs>? CapabilitiesChanged;

    /// <inheritdoc />
    public IEnumerable<SessionInfo> Sessions => [];

    /// <summary>
    /// Gets every push this double was asked to make, in order.
    /// </summary>
    public IReadOnlyList<Delivery> Delivered => _delivered;

    /// <summary>
    /// Gets a value indicating whether the one usable call fails instead of delivering.
    /// <para>
    /// A client that cannot be reached is an ordinary outcome rather than a second kind of double:
    /// a session that went away between the move and the push, a host that is shutting down. What a
    /// test needs from it is that the request is unaffected, so the failure belongs on the same
    /// object as the success.
    /// </para>
    /// </summary>
    public bool Refuses { get; init; }

    /// <inheritdoc />
    public Task SendMessageToUserSessions<T>(List<Guid> userIds, SessionMessageType name, T data, CancellationToken cancellationToken)
    {
        if (Refuses)
        {
            throw new InvalidOperationException("This session manager was told to fail the push.");
        }

        _delivered.Add(new Delivery(userIds, name, data));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendMessageToUserSessions<T>(List<Guid> userIds, SessionMessageType name, Func<T> dataFn, CancellationToken cancellationToken)
        => throw Refused(nameof(SendMessageToUserSessions) + " with a factory");

    /// <inheritdoc />
    public Task SendMessageToAdminSessions<T>(SessionMessageType name, T data, CancellationToken cancellationToken)
        => throw Refused(nameof(SendMessageToAdminSessions));

    /// <inheritdoc />
    public Task SendMessageToUserDeviceSessions<T>(string deviceId, SessionMessageType name, T data, CancellationToken cancellationToken)
        => throw Refused(nameof(SendMessageToUserDeviceSessions));

    /// <inheritdoc />
    public Task SendGeneralCommand(string controllingSessionId, string sessionId, GeneralCommand command, CancellationToken cancellationToken)
        => throw Refused(nameof(SendGeneralCommand));

    /// <inheritdoc />
    public Task SendMessageCommand(string controllingSessionId, string sessionId, MessageCommand command, CancellationToken cancellationToken)
        => throw Refused(nameof(SendMessageCommand));

    /// <inheritdoc />
    public Task SendPlayCommand(string controllingSessionId, string sessionId, PlayRequest command, CancellationToken cancellationToken)
        => throw Refused(nameof(SendPlayCommand));

    /// <inheritdoc />
    public Task SendSyncPlayCommand(string sessionId, SendCommand command, CancellationToken cancellationToken)
        => throw Refused(nameof(SendSyncPlayCommand));

    /// <inheritdoc />
    public Task SendSyncPlayGroupUpdate<T>(string sessionId, GroupUpdate<T> command, CancellationToken cancellationToken)
        => throw Refused(nameof(SendSyncPlayGroupUpdate));

    /// <inheritdoc />
    public Task SendBrowseCommand(string controllingSessionId, string sessionId, BrowseRequest command, CancellationToken cancellationToken)
        => throw Refused(nameof(SendBrowseCommand));

    /// <inheritdoc />
    public Task SendPlaystateCommand(string controllingSessionId, string sessionId, PlaystateRequest command, CancellationToken cancellationToken)
        => throw Refused(nameof(SendPlaystateCommand));

    /// <inheritdoc />
    public Task SendRestartRequiredNotification(CancellationToken cancellationToken)
        => throw Refused(nameof(SendRestartRequiredNotification));

    /// <inheritdoc />
    public Task<SessionInfo> LogSessionActivity(string appName, string appVersion, string deviceId, string deviceName, string remoteEndPoint, User user)
        => throw Refused(nameof(LogSessionActivity));

    /// <inheritdoc />
    public void OnSessionControllerConnected(SessionInfo session) => throw Refused(nameof(OnSessionControllerConnected));

    /// <inheritdoc />
    public void UpdateDeviceName(string sessionId, string reportedDeviceName) => throw Refused(nameof(UpdateDeviceName));

    /// <inheritdoc />
    public Task OnPlaybackStart(PlaybackStartInfo info) => throw Refused(nameof(OnPlaybackStart));

    /// <inheritdoc />
    public Task OnPlaybackProgress(PlaybackProgressInfo info) => throw Refused(nameof(OnPlaybackProgress));

    /// <inheritdoc />
    public Task OnPlaybackProgress(PlaybackProgressInfo info, bool isAutomated) => throw Refused(nameof(OnPlaybackProgress));

    /// <inheritdoc />
    public Task OnPlaybackStopped(PlaybackStopInfo info) => throw Refused(nameof(OnPlaybackStopped));

    /// <inheritdoc />
    public void AddAdditionalUser(string sessionId, Guid userId) => throw Refused(nameof(AddAdditionalUser));

    /// <inheritdoc />
    public void RemoveAdditionalUser(string sessionId, Guid userId) => throw Refused(nameof(RemoveAdditionalUser));

    /// <inheritdoc />
    public void ReportNowViewingItem(string sessionId, string itemId) => throw Refused(nameof(ReportNowViewingItem));

    /// <inheritdoc />
    public ValueTask ReportSessionEnded(string accessToken) => throw Refused(nameof(ReportSessionEnded));

    /// <inheritdoc />
    public Task<AuthenticationResult> AuthenticateNewSession(AuthenticationRequest request) => throw Refused(nameof(AuthenticateNewSession));

    /// <inheritdoc />
    public Task<AuthenticationResult> AuthenticateDirect(AuthenticationRequest request) => throw Refused(nameof(AuthenticateDirect));

    /// <inheritdoc />
    public void ReportCapabilities(string sessionId, ClientCapabilities capabilities) => throw Refused(nameof(ReportCapabilities));

    /// <inheritdoc />
    public void ReportTranscodingInfo(string deviceId, TranscodingInfo info) => throw Refused(nameof(ReportTranscodingInfo));

    /// <inheritdoc />
    public void ClearTranscodingInfo(string deviceId) => throw Refused(nameof(ClearTranscodingInfo));

    /// <inheritdoc />
    public SessionInfo GetSession(string deviceId, string client, string version) => throw Refused(nameof(GetSession));

    /// <inheritdoc />
    public IReadOnlyList<SessionInfoDto> GetSessions(Guid userId, string deviceId, int? activeWithinSeconds, Guid? controllableUserToCheck, bool isApiKey)
        => throw Refused(nameof(GetSessions));

    /// <inheritdoc />
    public Task<SessionInfo> GetSessionByAuthenticationToken(string token, string deviceId, string remoteEndpoint)
        => throw Refused(nameof(GetSessionByAuthenticationToken));

    /// <inheritdoc />
    public Task<SessionInfo> GetSessionByAuthenticationToken(Device info, string deviceId, string remoteEndpoint, string appVersion)
        => throw Refused(nameof(GetSessionByAuthenticationToken));

    /// <inheritdoc />
    public Task Logout(string accessToken) => throw Refused(nameof(Logout));

    /// <inheritdoc />
    public Task Logout(Device device) => throw Refused(nameof(Logout));

    /// <inheritdoc />
    public Task RevokeUserTokens(Guid userId, string currentAccessToken) => throw Refused(nameof(RevokeUserTokens));

    /// <inheritdoc />
    public Task CloseIfNeededAsync(SessionInfo session) => throw Refused(nameof(CloseIfNeededAsync));

    /// <inheritdoc />
    public Task CloseLiveStreamIfNeededAsync(string liveStreamId, string sessionIdOrPlaySessionId)
        => throw Refused(nameof(CloseLiveStreamIfNeededAsync));

    /// <inheritdoc />
    public SessionInfoDto ToSessionInfoDto(SessionInfo sessionInfo) => throw Refused(nameof(ToSessionInfoDto));

    /// <summary>
    /// What a member nothing here is allowed to call answers with.
    /// </summary>
    /// <param name="member">The member that was reached for.</param>
    /// <returns>The exception to raise, so every call site is one line.</returns>
    private static NotSupportedException Refused(string member)
        => new NotSupportedException(
            "This plugin reached ISessionManager." + member + ". The only call it is allowed to make is SendMessageToUserSessions, which names the one person a message is about; everything else here reaches somebody the message was not about.");

    /// <summary>
    /// One push, as it was asked for.
    /// </summary>
    /// <param name="UserIds">Whose sessions it was addressed to.</param>
    /// <param name="Name">The name it went out under.</param>
    /// <param name="Payload">What was sent.</param>
    internal sealed record Delivery(IReadOnlyList<Guid> UserIds, SessionMessageType Name, object? Payload);
}
#pragma warning restore CS0067
