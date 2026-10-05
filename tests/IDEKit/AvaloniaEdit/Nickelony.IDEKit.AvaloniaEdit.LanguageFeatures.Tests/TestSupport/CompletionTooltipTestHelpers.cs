using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

/// <summary>Provides the Avalonia half of the completion tooltip helpers that the shared completion tests call.</summary>
/// <remarks>
/// The AvaloniaEdit completion window exposes no styleable tooltip, so the helpers configure and cancel nothing.
/// </remarks>
internal static class CompletionTooltipTestHelpers
{
	/// <summary>Creates hooks that, where the engine has a tooltip, resolve each selected item's description.</summary>
	/// <param name="onResolve">The action that runs for each description resolution, which never runs here.</param>
	/// <returns>The hooks, without a description hook.</returns>
	internal static CompletionPresenterHooks CreateCountingHooks(Action onResolve) => new();

	/// <summary>Creates the hooks that the wiring smoke tests pass: the editor's font and the item text.</summary>
	/// <param name="editor">The editor whose font reaches the completion list.</param>
	/// <returns>The hooks, without a description hook.</returns>
	/// <remarks>
	/// The editor hosts the completion list in a popup, which carries no font of its own, so the editor's font is applied to the completion list
	/// instead of the window.
	/// </remarks>
	internal static CompletionPresenterHooks CreateWiringHooks(TextEditor editor)
	{
		return new()
		{
			ConfigureWindow = window => CompletionWindowTestHelpers.GetFrameElement(window).FontSize = editor.FontSize,
			GetDisplayInfo = item => (item.Text, null),
		};
	}

	/// <summary>Cancels the tooltip update that the presenter scheduled.</summary>
	/// <param name="completion">The completion presenter.</param>
	internal static void CancelPendingUpdate(CompletionPresenter completion) { }

	/// <summary>Creates a presentation with the default window theme.</summary>
	/// <returns>The presentation options.</returns>
	internal static CompletionPresentationOptions CreateThemedPresentation() => new() { WindowTheme = CompletionWindowTheme.Default };

	/// <summary>Opens the tooltip of the window with content that describes an earlier selection.</summary>
	/// <param name="window">The completion window.</param>
	/// <returns>An action that asserts the stale content is gone, which has nothing to assert here.</returns>
	internal static Action ArmStaleTooltip(CompletionWindow window) => static () => { };
}
