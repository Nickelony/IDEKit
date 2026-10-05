using System.Reflection;
using Avalonia.Controls;

namespace Nickelony.Testing;

/// <summary>Makes a test window report itself active or inactive, the way the platform does when the window gains or loses the foreground.</summary>
/// <remarks>
/// The headless platform activates a window when it shows it and never deactivates one, not even for another window it activates, so the helpers
/// call the handlers that a desktop platform reaches. They run no dispatcher work, so a test can change the activation between a posted callback
/// and its run.
/// </remarks>
internal static class TestWindowActivation
{
	private static readonly MethodInfo s_handleActivated = HandlerOf("HandleActivated");
	private static readonly MethodInfo s_handleDeactivated = HandlerOf("HandleDeactivated");

	/// <summary>Makes the window report itself active, as it does when it takes the foreground.</summary>
	/// <param name="window">The window to activate.</param>
	public static void Activate(Window window)
	{
		s_handleActivated.Invoke(window, null);
		Assert.IsTrue(window.IsActive);
	}

	/// <summary>Makes the window report itself inactive, as it does when another application takes the foreground.</summary>
	/// <param name="window">The window to deactivate.</param>
	public static void Deactivate(Window window)
	{
		s_handleDeactivated.Invoke(window, null);
		Assert.IsFalse(window.IsActive);
	}

	private static MethodInfo HandlerOf(string name)
	{
		return typeof(WindowBase).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
			?? throw new InvalidOperationException($"WindowBase declares no {name} method.");
	}
}
