namespace Nickelony.KeyBindings;

/// <summary>
/// Controls which users may change a command's key bindings.
/// </summary>
/// <remarks>
/// <see cref="HostManaged"/> exists for a command whose binding is supplied per installation through
/// persisted host settings while users may not rebind it, so a loaded override is honored even though a
/// proposal is rejected; <see cref="HostReserved"/> covers a command the host fully owns, where even a
/// loaded override is ignored in favor of the catalog defaults. Both differ from <see cref="Remappable"/>,
/// which accepts proposals.
/// </remarks>
public enum CommandRemappingPolicy
{
	/// <summary>Users may rebind the command; validation accepts proposed binding sets.</summary>
	Remappable,

	/// <summary>
	/// Only the host controls the command's bindings: validation rejects proposed binding sets, but
	/// loaded overrides are honored, so the host supplies the binding through persisted settings.
	/// </summary>
	HostManaged,

	/// <summary>
	/// The command is reserved by the host: validation rejects proposed binding sets and every loaded
	/// override is ignored in favor of the catalog defaults.
	/// </summary>
	HostReserved
}
