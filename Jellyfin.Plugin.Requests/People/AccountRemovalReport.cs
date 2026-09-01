namespace Jellyfin.Plugin.Requests.People;

/// <summary>
/// What one account removal did, counted so the caller can report it and a test can assert it.
/// <para>
/// The four are separate because they read differently to an operator answering for what is held. A
/// declined request is a record that stays, finished, saying that somebody who is gone asked for
/// this and was answered by nobody; a tombstoned one is a record that stays as it was decided, with
/// nobody in it; a detached one is a record that stays with one fewer person on it; and a left one
/// is a record that still names a deleted account, which is the only one of the four that anybody
/// has to act on.
/// </para>
/// <para>
/// Declined and tombstoned are counted apart rather than added together on purpose. Both leave a
/// record behind, and they are not the same record: one was ended by this pass and one was already
/// finished when the pass reached it, which is the difference between what became of an open
/// question and what became of an answered one.
/// </para>
/// <para>
/// <b>The first count was called Removed and named a record that no longer existed.</b> Nothing is
/// removed by this pass any more: #337 answered that an open request of a deleted person is closed
/// rather than deleted, and #361 built it. The field is renamed rather than kept with a new meaning,
/// because a count called Removed that counts records still in the store is the worst of the two
/// available mistakes.
/// </para>
/// </summary>
/// <param name="Declined">
/// Unfinished requests the deleted account had asked for, which were declined for
/// <see cref="Model.DeclineReason.TheRequesterIsGone"/> and stay with
/// <see cref="DeletedPerson.Tombstone"/> where the person was.
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
public readonly record struct AccountRemovalReport(int Declined, int Tombstoned, int Detached, int Left);
