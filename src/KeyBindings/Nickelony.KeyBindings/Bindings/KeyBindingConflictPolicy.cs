namespace Nickelony.KeyBindings;

/// <summary>
/// Controls how <see cref="IKeyBindingService{TCommandId}.Apply"/> treats key chords that are
/// currently assigned to other commands.
/// </summary>
public enum KeyBindingConflictPolicy
{
	/// <summary>Reject the proposed set with <see cref="KeyBindingOutcome.Conflict"/> when it collides with another command.</summary>
	Reject,

	/// <summary>
	/// Let the proposed set take precedence over remappable commands: a conflicting catalog default
	/// stays stored but is shadowed, and a conflicting binding is removed from the other command's
	/// override entry. Conflicts with commands that are not
	/// <see cref="CommandRemappingPolicy.Remappable"/> are still rejected, and a prefix relationship
	/// is never resolved by either policy (it is reported as
	/// <see cref="KeyBindingOutcome.PrefixConflict"/>).
	/// </summary>
	Replace
}
