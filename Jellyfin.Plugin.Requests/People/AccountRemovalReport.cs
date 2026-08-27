namespace Jellyfin.Plugin.Requests.People;

/// <summary>
/// What one account removal did, counted so the caller can report it and a test can assert it.
/// <para>
/// The three are separate because they read differently to an operator answering for what is held. A
/// removed request is a record that is gone; a detached one is a record that stays with one fewer
/// person on it; and a left one is a record that still names a deleted account, which is the only one
/// of the three that anybody has to act on.
/// </para>
/// </summary>
/// <param name="Removed">Requests the deleted account had asked for, which were removed.</param>
/// <param name="Detached">Requests somebody else had asked for, which the account came off.</param>
/// <param name="Left">
/// Requests that still name the account, because they kept moving while the removal ran. Nothing
/// looks at these again on its own.
/// </param>
public readonly record struct AccountRemovalReport(int Removed, int Detached, int Left);
