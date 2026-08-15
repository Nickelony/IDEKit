using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

/// <summary>
/// Creates editors, coordinators, and completion controllers for tests.
/// </summary>
internal static class CompletionTestHost
{
	/// <summary>
	/// Gets the options used by most tests: the defaults with one-millisecond debounce delays so timer paths
	/// run promptly.
	/// </summary>
	internal static readonly TextCompletionControllerOptions FastOptions = TextCompletionControllerOptions.Default with
	{
		RequestDebounceDelay = TimeSpan.FromMilliseconds(1.0),
		TooltipResolveDelay = TimeSpan.FromMilliseconds(1.0)
	};

	/// <summary>
	/// Creates a text editor with the given text.
	/// </summary>
	/// <param name="text">The initial editor text.</param>
	/// <returns>The created editor.</returns>
	internal static TextEditor CreateEditor(string text = "sample") => AvaloniaTestHost.CreateEditor(text);

	/// <summary>
	/// Creates the fixed test skin for completion windows.
	/// </summary>
	/// <returns>The test skin.</returns>
	internal static CompletionWindowSkin CreateSkin() => new() { BorderBrush = Brushes.Gray, Background = Brushes.Black, Foreground = Brushes.White };

	/// <summary>
	/// Creates a coordinator bound to the editor's text area with a fixed test skin.
	/// </summary>
	/// <param name="editor">The editor the coordinator serves.</param>
	/// <returns>The created coordinator.</returns>
	internal static CompletionWindowCoordinator CreateCoordinator(TextEditor editor) => new(
		editor.TextArea,
		CreateSkin());

	/// <summary>
	/// Creates a completion controller with the given options, hooks, and logger for a fresh test skin.
	/// </summary>
	/// <param name="editor">The editor whose text area the controller serves.</param>
	/// <param name="options">The options, or <see langword="null"/> for <see cref="FastOptions"/>.</param>
	/// <param name="hooks">The optional host hooks.</param>
	/// <param name="logger">The optional logger.</param>
	/// <returns>The created controller; its coordinator is <see cref="TextCompletionController.WindowCoordinator"/>.</returns>
	internal static TextCompletionController CreateController(
		TextEditor editor,
		TextCompletionControllerOptions? options = null,
		TextCompletionControllerHooks? hooks = null,
		ILogger? logger = null)
		=> new(editor.TextArea, CreateSkin(), options ?? FastOptions, hooks, logger);

	/// <summary>
	/// Creates an editor shown in a host window together with its controller and window coordinator.
	/// </summary>
	/// <param name="hooks">The optional host hooks.</param>
	/// <param name="options">The options, or <see langword="null"/> for <see cref="FastOptions"/>.</param>
	/// <param name="logger">The optional logger.</param>
	/// <param name="text">The initial editor text.</param>
	/// <returns>The controller, coordinator, and host window; dispose the host window when the test ends.</returns>
	internal static (TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) CreateHostedController(
		TextCompletionControllerHooks? hooks = null,
		TextCompletionControllerOptions? options = null,
		ILogger? logger = null,
		string text = "sample")
	{
		(TextEditor Editor, TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CreateHostedEditorController(hooks, options, logger, text);

		return (hosted.Controller, hosted.Coordinator, hosted.HostWindow);
	}

	/// <summary>
	/// Creates an editor shown in a host window together with its controller and window coordinator and
	/// returns the editor itself, for tests that configure the editor before the controller is created or
	/// inspect it after.
	/// </summary>
	/// <param name="hooks">The optional host hooks.</param>
	/// <param name="options">The options, or <see langword="null"/> for <see cref="FastOptions"/>.</param>
	/// <param name="logger">The optional logger.</param>
	/// <param name="text">The initial editor text.</param>
	/// <param name="configureEditor">
	/// An optional callback that configures the editor after it is shown and before the controller is
	/// created, so input services can be subscribed ahead of the controller's input policy.
	/// </param>
	/// <returns>The editor, controller, coordinator, and host window; dispose the host window when the test ends.</returns>
	internal static (TextEditor Editor, TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) CreateHostedEditorController(
		TextCompletionControllerHooks? hooks = null,
		TextCompletionControllerOptions? options = null,
		ILogger? logger = null,
		string text = "sample",
		Action<TextEditor>? configureEditor = null)
	{
		TextEditor editor = CreateEditor(text);
		HostWindow hostWindow = AvaloniaTestHost.ShowInHostWindow(editor);
		configureEditor?.Invoke(editor);
		TextCompletionController controller = CreateController(editor, options, hooks, logger);

		return (editor, controller, controller.WindowCoordinator, hostWindow);
	}

	/// <summary>
	/// Creates a completion data item for tests with the shared wrapping description.
	/// </summary>
	/// <param name="text">The item text.</param>
	/// <param name="description">The item description, or <see langword="null"/> for none.</param>
	/// <returns>The created item.</returns>
	internal static TestCompletionData CreateItem(string text, string? description = "A description that must wrap.")
		=> new(text, description);

	/// <summary>
	/// Resolves the AvaloniaEdit completion tooltip of a window for tooltip-state assertions.
	/// </summary>
	/// <param name="completionWindow">The completion window whose tooltip is requested.</param>
	/// <returns>The window's tooltip.</returns>
	/// <remarks>
	/// AvaloniaEdit exposes no tooltip member for <see cref="CompletionWindowTooltipAccess.TryGetTooltip"/> to
	/// read (unlike WPF AvalonEdit's reflected private field), so the accessor always reports the tooltip as
	/// unavailable and this helper always throws. Tooltip-aware tests disable that path the same way the
	/// controller does when the reflected field is missing in the WPF engine.
	/// </remarks>
	internal static ToolTip GetCompletionTooltip(CompletionWindow completionWindow)
		=> CompletionWindowTooltipAccess.TryGetTooltip(completionWindow, out ToolTip? tooltip)
			? tooltip
			: throw new InvalidOperationException("The completion window has no tooltip.");

	/// <summary>
	/// Pumps dispatcher frames until the controller's tooltip resolve timer has run, with a deadline so a
	/// broken timer cannot hang the test.
	/// </summary>
	/// <param name="controller">The controller whose tooltip update is awaited.</param>
	internal static void PumpPastTooltipDebounce(TextCompletionController controller)
	{
		// The pending flag is the internal test seam the presenter exposes; asserting it before the pump keeps
		// this helper from passing trivially when no update was ever scheduled.
		Assert.IsTrue(controller.TestHooks.IsTooltipUpdatePending, "No tooltip update was scheduled before the debounce was pumped.");

		DispatcherTestUtils.PumpUntil(() => !controller.TestHooks.IsTooltipUpdatePending);
	}
}
