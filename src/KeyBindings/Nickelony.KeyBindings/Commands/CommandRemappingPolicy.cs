namespace Nickelony.KeyBindings;

/// <summary>Controls which users may change a command's key bindings.</summary>
public enum CommandRemappingPolicy
{
	/// <summary>Users may rebind the command; validation accepts proposed binding sets.</summary>
	Remappable,

	/// <summary>
	/// Only the host controls the command's bindings: validation rejects proposed binding sets, but loaded overrides are honored, so the host
	/// supplies the binding through persisted settings.
	/// </summary>
	HostManaged,

	/// <summary>
	/// The command is reserved by the host: validation rejects proposed binding sets and every loaded override is ignored in favor
	/// of the catalog defaults.
	/// </summary>
	HostReserved,
}
