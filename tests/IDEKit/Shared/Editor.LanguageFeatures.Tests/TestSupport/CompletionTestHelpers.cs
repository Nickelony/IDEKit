using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
#if AVALONIAEDIT
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>Creates editors, presenters, and completion controllers for tests.</summary>
internal static class CompletionTestHelpers
{
	/// <summary>The document identifier that the controllers of the tests send with each request.</summary>
	internal const string TestDocumentId = "file:///test.lua";

	/// <summary>
	/// The controller options used by most tests: the defaults with a one-millisecond debounce delay so the timer path runs promptly.
	/// </summary>
	internal static readonly CompletionControllerOptions s_fastOptions = CompletionControllerOptions.Default with
	{
		RequestDebounceDelay = TimeSpan.FromMilliseconds(1.0),
	};

	/// <summary>Creates a text editor with the given text.</summary>
	internal static TextEditor CreateEditor(string text = "sample") => TestHost.CreateEditor(text);

	/// <summary>Creates the fixed test theme for completion windows.</summary>
	internal static CompletionWindowTheme CreateTheme()
	{
		return new()
		{
			BorderBrush = Brushes.Gray,
			Background = Brushes.Black,
			Foreground = Brushes.White,
		};
	}

	/// <summary>
	/// Creates the fixed test presentation with the test window theme, the default tooltip theme, a one-millisecond tooltip delay, and the given
	/// window options.
	/// </summary>
	/// <param name="windowOptions">The window options, or <see langword="null"/> for <see cref="CompletionWindowOptions.Default"/>.</param>
	internal static CompletionPresentationOptions CreatePresentation(CompletionWindowOptions? windowOptions = null) =>
		new()
		{
			WindowTheme = CreateTheme(),
			WindowOptions = windowOptions ?? CompletionWindowOptions.Default,
#if !AVALONIAEDIT
			TooltipResolveDelay = TimeSpan.FromMilliseconds(1.0),
#endif
		};

	/// <summary>Creates a window presenter bound to the editor's text area with a fixed test theme.</summary>
	/// <param name="editor">The editor whose text area the presenter serves.</param>
	/// <param name="showWindow">The action that replaces showing a created window, or <see langword="null"/> to show it.</param>
	internal static CompletionWindowPresenter CreateWindowPresenter(TextEditor editor, Action<CompletionWindow>? showWindow = null) =>
		showWindow is null ? new(editor.TextArea, CreateTheme()) : new(editor.TextArea, CreateTheme(), showWindow);

	/// <summary>
	/// Creates a completion presenter and the controller that shows its decisions, with the given options, hooks, logger, and presentation;
	/// the default presentation uses a fresh test theme.
	/// </summary>
	/// <param name="options">The controller options, or <see langword="null"/> for <see cref="s_fastOptions"/>.</param>
	/// <param name="hooks">The presenter hooks, or <see langword="null"/> for none.</param>
	/// <param name="presentation">The presentation, or <see langword="null"/> for <see cref="CreatePresentation"/>.</param>
	/// <param name="provider">The provider, or <see langword="null"/> for one that answers no items.</param>
	/// <param name="controllerHooks">The controller hooks, or <see langword="null"/> for none.</param>
	/// <returns>The controller and its presenter, which the caller disposes together.</returns>
	internal static CompletionSession CreateController(
		TextEditor editor,
		CompletionControllerOptions? options = null,
		CompletionPresenterHooks? hooks = null,
		ILogger? logger = null,
		CompletionPresentationOptions? presentation = null,
		ICompletionProvider? provider = null,
		CompletionControllerHooks? controllerHooks = null
	)
	{
		var completion = new CompletionPresenter(textArea: editor.TextArea, presentation: presentation ?? CreatePresentation(), hooks, logger);

		try
		{
			CompletionController controller = CompletionControllerFactory.Create(
				textArea: editor.TextArea,
				documentId: TestDocumentId,
				provider: provider ?? new ScriptedCompletionProvider(),
				presenter: completion,
				hooks: controllerHooks,
				options: options ?? s_fastOptions,
				logger: logger
			);

			return new CompletionSession(controller, completion);
		}
		catch
		{
			completion.Dispose();
			throw;
		}
	}

	/// <summary>Creates an editor shown in a host window together with its controller and window presenter.</summary>
	/// <param name="options">The controller options, or <see langword="null"/> for <see cref="s_fastOptions"/>.</param>
	/// <param name="presentation">The presentation, or <see langword="null"/> for <see cref="CreatePresentation"/>.</param>
	/// <returns>The controller, window presenter, host window and completion presenter; dispose the host window when the test ends.</returns>
	internal static (
		CompletionController Controller,
		CompletionWindowPresenter Presenter,
		HostWindow HostWindow,
		CompletionPresenter Completion
	) CreateHostedController(
		CompletionPresenterHooks? hooks = null,
		CompletionControllerOptions? options = null,
		ILogger? logger = null,
		string text = "sample",
		CompletionPresentationOptions? presentation = null,
		ICompletionProvider? provider = null,
		CompletionControllerHooks? controllerHooks = null
	)
	{
		(
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) hosted = CreateHostedEditorController(
			hooks,
			options,
			logger,
			text,
			presentation: presentation,
			provider: provider,
			controllerHooks: controllerHooks
		);

		return (hosted.Controller, hosted.Presenter, hosted.HostWindow, hosted.Completion);
	}

	/// <summary>Creates an editor shown in a host window with its controller and presenters, and returns the editor itself.</summary>
	/// <param name="options">The controller options, or <see langword="null"/> for <see cref="s_fastOptions"/>.</param>
	/// <param name="configureEditor">An optional callback that runs after the editor is shown and before the controller is created.</param>
	/// <param name="presentation">The presentation, or <see langword="null"/> for <see cref="CreatePresentation"/>.</param>
	/// <returns>
	/// The editor, controller, window presenter, host window and completion presenter; dispose the host window when the test ends,
	/// and the controller, then the completion presenter, to end the session.
	/// </returns>
	internal static (
		TextEditor Editor,
		CompletionController Controller,
		CompletionWindowPresenter Presenter,
		HostWindow HostWindow,
		CompletionPresenter Completion
	) CreateHostedEditorController(
		CompletionPresenterHooks? hooks = null,
		CompletionControllerOptions? options = null,
		ILogger? logger = null,
		string text = "sample",
		Action<TextEditor>? configureEditor = null,
		CompletionPresentationOptions? presentation = null,
		ICompletionProvider? provider = null,
		CompletionControllerHooks? controllerHooks = null
	)
	{
		TextEditor editor = CreateEditor(text);
		HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		configureEditor?.Invoke(editor);
		CompletionSession session = CreateController(editor, options, hooks, logger, presentation, provider, controllerHooks);

		return (editor, session.Controller, session.Completion.WindowPresenter, hostWindow, session.Completion);
	}

	/// <summary>A completion controller and the presenter that shows its decisions, which dispose in the order the contract asks for.</summary>
	/// <param name="Controller">The controller.</param>
	/// <param name="Completion">The presenter, which the controller never disposes.</param>
	internal readonly record struct CompletionSession(CompletionController Controller, CompletionPresenter Completion) : IDisposable
	{
		/// <summary>Disposes the controller, then the presenter.</summary>
		public void Dispose()
		{
			Controller.Dispose();
			Completion.Dispose();
		}
	}

	/// <summary>Creates a completion data item for tests with the shared wrapping description.</summary>
	/// <param name="description">The item description, or <see langword="null"/> for none.</param>
	internal static StubCompletionData CreateItem(string text, string? description = "A description that must wrap.") => new(text, description);

#if !AVALONIAEDIT
	/// <summary>Resolves the editor's completion tooltip of a window for tooltip-state assertions.</summary>
	internal static ToolTip GetCompletionTooltip(CompletionWindow completionWindow)
	{
		return CompletionTooltipResolver.TryGetTooltip(completionWindow, out ToolTip? tooltip)
			? tooltip
			: throw new InvalidOperationException("The completion window has no tooltip.");
	}
#endif

	/// <summary>
	/// Pumps dispatcher frames until the controller's tooltip resolve timer has run, with a deadline so a broken timer cannot hang the test.
	/// </summary>
	internal static void PumpPastTooltipDebounce(CompletionPresenter completion)
	{
		// The pending flag is the internal test hook the presenter exposes; asserting it before the pump keeps
		// this helper from passing trivially when no update was ever scheduled.
		Assert.IsTrue(completion.IsTooltipUpdatePending, "No tooltip update was scheduled before the debounce was pumped.");
		DispatcherTestHelpers.PumpUntil(() => !completion.IsTooltipUpdatePending);
	}
}
