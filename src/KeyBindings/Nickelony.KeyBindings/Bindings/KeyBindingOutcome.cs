namespace Nickelony.KeyBindings;

/// <summary>Classifies what a key binding validation or mutation operation did.</summary>
/// <remarks>
/// A mutation reports the outcome of the binding set it produces, not only of the set it was asked to store:
/// a <see cref="Conflict"/> or <see cref="PrefixConflict"/> can describe a binding another command keeps, or regains, when the operation removes
/// or restores an entry.
/// </remarks>
public enum KeyBindingOutcome
{
	/// <summary>No outcome has been assigned; no operation returns it.</summary>
	/// <remarks>
	/// It is the zero value, so an unassigned variable or a <see langword="default"/> value is Unspecified and never <see cref="Succeeded"/>.
	/// A host never has to handle it as the outcome of an operation.
	/// </remarks>
	Unspecified,

	/// <summary>The proposed binding set is valid, or the requested change was applied.</summary>
	Succeeded,

	/// <summary>The command is not present in the catalog.</summary>
	UnknownCommand,

	/// <summary>The command is <see cref="CommandRemappingPolicy.HostManaged"/>.</summary>
	HostManaged,

	/// <summary>The command is <see cref="CommandRemappingPolicy.HostReserved"/>.</summary>
	HostReserved,

	/// <summary>The proposed binding set contains the same key chord more than once.</summary>
	DuplicateInCommand,

	/// <summary>
	/// A chord is already assigned to another command and the operation cannot take it over: a refused request, a non-remappable owner,
	/// a preserved entry, or a restoring default that would collide with a binding another command holds.
	/// </summary>
	Conflict,

	/// <summary>
	/// A chord stands in a strict prefix relationship with another co-active bound chord, in either direction, so the longer
	/// chord could never dispatch. <see cref="KeyBindingConflictPolicy.Replace"/> does not resolve this: the relationship is a declaration error
	/// rather than a competing claim.
	/// </summary>
	PrefixConflict,

	/// <summary>The overrides store rejected the snapshot; no runtime state was changed.</summary>
	PersistenceFailed,
}
