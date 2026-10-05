using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

/// <summary>Provides the WPF half of the completion tooltip helpers that the shared completion tests call.</summary>
internal static class CompletionTooltipTestHelpers
{
	/// <summary>Creates hooks that resolve each selected item's description to a fixed text.</summary>
	/// <param name="onResolve">The action that runs for each description resolution.</param>
	/// <returns>The hooks.</returns>
	internal static CompletionPresenterHooks CreateCountingHooks(Action onResolve)
	{
		return new()
		{
			ResolveDescriptionAsync = (_, _) =>
			{
				onResolve();
				return Task.FromResult<object?>("resolved description");
			},
		};
	}

	/// <summary>Creates the hooks that the wiring smoke tests pass: the editor's font, the item text, and the item's own description.</summary>
	/// <param name="editor">The editor whose font reaches the completion window.</param>
	/// <returns>The hooks.</returns>
	internal static CompletionPresenterHooks CreateWiringHooks(TextEditor editor)
	{
		return new()
		{
			ConfigureWindow = window => CompletionWindowTestHelpers.GetFrameElement(window).FontSize = editor.FontSize,
			GetDisplayInfo = item => (item.Text, null),
			ResolveDescriptionAsync = (item, _) => Task.FromResult<object?>(item.Description),
		};
	}

	/// <summary>Cancels the tooltip update that the presenter scheduled.</summary>
	/// <param name="completion">The completion presenter.</param>
	internal static void CancelPendingUpdate(CompletionPresenter completion) => completion.CancelTooltipUpdate();

	/// <summary>Creates a presentation with the default window theme and a dark tooltip theme.</summary>
	/// <returns>The presentation options.</returns>
	internal static CompletionPresentationOptions CreateThemedPresentation()
	{
		return new()
		{
			WindowTheme = CompletionWindowTheme.Default,
			TooltipTheme = CompletionTooltipTheme.Default with { Background = Brushes.Black, BorderBrush = Brushes.Gray },
		};
	}

	/// <summary>Opens the tooltip of the window with content that describes an earlier selection.</summary>
	/// <param name="window">The completion window.</param>
	/// <returns>An action that asserts the stale content is gone.</returns>
	/// <remarks>
	/// AvalonEdit's own selection handler can reopen the tooltip for the new selection, so the invariant is that the stale content is gone,
	/// not that nothing is open.
	/// </remarks>
	internal static Action ArmStaleTooltip(CompletionWindow window)
	{
		ToolTip tooltip = CompletionTooltipResolver.TryGetTooltip(window, out ToolTip? found)
			? found
			: throw new InvalidOperationException("The completion window has no tooltip.");

		var staleContent = new TextBlock { Text = "old tooltip" };
		tooltip.Content = staleContent;
		tooltip.IsOpen = true;

		return () =>
			Assert.IsFalse(
				tooltip.IsOpen && ReferenceEquals(staleContent, tooltip.Content),
				"The stale tooltip of the previous selection survived the refresh."
			);
	}
}
