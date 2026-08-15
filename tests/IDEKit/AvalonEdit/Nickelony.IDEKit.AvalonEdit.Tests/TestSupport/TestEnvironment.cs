using System.Runtime.CompilerServices;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

/// <summary>
/// Process-wide test setup.
/// </summary>
internal static class TestEnvironment
{
	/// <summary>
	/// Disables the WPF UI Automation bridge for items controls before WPF initializes.
	/// </summary>
	/// <remarks>
	/// The automation peers hold native, reference-counted GC handles, so an editor that took real
	/// keyboard or focus input stays reachable after its window closes and breaks GC-reachability tests.
	/// The switches mirror the documented workaround for the same failure.
	/// </remarks>
	[ModuleInitializer]
	internal static void Initialize()
	{
		AppContext.SetSwitch("Switch.System.Windows.Automation.Peers.ItemAutomationPeerKeepsItsItemAlive", false);
		AppContext.SetSwitch("Switch.System.Windows.Controls.ItemsControlDoesNotSupportAutomation", true);
	}
}
