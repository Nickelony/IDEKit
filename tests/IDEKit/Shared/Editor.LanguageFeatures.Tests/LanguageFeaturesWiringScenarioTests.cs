#if AVALONIAEDIT
using Avalonia.Media;
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.Diagnostics;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Hover;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.SemanticTokens;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.SignatureHelp;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;
using Nickelony.IDEKit.LanguageFeatures.Controllers.SignatureHelp;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.Diagnostics;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Hover;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.SemanticTokens;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.SignatureHelp;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;
using Nickelony.IDEKit.LanguageFeatures.Controllers.SignatureHelp;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>Constructs the editor subsystems through their public API, so a breaking change fails to compile here.</summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class LanguageFeaturesWiringScenarioTests
{
	/// <summary>Constructs every subsystem through its public API, so a breaking change to the construction stops compiling here.</summary>
	/// <remarks>
	/// The behavioral coverage of each subsystem lives in its own suite; this method only checks that the wiring constructs.
	/// </remarks>
	[TestMethod]
	public void Wiring_ConstructsEverySubsystem()
	{
		var editor = new TextEditor { Text = "sample" };
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var provider = new EmptyLanguageFeatureProvider();

		CompletionPresenterHooks completionHooks = CompletionTooltipTestHelpers.CreateWiringHooks(editor);

		using var completion = new CompletionPresenter(editor.TextArea, CompletionTooltipTestHelpers.CreateThemedPresentation(), completionHooks);

		using CompletionController completionController = CompletionControllerFactory.Create(
			textArea: editor.TextArea,
			documentId: "sample.txt",
			provider,
			presenter: completion
		);

		using HoverController hover = HoverControllerFactory.Create(
			editor.TextArea,
			provider,

			new HoverControllerHooks
			{
				BuildDecision = offset => new HoverDecision
				{
					ShouldRequestHover = true,
					RequestOffset = offset,
					CanShowHoverContent = true,
					CanShowDiagnosticFallback = false,
					Diagnostic = null,
				},

				BuildRequest = offset => new HoverRequest("sample.txt", editor.Text, offset),
				ShowHover = (hoverInfo, diagnosticInfo, _) => { },
			}
		);

		using SignatureHelpController signatures = SignatureHelpControllerFactory.Create(
			editor.TextArea,
			provider,

			new SignatureHelpControllerHooks(
				BuildRequest: (offset, context) =>
					new SignatureHelpRequest(documentId: "sample.txt", documentText: editor.Text, caretOffset: offset, context),

				ShowSignatureHelp: static (_, _) => { },
				DismissSignatureHelp: static () => { }
			)
		);

		using CodeActionController codeActions = CodeActionControllerFactory.Create(
			editor.TextArea,
			provider,
			new CodeActionControllerHooks(BuildRequest: static _ => null, ExecuteActionAsync: static (_, _) => Task.CompletedTask)
		);

		using var codeActionPresenter = new CodeActionPresenter(
			editor.TextArea,
			codeActions,
			presentation: new CodeActionPresentationOptions { MenuTheme = CodeActionMenuTheme.Default }
		);

		editor.TextArea.LeftMargins.Add(codeActionPresenter.Margin);

		var colorizer = new SemanticTokensColorizer(editor.TextArea.TextView, new StubSemanticTokenStyleResolver());
		editor.TextArea.TextView.LineTransformers.Add(colorizer);

		// The controller installs its renderer in the text view, and its disposal removes it again.
		using (
			DiagnosticsController diagnostics = DiagnosticsControllerFactory.Create(
				textArea: editor.TextArea,
				documentId: "sample.txt",
				provider,
				source: null
			)
		)
		{
			Assert.ContainsSingle(editor.TextArea.TextView.BackgroundRenderers.OfType<DiagnosticsRenderer>());
		}

		Assert.IsEmpty(editor.TextArea.TextView.BackgroundRenderers.OfType<DiagnosticsRenderer>());

		editor.TextArea.LeftMargins.Remove(codeActionPresenter.Margin);
		editor.TextArea.TextView.LineTransformers.Remove(colorizer);
	}

	/// <summary>One provider that answers every feature contract the controllers ask for with its documented empty result.</summary>
	private sealed class EmptyLanguageFeatureProvider
		: ICompletionProvider,
			IHoverProvider,
			ISignatureHelpProvider,
			ICodeActionProvider,
			IDiagnosticsProvider
	{
		public Task<IReadOnlyList<CompletionItem>> GetCompletionItemsAsync(
			CompletionRequest request,
			CancellationToken cancellationToken = default
		) => Task.FromResult<IReadOnlyList<CompletionItem>>([]);

		public Task<HoverInformation?> GetHoverAsync(HoverRequest request, CancellationToken cancellationToken = default) =>
			Task.FromResult<HoverInformation?>(null);

		public Task<SignatureHelpInformation?> GetSignatureHelpAsync(SignatureHelpRequest request, CancellationToken cancellationToken = default) =>
			Task.FromResult<SignatureHelpInformation?>(null);

		public Task<IReadOnlyList<CodeAction>> GetCodeActionsAsync(CodeActionRequest request, CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<CodeAction>>([]);

		public Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(DiagnosticsRequest request, CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<Diagnostic>>([]);
	}

	/// <summary>
	/// Checks that a debounced scheduled request asks the provider and opens the completion list through the default completion data adapter,
	/// because no <see cref="CompletionPresenterHooks.CompletionItemFactory"/> is supplied.
	/// </summary>
	[TestMethod]
	public void Wiring_ScheduledRequestRunsTheDefaultCompletionAdapter()
	{
		var editor = new TextEditor { Text = "sample" };
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var provider = ScriptedCompletionProvider.Answering("sample");

		CompletionPresenterHooks hooks = CompletionTooltipTestHelpers.CreateWiringHooks(editor);
		CompletionControllerOptions options = CompletionControllerOptions.Default with { RequestDebounceDelay = TimeSpan.FromMilliseconds(1.0) };

		var completion = new CompletionPresenter(
			editor.TextArea,
			new CompletionPresentationOptions { WindowTheme = CompletionWindowTheme.Default },
			hooks
		);

		CompletionController controller = CompletionControllerFactory.Create(
			textArea: editor.TextArea,
			documentId: "sample.txt",
			provider,
			presenter: completion,
			options: options
		);

		using (completion)
		{
			using (controller)
			{
				controller.ScheduleRequest();

				DispatcherTestHelpers.PumpUntil(() => provider.Requests.Count > 0);
				DispatcherTestHelpers.PumpUntil(() => controller.CurrentPresentation.IsListVisible);

				Assert.IsTrue(controller.CurrentPresentation.IsListVisible);
			}
		}
	}
}
