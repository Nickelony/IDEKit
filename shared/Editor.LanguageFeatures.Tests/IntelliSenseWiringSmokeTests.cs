#if AVALONIAEDIT
using Avalonia.Media;
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.Diagnostics;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Highlighting;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Hover;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Signatures;
using Nickelony.IDEKit.IntelliSense.CodeActions;
using Nickelony.IDEKit.IntelliSense.Completion;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Hover;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.Diagnostics;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Highlighting;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Hover;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Signatures;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.IntelliSense.CodeActions;
using Nickelony.IDEKit.IntelliSense.Completion;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Hover;
using Nickelony.IDEKit.IntelliSense.SemanticTokens;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

/// <summary>
/// Constructs the wiring the package README documents, using only the public API, so a breaking change to
/// the documented construction stops compiling here instead of letting the README drift away from the code.
/// </summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class IntelliSenseWiringSmokeTests
{
	/// <summary>
	/// Constructs every subsystem through the public API the README documents, so a breaking change to the
	/// documented construction stops compiling here instead of letting the README drift away from the code.
	/// The behavioral coverage of each subsystem lives in its own suite; this method only proves the wiring
	/// compiles.
	/// </summary>
	[TestMethod]
	public void ReadmeWiring_ConstructsEverySubsystem()
	{
		var editor = new TextEditor { Text = "sample" };
		using var hostWindow = TestHost.ShowInHostWindow(editor);

		using var completion = new TextCompletionController(
			editor.TextArea,
			CompletionWindowSkin.Default,
			TextCompletionControllerOptions.Default,
			new TextCompletionControllerHooks
			{
#if AVALONIAEDIT
				// Avalonia divergence: the editor hosts the completion list in a Popup, which carries no
				// FontSize of its own, so the editor's font is applied to the completion list instead of the
				// window (the WPF reference sets window.FontSize directly).
				ConfigureWindow = window => window.CompletionList.FontSize = editor.FontSize,
#else
				ConfigureWindow = window => window.FontSize = editor.FontSize,
#endif
				GetDisplayInfo = item => (item.Text, null),
				ResolveDescriptionAsync = (item, cancellationToken) => Task.FromResult<object?>(item.Description),
				TooltipSkin = CompletionTooltipSkin.Default with
				{
					Background = Brushes.Black,
					BorderBrush = Brushes.Gray
				}
			});

		using var hover = new TextHoverController(
			editor,
			new TextHoverControllerHooks
			{
				GetOffsetFromPoint = point => editor.GetPositionFromPoint(point) is { } position
					? editor.Document.GetOffset(position.Location)
					: null,
				BuildEvaluationState = offset => new TextHoverEvaluationState
				{
					ShouldRequestHover = true,
					RequestOffset = offset,
					CanShowHoverContent = true,
					CanShowDiagnosticFallback = false,
					DiagnosticInfo = null
				},
				RequestHoverAsync = (offset, cancellationToken) => Task.FromResult<Nickelony.IDEKit.IntelliSense.Hover.TextHoverInfo?>(null),
				ShowTooltip = (hoverInfo, diagnosticInfo) => { }
			});

		using var signatures = new TextSignatureHelpController(
			new TextSignatureHelpControllerHooks
			{
				GetCurrentCaretOffset = () => editor.CaretOffset,
				RequestSignatureHelpAsync = (offset, context, cancellationToken) => Task.FromResult<Nickelony.IDEKit.IntelliSense.Signatures.TextSignatureHelp?>(null),
				ShowSignatureHelp = _ => { },
				DismissSignatureHelp = () => { }
			});

		using var codeActions = new TextCodeActionController(
			editor.TextArea,
			new TextCodeActionPresentation { Skin = TextCodeActionMenuSkin.Default },
			hooks: new TextCodeActionControllerHooks
			{
				BuildRequest = _ => null,
				RequestCodeActionsAsync = (_, _) => Task.FromResult<IReadOnlyList<TextCodeActionItem>>([]),
				ExecuteActionAsync = _ => Task.CompletedTask
			});

		editor.TextArea.LeftMargins.Add(codeActions.Margin);

		var colorizer = new SemanticTokensColorizer(editor.TextArea.TextView, new TestSemanticTokenStyleResolver());
		editor.TextArea.TextView.LineTransformers.Add(colorizer);

		// The diagnostics factory projects segments from a provider the same way the README documents.
		DiagnosticsRenderer renderer = TextDiagnosticSegmentFactory.CreateRenderer(static () => []);

		Assert.IsNotNull(renderer);

		editor.TextArea.LeftMargins.Remove(codeActions.Margin);
		editor.TextArea.TextView.LineTransformers.Remove(colorizer);
	}

	/// <summary>
	/// Runs the README's distinctive behavioral claim: the debounced scheduled request opens the window
	/// through the package's default completion-data adapter, so no item mapper is needed.
	/// </summary>
	[TestMethod]
	public void ReadmeWiring_ScheduledRequestRunsTheDefaultCompletionAdapter()
	{
		var editor = new TextEditor { Text = "sample" };
		using var hostWindow = TestHost.ShowInHostWindow(editor);

		int scheduledRequestCount = 0;
		TextCompletionController? completion = null;

		completion = new TextCompletionController(
			editor.TextArea,
			CompletionWindowSkin.Default,
			TextCompletionControllerOptions.Default with
			{
				RequestDebounceDelay = TimeSpan.FromMilliseconds(1.0)
			},
			new TextCompletionControllerHooks
			{
#if AVALONIAEDIT
				// Avalonia divergence: see ReadmeWiring_ConstructsEverySubsystem; the editor's font reaches
				// the completion list rather than the popup-backed window.
				ConfigureWindow = window => window.CompletionList.FontSize = editor.FontSize,
#else
				ConfigureWindow = window => window.FontSize = editor.FontSize,
#endif
				GetDisplayInfo = item => (item.Text, null),
				ResolveDescriptionAsync = (item, cancellationToken) => Task.FromResult<object?>(item.Description),
				ScheduledRequestAsync = () =>
				{
					scheduledRequestCount++;
					return completion!.RequestAsync(
						_ => Task.FromResult(TextCompletionSessionDecision.Open([new TextCompletionItem("sample")], 0, 5)));
				}
			});

		using (completion)
		{
			completion.ScheduleRequest();
			DispatcherTestUtils.PumpUntil(() => scheduledRequestCount > 0);
			DispatcherTestUtils.PumpUntil(() => completion.CurrentPresentation.IsListVisible);

			Assert.IsTrue(completion.CurrentPresentation.IsListVisible);
		}
	}
}
