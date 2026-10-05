namespace Nickelony.KeyBindings;

/// <summary>
/// Controls how <see cref="IKeyBindingService{TCommandId}.Apply"/> and <see cref="IKeyBindingService{TCommandId}.Validate"/> treat key chords
/// that are currently assigned to other commands.
/// </summary>
public enum KeyBindingConflictPolicy
{
	/// <summary>Reject the proposed set with <see cref="KeyBindingOutcome.Conflict"/> when it collides with another command.</summary>
	Reject,

	/// <summary>
	/// Let the proposed set take precedence over remappable commands: a conflicting catalog default stays stored but is shadowed, and a conflicting
	/// binding is removed from the other command's override entry.
	/// </summary>
	/// <remarks>
	/// A conflict with a non-remappable command, and any prefix relationship, is still rejected
	/// (<see cref="KeyBindingOutcome.PrefixConflict"/> for the latter).
	/// </remarks>
	Replace,
}
