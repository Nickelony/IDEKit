#if AVALONIAEDIT
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.IntelliSense.CodeActions;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using ICSharpCode.AvalonEdit;
using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.IntelliSense.CodeActions;
using System.Windows.Controls;
using System.Windows.Media;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

/// <summary>
/// Hosts a code-action controller over a test editor and records every hook call, so code-action tests
/// share one host instead of repeating the full hook set.
/// </summary>
/// <remarks>
/// The delegate properties are mutable on purpose: a test starts from the defaults and swaps in the
/// behavior it needs, and the recorder lists observe what the controller reported.
/// </remarks>
internal sealed class CodeActionTestHost : IDisposable
{
	/// <summary>
	/// Gets the options used by most tests: the defaults with a one-millisecond debounce delay so the
	/// debounced request path runs promptly.
	/// </summary>
	internal static readonly TextCodeActionControllerOptions FastOptions = TextCodeActionControllerOptions.Default with
	{
		RequestDebounceDelay = TimeSpan.FromMilliseconds(1.0)
	};

	/// <summary>
	/// Initializes a new instance of the <see cref="CodeActionTestHost"/> class and shows the editor in
	/// a host window.
	/// </summary>
	/// <param name="text">The initial editor text.</param>
	internal CodeActionTestHost(string text = "one\ntwo\nthree")
	{
		Editor = TestHost.CreateEditor(text);
		HostWindow = TestHost.ShowInHostWindow(Editor);
	}

	/// <summary>Gets the editor the controller serves.</summary>
	internal TextEditor Editor { get; }

	/// <summary>Gets the host window that owns the editor; dispose the host to close it.</summary>
	internal HostWindow HostWindow { get; }

	/// <summary>Gets the contexts the controller asked the state builder about, in order.</summary>
	internal List<TextCodeActionContext> Contexts { get; } = [];

	/// <summary>Gets the request states the controller passed to the request hook, in order.</summary>
	internal List<TextCodeActionRequest> Requests { get; } = [];

	/// <summary>Gets the cancellation tokens the controller passed to the request hook, in order.</summary>
	internal List<CancellationToken> RequestTokens { get; } = [];

	/// <summary>Gets the actions the controller passed to the execute hook, in order.</summary>
	internal List<TextCodeActionItem> Executed { get; } = [];

	/// <summary>Gets the menus the controller created, in order.</summary>
	internal List<ContextMenu> Menus { get; } = [];

	/// <summary>Gets a value indicating whether the controller invoked the menu configuration hook.</summary>
	internal bool ConfigureMenuInvoked { get; private set; }

	/// <summary>Gets a value indicating whether the controller invoked the menu-item configuration hook.</summary>
	internal bool ConfigureMenuItemInvoked { get; private set; }

	/// <summary>
	/// Gets or sets the state builder hook. Defaults to the selection range, or the caret offset when
	/// the selection is empty.
	/// </summary>
	internal Func<TextCodeActionContext, TextCodeActionRequest?> BuildRequest { get; set; } = static context =>
		new TextCodeActionRequest(context.DocumentText, context.SelectionStartOffset, context.SelectionEndOffset);

	/// <summary>Gets or sets the request hook. Defaults to an immediately completing empty result.</summary>
	internal Func<TextCodeActionRequest, CancellationToken, Task<IReadOnlyList<TextCodeActionItem>>> RequestCodeActionsAsync { get; set; } =
		static (_, _) => Task.FromResult<IReadOnlyList<TextCodeActionItem>>([]);

	/// <summary>Gets or sets the execute hook. Defaults to an immediately completing no-op.</summary>
	internal Func<TextCodeActionItem, Task> ExecuteActionAsync { get; set; } = static _ => Task.CompletedTask;

	/// <summary>Gets or sets the optional additional menu configuration applied after the recorded one.</summary>
	internal Action<ContextMenu>? ConfigureMenu { get; set; }

	/// <summary>Gets or sets the optional additional menu-item configuration applied after the recorded one.</summary>
	internal Action<MenuItem, TextCodeActionItem>? ConfigureMenuItem { get; set; }

	/// <summary>Creates the fixed test skin for actions menus.</summary>
	/// <returns>The test skin.</returns>
	internal static TextCodeActionMenuSkin CreateSkin() => new() { BorderBrush = Brushes.Gray, Background = Brushes.Black, Foreground = Brushes.White };

	/// <summary>Creates the fixed test presentation inputs for actions menus.</summary>
	/// <param name="menuOptions">The menu presentation options, or <see langword="null"/> for the defaults.</param>
	/// <returns>The test presentation.</returns>
	internal static TextCodeActionPresentation CreatePresentation(TextCodeActionMenuOptions? menuOptions = null)
		=> new() { Skin = CreateSkin(), MenuOptions = menuOptions ?? TextCodeActionMenuOptions.Default };

	/// <summary>Creates a code-action item for tests.</summary>
	/// <param name="title">The action title.</param>
	/// <param name="isPreferred">Whether the action is preferred.</param>
	/// <param name="kind">The action kind.</param>
	/// <param name="payload">The optional host payload.</param>
	/// <returns>The created item.</returns>
	internal static TextCodeActionItem CreateItem(string title, bool isPreferred = false, string? kind = "quickfix", object? payload = null)
		=> new(title, kind, isPreferred, payload);

	/// <summary>Creates a controller wired to this host's hooks.</summary>
	/// <param name="options">The options, or <see langword="null"/> for <see cref="FastOptions"/>.</param>
	/// <param name="menuOptions">The menu presentation options, or <see langword="null"/> for the defaults.</param>
	/// <param name="logger">The optional logger.</param>
	/// <returns>The created controller.</returns>
	internal TextCodeActionController CreateController(
		TextCodeActionControllerOptions? options = null,
		TextCodeActionMenuOptions? menuOptions = null,
		ILogger? logger = null)
		=> new(
			Editor.TextArea,
			CreatePresentation(menuOptions),
			hooks: new TextCodeActionControllerHooks
			{
				BuildRequest = context =>
				{
					Contexts.Add(context);
					return BuildRequest(context);
				},
				RequestCodeActionsAsync = (state, cancellationToken) =>
				{
					Requests.Add(state);
					RequestTokens.Add(cancellationToken);
					return RequestCodeActionsAsync(state, cancellationToken);
				},
				ExecuteActionAsync = item =>
				{
					Executed.Add(item);
					return ExecuteActionAsync(item);
				},
				ConfigureMenu = menu =>
				{
					ConfigureMenuInvoked = true;
					Menus.Add(menu);
					ConfigureMenu?.Invoke(menu);
				},
				ConfigureMenuItem = (menuItem, item) =>
				{
					ConfigureMenuItemInvoked = true;
					ConfigureMenuItem?.Invoke(menuItem, item);
				}
			},
			options: options ?? FastOptions,
			logger: logger);

	/// <summary>Creates a controller over an editor that is not connected to a presentation source.</summary>
	/// <param name="editor">The editor to serve.</param>
	/// <param name="requestCodeActionsAsync">The optional request hook; defaults to an empty result.</param>
	/// <returns>The created controller.</returns>
	internal static TextCodeActionController CreateUnhostedController(
		TextEditor editor,
		Func<TextCodeActionRequest, CancellationToken, Task<IReadOnlyList<TextCodeActionItem>>>? requestCodeActionsAsync = null)
		=> new(
			editor.TextArea,
			CreatePresentation(),
			options: FastOptions,
			hooks: new TextCodeActionControllerHooks
			{
				BuildRequest = static context => new TextCodeActionRequest(
					context.DocumentText, context.SelectionStartOffset, context.SelectionEndOffset),
				RequestCodeActionsAsync = requestCodeActionsAsync
					?? (static (_, _) => Task.FromResult<IReadOnlyList<TextCodeActionItem>>([])),
				ExecuteActionAsync = static _ => Task.CompletedTask
			});

	/// <summary>
	/// Runs an asynchronously completing controller operation to completion without blocking the
	/// dispatcher, using the shared dispatcher-pumping pattern of the controller tests.
	/// </summary>
	/// <param name="task">The operation to complete.</param>
	internal static void RunToCompletion(Task task)
	{
		DispatcherTestUtils.PumpUntil(() => task.IsCompleted);
		task.GetAwaiter().GetResult();
	}

	/// <inheritdoc/>
	public void Dispose() => HostWindow.Close();
}
