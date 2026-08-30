namespace Jellyfin.Plugin.Requests.People;

/// <summary>
/// What one account removal did, counted so the caller can report it and a test can assert it.
/// <para>
/// The four are separate because they read differently to an operator answering for what is held. A
/// removed request is a record that is gone; a tombstoned one is a record that stays with nobody in
/// it, which is a different answer to "what do you still hold" than either of the other two; a
/// detached one is a record that stays with one fewer person on it; and a left one is a record that
/// still names a deleted account, which is the only one of the four that anybody has to act on.
/// </para>
/// <para>
/// Removed and tombstoned are counted apart rather than added together on purpose. An operator
/// asked what became of a deleted person's requests is answering about records that no longer exist
/// and records that do, and one number covering both would make the second sound like the first.
/// </para>
/// </summary>
/// <param name="Removed">
/// Unfinished requests the deleted account had asked for, which were removed. Whether those should
/// instead be closed as withdrawn is #337.
/// </param>
/// <param name="Tombstoned">
/// Finished requests the deleted account had asked for, which stay with
/// <see cref="DeletedPerson.Tombstone"/> where the person was.
/// </param>
/// <param name="Detached">Requests somebody else had asked for, which the account came off.</param>
/// <param name="Left">
/// Requests that still name the account, because they kept moving while the removal ran. Nothing
/// looks at these again on its own.
/// </param>
public readonly record struct AccountRemovalReport(int Removed, int Tombstoned, int Detached, int Left);
