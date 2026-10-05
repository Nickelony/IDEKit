#if AVALONIAEDIT
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>
/// Hosts a code-action controller and presenter over a test editor shown in a host window and records every hook call, so the presenter and margin
/// tests share one host instead of repeating the full hook set.
/// </summary>
/// <remarks>
/// The delegate properties are mutable on purpose: a test starts from the defaults and swaps in the behavior it needs, and the recorder
/// lists observe what the controller and the presenter reported. The controller coordination itself is covered by the controller tests,
/// which run without an editor.
/// </remarks>
internal sealed class CodeActionPresenterTestHost : IDisposable, ICodeActionProvider
{
	/// <summary>The document identifier the requests the host builds carry.</summary>
	internal const string DocumentId = "code-action-test.lua";

	/// <summary>
	/// The options used by most tests: the defaults with a one-millisecond debounce delay so the debounced request path runs promptly.
	/// </summary>
	internal static readonly CodeActionControllerOptions s_fastOptions = CodeActionControllerOptions.Default with
	{
		RequestDebounceDelay = TimeSpan.FromMilliseconds(1.0),
	};

	/// <summary>Initializes a new instance of the <see cref="CodeActionPresenterTestHost"/> class and shows the editor in a host window.</summary>
	/// <param name="text">The initial editor text.</param>
	internal CodeActionPresenterTestHost(string text = "one\ntwo\nthree")
	{
		Editor = TestHost.CreateEditor(text);
		HostWindow = TestHost.ShowInHostWindow(Editor);
	}

	/// <summary>The editor the controller and the presenter serve.</summary>
	internal TextEditor Editor { get; }

	/// <summary>The host window that owns the editor; dispose the host to close it.</summary>
	internal HostWindow HostWindow { get; }

	/// <summary>The requests the controller passed to the provider, in order.</summary>
	internal List<CodeActionRequest> Requests { get; } = [];

	/// <summary>The actions the controller passed to the execute hook, in order.</summary>
	internal List<CodeAction> Executed { get; } = [];

	/// <summary>The menus the presenter created, in order.</summary>
	internal List<ContextMenu> Menus { get; } = [];

	/// <summary>Indicates whether the presenter invoked the menu configuration hook.</summary>
	internal bool ConfigureMenuInvoked { get; private set; }

	/// <summary>Indicates whether the presenter invoked the menu-item configuration hook.</summary>
	internal bool ConfigureMenuItemInvoked { get; private set; }

	/// <summary>Holds the provider behavior, which defaults to an immediately completing empty result.</summary>
	internal Func<CodeActionRequest, CancellationToken, Task<IReadOnlyList<CodeAction>>> RequestCodeActionsAsync { get; set; } =
		static (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([]);

	/// <summary>Holds the execute hook, which defaults to an immediately completing no-op.</summary>
	internal Func<CodeAction, Task> ExecuteActionAsync { get; set; } = static _ => Task.CompletedTask;

	/// <summary>Holds the optional additional menu configuration applied after the recorded one.</summary>
	internal Action<ContextMenu>? ConfigureMenu { get; set; }

	/// <summary>Holds the optional additional menu-item configuration applied after the recorded one.</summary>
	internal Action<MenuItem, CodeAction>? ConfigureMenuItem { get; set; }

	/// <summary>Creates the fixed test theme for actions menus.</summary>
	/// <returns>The test theme.</returns>
	internal static CodeActionMenuTheme CreateTheme()
	{
		return new()
		{
			BorderBrush = Brushes.Gray,
			Background = Brushes.Black,
			Foreground = Brushes.White,
		};
	}

	/// <summary>Creates the fixed test presentation inputs for actions menus.</summary>
	/// <param name="menuOptions">The menu presentation options, or <see langword="null"/> for the defaults.</param>
	/// <returns>The test presentation.</returns>
	internal static CodeActionPresentationOptions CreatePresentation(CodeActionMenuOptions? menuOptions = null) =>
		new() { MenuTheme = CreateTheme(), MenuOptions = menuOptions ?? CodeActionMenuOptions.Default };

	/// <summary>Creates a code-action item for tests.</summary>
	/// <param name="title">The action title.</param>
	/// <param name="isPreferred">Whether the action is preferred.</param>
	/// <param name="kind">The action kind.</param>
	/// <param name="payload">The optional host payload.</param>
	/// <returns>The created item.</returns>
	internal static CodeAction CreateItem(string title, bool isPreferred = false, string? kind = "quickfix", object? payload = null) =>
		new(title, kind: kind, isPreferred: isPreferred, data: payload);

	/// <summary>Runs an asynchronously completing controller operation to completion without blocking the dispatcher.</summary>
	/// <param name="task">The operation to complete.</param>
	internal static void RunToCompletion(Task task)
	{
		DispatcherTestHelpers.PumpUntil(() => task.IsCompleted);
		task.GetAwaiter().GetResult();
	}

	/// <inheritdoc/>
	public async Task<IReadOnlyList<CodeAction>> GetCodeActionsAsync(CodeActionRequest request, CancellationToken cancellationToken = default)
	{
		Requests.Add(request);
		return await RequestCodeActionsAsync(request, cancellationToken).ConfigureAwait(true);
	}

	/// <summary>Creates a controller over the editor through the factory, wired to this host's hooks and provider.</summary>
	/// <param name="options">The options, or <see langword="null"/> for <see cref="s_fastOptions"/>.</param>
	/// <param name="logger">The optional logger.</param>
	/// <returns>The created controller.</returns>
	internal CodeActionController CreateController(CodeActionControllerOptions? options = null, ILogger? logger = null)
	{
		return CodeActionControllerFactory.Create(
			textArea: Editor.TextArea,
			provider: this,

			hooks: new CodeActionControllerHooks(
				BuildRequest: static context => new CodeActionRequest(DocumentId, context.GetText(), context.Range),

				ExecuteActionAsync: (item, _) =>
				{
					Executed.Add(item);
					return ExecuteActionAsync(item);
				}
			),

			options: options ?? s_fastOptions,
			logger
		);
	}

	/// <summary>Creates a presenter over the editor for <paramref name="controller"/>, wired to this host's menu hooks.</summary>
	/// <param name="controller">The controller whose actions the presenter shows.</param>
	/// <param name="menuOptions">The menu presentation options, or <see langword="null"/> for the defaults.</param>
	/// <returns>The created presenter.</returns>
	internal CodeActionPresenter CreatePresenter(CodeActionController controller, CodeActionMenuOptions? menuOptions = null)
	{
		return new CodeActionPresenter(
			textArea: Editor.TextArea,
			controller,
			presentation: CreatePresentation(menuOptions),

			hooks: new CodeActionMenuHooks
			{
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
				},
			}
		);
	}

	/// <summary>Creates a controller over an editor that is not connected to a presentation source.</summary>
	/// <param name="editor">The editor to serve.</param>
	/// <param name="requestCodeActionsAsync">The optional provider behavior; defaults to an empty result.</param>
	/// <returns>The created controller.</returns>
	internal static CodeActionController CreateUnhostedController(
		TextEditor editor,
		Func<CodeActionRequest, CancellationToken, Task<IReadOnlyList<CodeAction>>>? requestCodeActionsAsync = null
	)
	{
		return CodeActionControllerFactory.Create(
			textArea: editor.TextArea,
			provider: new DelegateCodeActionProvider(requestCodeActionsAsync),
			options: s_fastOptions,

			hooks: new CodeActionControllerHooks(
				BuildRequest: static context => new CodeActionRequest(DocumentId, context.GetText(), context.Range),
				ExecuteActionAsync: static (_, _) => Task.CompletedTask
			)
		);
	}

	/// <inheritdoc/>
	public void Dispose() => HostWindow.Close();

	/// <summary>A provider that answers every request with the supplied behavior, or with an empty result when none is supplied.</summary>
	private sealed class DelegateCodeActionProvider(
		Func<CodeActionRequest, CancellationToken, Task<IReadOnlyList<CodeAction>>>? requestCodeActionsAsync
	) : ICodeActionProvider
	{
		public async Task<IReadOnlyList<CodeAction>> GetCodeActionsAsync(CodeActionRequest request, CancellationToken cancellationToken = default)
		{
			if (requestCodeActionsAsync is null)
				return [];

			return await requestCodeActionsAsync(request, cancellationToken).ConfigureAwait(true);
		}
	}
}
