using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Requests.Notify;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Requests.Api;

/// <summary>
/// The one switch a person owns on this plugin: whether it pushes them a message when a request of
/// their own moves.
/// <para>
/// <b>Nobody can reach anybody else's setting from here, and it is the shape rather than a check.</b>
/// The person is read off the call and there is no parameter, no route segment and no body field
/// that names one, so there is no call an administrator can make that changes what somebody else is
/// told. That is the decision recorded on #9: an operator decides what leaves their server, and a
/// person decides what is pushed at them about their own request.
/// </para>
/// <para>
/// A controller of its own rather than two more actions beside the queue, for the reason
/// <see cref="MyRequestsPageController"/> is one: it reads no request, needs no store, no clock and
/// no identifier source, and putting it there would hand every store test a preference file to
/// carry.
/// </para>
/// </summary>
[Authorize]
public sealed class MyNoticeSettingController : RequestsControllerBase
{
    private readonly ICallerIdentity _callers;
    private readonly INoticePreferences _preferences;

    /// <summary>
    /// Initializes a new instance of the <see cref="MyNoticeSettingController"/> class.
    /// </summary>
    /// <param name="callers">Who the server says is calling.</param>
    /// <param name="preferences">What is kept about who wants to be told.</param>
    public MyNoticeSettingController(ICallerIdentity callers, INoticePreferences preferences)
    {
        _callers = callers;
        _preferences = preferences;
    }

    /// <summary>
    /// What the caller has set about being told when their own request moves.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The caller's own setting, which is on where they have never touched it.</returns>
    [HttpGet("Notices/Mine")]
    [Authorize]
    [ProducesResponseType<MyNoticeSetting>(StatusCodes.Status200OK)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<MyNoticeSetting>> MineAsync(CancellationToken cancellationToken)
    {
        var caller = await _callers.UserIdAsync(HttpContext).ConfigureAwait(false);

        if (caller is not Guid person)
        {
            return NoUser("This call is authenticated but names no user, so there is nobody whose setting this would be.");
        }

        try
        {
            return Ok(new MyNoticeSetting
            {
                TellsMe = await _preferences.TellsThemAsync(person, cancellationToken).ConfigureAwait(false)
            });
        }
        catch (NoticePreferencesException)
        {
            return CouldNotBeRead();
        }
    }

    /// <summary>
    /// Turns the message about the caller's own requests on or off, for the caller and nobody else.
    /// </summary>
    /// <param name="body">Which way to set it.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The setting as it stands after the call.</returns>
    [HttpPost("Notices/Mine")]
    [Authorize]
    [ProducesResponseType<MyNoticeSetting>(StatusCodes.Status200OK)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<RequestFailure>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<MyNoticeSetting>> SetMineAsync(
        [FromBody] SetMyNoticeBody body,
        CancellationToken cancellationToken)
    {
        if (body?.TellsMe is not bool tellsMe)
        {
            return Invalid(
                "tellsMe",
                "This call says nothing about which way to set it, and a call that says nothing is not a call to turn it off.");
        }

        var caller = await _callers.UserIdAsync(HttpContext).ConfigureAwait(false);

        if (caller is not Guid person)
        {
            return NoUser("This call is authenticated but names no user, so there is nobody whose setting this would be.");
        }

        try
        {
            return Ok(new MyNoticeSetting
            {
                TellsMe = await _preferences.SetAsync(person, tellsMe, cancellationToken).ConfigureAwait(false)
            });
        }
        catch (NoticePreferencesException)
        {
            return CouldNotBeRead();
        }
    }

    /// <summary>
    /// One failure, under the status code its class is reported with, which is
    /// <see cref="RequestFailure.StatusFor"/>'s pairing rather than one chosen here.
    /// </summary>
    /// <param name="code">What went wrong.</param>
    /// <param name="message">The sentence for the person reading it.</param>
    /// <param name="field">The field that is wrong, where there is one.</param>
    /// <returns>The failure, under its status code.</returns>
    private ObjectResult Failed(RequestFailureCode code, string message, string? field = null)
        => StatusCode(
            RequestFailure.StatusFor(code),
            new RequestFailure { Code = code, Message = message, Field = field });

    /// <summary>
    /// A body that cannot be acted on, naming the field that is wrong.
    /// </summary>
    /// <param name="field">The field, spelled as the body spells it.</param>
    /// <param name="reason">What is wrong with it.</param>
    /// <returns>The failure.</returns>
    private ObjectResult Invalid(string field, string reason)
        => Failed(RequestFailureCode.InvalidBody, reason, field: field);

    /// <summary>
    /// A call that authenticated and names no person.
    /// </summary>
    /// <param name="reason">What that means for this endpoint.</param>
    /// <returns>The failure.</returns>
    private ObjectResult NoUser(string reason)
        => Failed(RequestFailureCode.NoUserOnTheCall, reason);

    /// <summary>
    /// What is kept about who wants to be told could not be read.
    /// <para>
    /// Nothing from the exception reaches the caller: its message is written for an operator reading
    /// the log and this one is written for the person reading the screen, and neither of them is
    /// served by a path on the server's disk.
    /// </para>
    /// </summary>
    /// <returns>The failure.</returns>
    private ObjectResult CouldNotBeRead()
        => Failed(
            RequestFailureCode.TheStoreCouldNotBeRead,
            "This setting could not be read, so nothing was changed. Nothing is wrong with the call, and the server log says what an operator has to repair.");
}
