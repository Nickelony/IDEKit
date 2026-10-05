namespace Nickelony.IDEKit.Core.Threading;

/// <summary>Identifies the priority at which a neutral UI dispatch callback is queued.</summary>
/// <remarks>
/// The set is the intersection shared editor code needs; an editor binding maps each member to the closest priority its toolkit's dispatcher defines,
/// so the exact numeric value is not part of the contract.
/// </remarks>
public enum UiDispatcherPriority
{
	/// <summary>Runs after input work and before ContextIdle work queued at the same time.</summary>
	Background,

	/// <summary>Runs after input and layout but before the dispatcher goes idle.</summary>
	ContextIdle,

	Normal,

	/// <summary>Runs at the layout and rendering level, in the order the work was queued.</summary>
	Render,
}
