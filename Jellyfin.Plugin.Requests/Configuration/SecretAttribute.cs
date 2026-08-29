using System;

namespace Jellyfin.Plugin.Requests.Configuration;

/// <summary>
/// Marks a setting whose value may not appear in anything this plugin writes for somebody else to
/// read.
/// <para>
/// <b>Operators paste logs into issue trackers.</b> That is not a habit anybody is going to change,
/// so the log has to be safe to paste, and the same goes for an error message, an activity entry and
/// any diagnostic. #100 is where that was decided.
/// </para>
/// <para>
/// <b>What the mark is, and what it is not.</b> It is a name a rule can be pointed at: the invariant
/// lint refuses a marked setting reaching a message, and the suite refuses a mark this attribute
/// carries that no rule names, so the two cannot drift apart. It is not a redacting type, and that
/// is a decision rather than an omission - the host keeps this configuration by serialising it and
/// the settings page edits it the same way, so a value that hands out a redaction on those paths is
/// a setting that does not survive a restart. Decision 9 on #113 answered that with a write-only
/// settings page instead, which #318 built.
/// </para>
/// <para>
/// <b>So this attribute refuses nothing on its own.</b> Nothing reads it at run time and no code
/// path behaves differently for a marked property. What holds the rule is the lint and the suite,
/// and this is the thing they both read, which is why it is one mark rather than a list in each of
/// them.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SecretAttribute : Attribute
{
}
