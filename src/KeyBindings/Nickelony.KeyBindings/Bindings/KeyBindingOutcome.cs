namespace Nickelony.KeyBindings;

/// <summary>
/// Describes the result of a key binding validation or mutation operation:
/// <see cref="IKeyBindingService{TCommandId}.Validate(TCommandId, System.Collections.Generic.IReadOnlyList{KeyChord}, KeyBindingConflictPolicy)"/>,
/// <see cref="IKeyBindingService{TCommandId}.Apply"/>, <see cref="IKeyBindingService{TCommandId}.Reset"/>, or
/// <see cref="IKeyBindingService{TCommandId}.ResetAll"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Unspecified"/> is the zero value, so it is what an unassigned variable or a
/// <see langword="default"/> value reads as. It deliberately does not read as
/// <see cref="Succeeded"/>, and no operation returns it, so a consumer can treat it as "no result
/// was assigned" rather than as an outcome it has to handle.
/// </para>
/// <para>
/// A mutation reports the outcome of the binding set it produces, not only of the set it was asked
/// to store: a <see cref="Conflict"/> or <see cref="PrefixConflict"/> can describe a binding another
/// command keeps, or regains, when the operation removes or restores an entry.
/// </para>
/// </remarks>
public enum KeyBindingOutcome
{
	/// <summary>
	/// No outcome has been assigned. This is the documented default value, so an unassigned value
	/// never reads as <see cref="Succeeded"/>; no operation returns it.
	/// </summary>
	Unspecified,

	/// <summary>The proposed binding set is valid, or the requested change was applied.</summary>
	Succeeded,

	/// <summary>The command is not present in the catalog.</summary>
	UnknownCommand,

	/// <summary>
	/// The command is <see cref="CommandRemappingPolicy.HostManaged"/>: proposed binding sets are
	/// rejected, while loaded overrides are still honored.
	/// </summary>
	NotRemappable,

	/// <summary>The command is <see cref="CommandRemappingPolicy.HostReserved"/> and cannot be rebound.</summary>
	Reserved,

	/// <summary>The proposed binding set contains the same key chord more than once.</summary>
	DuplicateInCommand,

	/// <summary>
	/// A chord is assigned to another command, through a catalog default or an override entry, and the
	/// operation cannot take it over: the request does not allow it, the owning command's remapping
	/// policy forbids it, a preserved entry claims it, or restoring a catalog default would collide
	/// with a binding another command holds.
	/// </summary>
	Conflict,

	/// <summary>
	/// A chord stands in a strict prefix relationship with another co-active bound chord, in either
	/// direction, so the shorter chord could never reach the longer one.
	/// <see cref="KeyBindingConflictPolicy.Replace"/> deliberately does not resolve this: the
	/// relationship is a declaration error rather than a competing claim, and silently removing the
	/// other command's binding to "fix" it would be wrong.
	/// </summary>
	PrefixConflict,

	/// <summary>The overrides store rejected the snapshot; no runtime state was changed.</summary>
	PersistenceFailed
}
