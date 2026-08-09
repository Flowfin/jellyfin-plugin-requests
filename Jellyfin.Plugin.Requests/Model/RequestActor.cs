using System;

namespace Jellyfin.Plugin.Requests.Model;

/// <summary>
/// What a caller is, as far as moving a request is concerned. Three kinds of caller reach the
/// lifecycle and no more: the person who asked, an administrator of the server, and the plugin
/// itself acting on something it observed.
/// <para>
/// It is a set rather than a single value because one caller can be two of them at once. An
/// administrator who asked for something themselves is both the requester and an administrator on
/// that request and only an administrator on everybody else's, and a rule that made a caller choose
/// one label would have to guess which. <see cref="RequestCaller.RolesOn"/> is where a caller and a
/// request produce this set, and <see cref="RequestTransition.Permitted"/> is the set a cell admits;
/// a move is permitted where the two sets share a value.
/// </para>
/// <para>
/// The values say what a caller <b>is</b> and never what it may do. What each one may do is per
/// cell, in <see cref="RequestLifecycle.Table"/>, so widening a permission is an edit to one row of
/// a table that a test reads back rather than a condition added somewhere a reader has to find.
/// </para>
/// </summary>
[Flags]
public enum RequestActor
{
    /// <summary>
    /// Nothing. This is what an ordinary user holds on somebody else's request, and it is what every
    /// refused cell admits, so the two are refused by the same comparison rather than by two rules.
    /// <para>
    /// It is the zero value on purpose, so that a set nobody filled in permits nothing. A default
    /// that permitted something would make a forgotten cell the widest one in the table.
    /// </para>
    /// </summary>
    None = 0,

    /// <summary>
    /// The person who asked for this thing. Held only against their own request, which is decided by
    /// comparing the caller's user identifier with
    /// <see cref="MediaRequest.RequestedByUserId"/> and never by comparing names.
    /// <para>
    /// No cell of the table admits this on its own today. Asking is not a move: approving one's own
    /// request is the case this whole check exists for, a decline is an operator's answer, and there
    /// is no state for a user withdrawing, refused with the <c>Cancelled</c> state on #113. The
    /// value exists because the surfaces have to answer "is this yours" against the same comparison,
    /// and because the per-user automatic approval left to M12 is a cell that admits it.
    /// </para>
    /// </summary>
    Requester = 1,

    /// <summary>
    /// An administrator of the server. Every decision belongs here: an approval, a decline, and
    /// taking either back.
    /// <para>
    /// Whether an administrator may decide on their own request is a configuration question that
    /// belongs to M12, and it is asked of the caller rather than of the table: such a caller is
    /// built with <see cref="RequestCaller.User"/> instead of
    /// <see cref="RequestCaller.Administrator"/> and holds only <see cref="Requester"/> on that one
    /// request. Nothing here decides it and nothing here has to change when it is decided.
    /// </para>
    /// </summary>
    Administrator = 2,

    /// <summary>
    /// The plugin itself, acting on something it observed rather than on somebody's decision. Every
    /// observation belongs here: that the library now holds what was asked for, and that something
    /// sent onward did not arrive.
    /// <para>
    /// It is separate from an administrator because the two read differently in a history and in a
    /// complaint. A request the library check fulfilled and one a person marked fulfilled are
    /// different facts, and a person should not answer for a move nobody made.
    /// </para>
    /// </summary>
    Plugin = 4
}
