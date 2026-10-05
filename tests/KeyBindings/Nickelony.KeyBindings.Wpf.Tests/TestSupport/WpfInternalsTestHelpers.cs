using System.Reflection;
using System.Windows.Input;

namespace Nickelony.KeyBindings.Wpf.Tests;

/// <summary>
/// Reaches the private WPF framework members that have no public setter, so a routed key event can be shaped the way a real keyboard produces it.
/// </summary>
/// <remarks>
/// This is the single home of the suite's WPF reflection. <see cref="KeyEventArgs"/> exposes <see cref="KeyEventArgs.IsRepeat"/> and the system-key
/// shape as read-only state, and only the internal <c>SetRepeat</c>/<c>MarkSystem</c> members can set them.
/// </remarks>
internal static class WpfInternalsTestHelpers
{
	/// <summary>Marks a synthesized key event as an auto-repeat.</summary>
	internal static void MarkRepeat(KeyEventArgs args) => GetInternalMethod("SetRepeat").Invoke(args, [true]);

	/// <summary>Marks a synthesized key event as a system key, so its real key is reported through <see cref="KeyEventArgs.SystemKey"/>.</summary>
	internal static void MarkSystem(KeyEventArgs args) => GetInternalMethod("MarkSystem").Invoke(args, null);

	private static MethodInfo GetInternalMethod(string name)
	{
		MethodInfo? method = typeof(KeyEventArgs).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);

		Assert.IsNotNull(method, $"WPF's KeyEventArgs.{name} internals changed.");

		return method;
	}
}
