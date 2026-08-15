using Avalonia.Controls;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

/// <summary>
/// Keeps a completion window from taking focus when the user clicks the completion list.
/// </summary>
/// <remarks>
/// <para>
/// The shared completion controller installs the non-activation policy through one call in both bindings: it
/// calls <see cref="MakeNonActivatable"/> once, before it shows a completion window.
/// </para>
/// <para>
/// An AvaloniaEdit completion window is a <c>Popup</c> hosted inside the editor's own window, so a click in
/// the list cannot activate a separate top-level window and take focus from the editor. No platform hook is
/// needed or available, so the member only validates its argument; the completion list already installs its
/// own pointer-accept handling.
/// </para>
/// </remarks>
internal static class CompletionWindowInterop
{
	/// <summary>
	/// Keeps the given completion window from taking focus when it is clicked.
	/// </summary>
	/// <remarks>
	/// A deliberate no-op beyond its null check, because the Avalonia completion window does not activate the
	/// parent window. The shared call site runs before the window is shown, which is where the WPF binding
	/// installs its platform hook.
	/// </remarks>
	/// <param name="control">The completion window to make non-activatable.</param>
	/// <exception cref="ArgumentNullException"><paramref name="control"/> is <see langword="null"/>.</exception>
	internal static void MakeNonActivatable(Control control)
		=> ArgumentNullException.ThrowIfNull(control);
}
