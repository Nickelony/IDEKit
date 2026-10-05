using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>
/// Builds a code-action controller over a fake selection source and records every hook call, so code-action tests share one host instead of repeating
/// the full hook set. The host is the provider the controller asks, and it owns the dispatcher the controller runs on.
/// </summary>
/// <remarks>
/// The delegate properties are mutable on purpose: a test starts from the defaults and swaps in the behavior it needs, and the recorder lists observe
/// what the controller reported. The host disposes its dispatcher, which restores the thread's synchronization context.
/// </remarks>
internal sealed class CodeActionTestHost : IDisposable, ICodeActionProvider
{
	/// <summary>The document identifier the requests the host builds carry.</summary>
	internal const string DocumentId = "code-action-test.lua";

	/// <summary>
	/// The options used by most tests: the defaults with a one-millisecond debounce delay, which a test advances past with the virtual clock.
	/// </summary>
	internal static readonly CodeActionControllerOptions s_fastOptions = CodeActionControllerOptions.Default with
	{
		RequestDebounceDelay = TimeSpan.FromMilliseconds(1.0),
	};

	/// <param name="installSynchronizationContext">
	/// Whether the dispatcher is the synchronization context of the test thread, as the dispatcher of a running editor is. A test that needs
	/// a continuation to resume on the thread pool passes <see langword="false"/>.
	/// </param>
	internal CodeActionTestHost(bool installSynchronizationContext = true)
	{
		Dispatcher = new FakePumpedDispatcher(installSynchronizationContext);
		Source = new FakeCodeActionSelectionSource(Dispatcher);
	}

	/// <summary>The dispatcher the controller runs on; the test pumps it.</summary>
	internal FakePumpedDispatcher Dispatcher { get; }

	/// <summary>The selection source the controller reads; a test scripts the document, the caret, and the selection through it.</summary>
	internal FakeCodeActionSelectionSource Source { get; }

	/// <summary>The contexts the controller asked the request builder about, in order.</summary>
	internal List<CodeActionSelection> Contexts { get; } = [];

	/// <summary>The requests the controller passed to the provider, in order.</summary>
	internal List<CodeActionRequest> Requests { get; } = [];

	/// <summary>The cancellation tokens the controller passed to the provider, in order.</summary>
	internal List<CancellationToken> RequestTokens { get; } = [];

	/// <summary>The actions the controller passed to the execute hook, in order.</summary>
	internal List<CodeAction> Executed { get; } = [];

	/// <summary>The tokens the controller passed to the execute hook, in order.</summary>
	internal List<CancellationToken> ExecuteTokens { get; } = [];

	/// <summary>Holds the request builder hook, which defaults to the selection range, or the caret offset when the selection is empty.</summary>
	internal Func<CodeActionSelection, CodeActionRequest?> BuildRequest { get; set; } =
		static context => new CodeActionRequest(DocumentId, context.GetText(), context.Range);

	/// <summary>Holds the provider behavior, which defaults to an immediately completing empty result.</summary>
	internal Func<CodeActionRequest, CancellationToken, Task<IReadOnlyList<CodeAction>>> RequestCodeActionsAsync { get; set; } =
		static (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([]);

	/// <summary>Holds the execute hook, which defaults to an immediately completing no-op.</summary>
	internal Func<CodeAction, Task> ExecuteActionAsync { get; set; } = static _ => Task.CompletedTask;

	/// <summary>Creates a code-action item for tests.</summary>
	/// <param name="title">The action title.</param>
	/// <param name="isPreferred">Whether the action is preferred.</param>
	/// <param name="kind">The action kind.</param>
	/// <param name="payload">The optional host payload.</param>
	/// <returns>The created item.</returns>
	internal static CodeAction CreateItem(string title, bool isPreferred = false, string? kind = "quickfix", object? payload = null) =>
		new(title, kind: kind, isPreferred: isPreferred, data: payload);

	/// <summary>Creates a provider result of one default item, the shape most tests publish.</summary>
	/// <returns>The result.</returns>
	internal static Task<IReadOnlyList<CodeAction>> OneItem() => Task.FromResult<IReadOnlyList<CodeAction>>([CreateItem("Fix it")]);

	/// <inheritdoc/>
	public async Task<IReadOnlyList<CodeAction>> GetCodeActionsAsync(CodeActionRequest request, CancellationToken cancellationToken = default)
	{
		Requests.Add(request);
		RequestTokens.Add(cancellationToken);

		return await RequestCodeActionsAsync(request, cancellationToken).ConfigureAwait(true);
	}

	/// <summary>Creates a controller wired to this host's source, hooks, provider and dispatcher.</summary>
	/// <param name="options">The options, or <see langword="null"/> for <see cref="s_fastOptions"/>.</param>
	/// <param name="logger">The optional logger.</param>
	/// <param name="provider">
	/// The provider the controller asks, or <see langword="null"/> for this host, which offers actions and nothing else.
	/// </param>
	/// <returns>The created controller.</returns>
	internal CodeActionController CreateController(
		CodeActionControllerOptions? options = null,
		ILogger? logger = null,
		ICodeActionProvider? provider = null
	) => new(selectionSource: Source, provider: provider ?? this, hooks: CreateHooks(), Dispatcher, options: options ?? s_fastOptions, logger);

	/// <summary>Creates the hook set that records into this host.</summary>
	/// <returns>The hooks.</returns>
	internal CodeActionControllerHooks CreateHooks()
	{
		return new CodeActionControllerHooks(
			BuildRequest: context =>
			{
				Contexts.Add(context);
				return BuildRequest(context);
			},

			ExecuteActionAsync: (item, token) =>
			{
				Executed.Add(item);
				ExecuteTokens.Add(token);

				return ExecuteActionAsync(item);
			}
		);
	}

	/// <summary>Moves the virtual clock past the debounce delay of <see cref="s_fastOptions"/>, so a scheduled request runs.</summary>
	internal void SettleDebounce() => Dispatcher.Advance(TimeSpan.FromMilliseconds(5.0));

	/// <summary>Runs an asynchronously completing controller operation to completion by pumping the dispatcher.</summary>
	/// <param name="task">The operation to complete.</param>
	internal void RunToCompletion(Task task)
	{
		Dispatcher.PumpUntil(() => task.IsCompleted);
		task.GetAwaiter().GetResult();
	}

	/// <summary>Disposes the dispatcher, which restores the synchronization context it installed.</summary>
	public void Dispose() => Dispatcher.Dispose();
}
