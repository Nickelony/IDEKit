#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Navigation;
using PointerModifiers = Avalonia.Input.KeyModifiers;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Navigation;
using PointerModifiers = System.Windows.Input.ModifierKeys;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class PointerNavigationSessionTests
{
	private const string DocumentText = "sample alpha = 1\nsample bravo = 2\nsample charlie = 3";
	private const string DocumentId = "pointer-test.txt";

	private static readonly int s_alphaOffset = DocumentText.IndexOf("alpha", StringComparison.Ordinal);
	private static readonly int s_bravoOffset = DocumentText.IndexOf("bravo", StringComparison.Ordinal) + 1;
	private static readonly int s_charlieOffset = DocumentText.IndexOf("charlie", StringComparison.Ordinal);

	// The modifiers the next press holds, which the WPF session reads in place of the real keyboard.
	private PointerModifiers _held;

	[TestMethod]
	public void ModifierClickOnASymbol_IsHandledAndNavigates()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor(DocumentText);
		var provider = new GatedDefinitionProvider();

		using (hostWindow)
		{
			using PointerNavigationSession session = Attach(editor.TextArea, provider, () => s_bravoOffset);
			Assert.IsTrue(Press(editor.TextArea, PointerModifiers.Control));

			provider.Answer(0, LocationAt(3, 8));
			DispatcherTestHelpers.PumpUntil(() => session.CurrentNavigation.IsCompleted);

			Assert.AreEqual(s_bravoOffset, provider.Calls[0].Request.SymbolOffset);
			Assert.AreEqual("bravo", provider.Calls[0].Request.SymbolName);
			Assert.AreEqual(s_charlieOffset, editor.CaretOffset);
		}
	}

	[TestMethod]
	public void ModifierClick_IsHandledBeforeItsDefinitionResolves()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor(DocumentText);
		var provider = new GatedDefinitionProvider();

		using (hostWindow)
		{
			using PointerNavigationSession session = Attach(editor.TextArea, provider, () => s_bravoOffset);

			// The routing of the press ends while the provider still runs, so the press must be claimed before the resolve completes, or the
			// editor's own press handler would move the caret and start a selection.
			Assert.IsTrue(Press(editor.TextArea, PointerModifiers.Control));
			Assert.HasCount(1, provider.Calls);
			Assert.IsFalse(session.CurrentNavigation.IsCompleted);
			Assert.AreEqual(0, editor.CaretOffset);

			provider.Answer(0, LocationAt(3, 8));
			DispatcherTestHelpers.PumpUntil(() => session.CurrentNavigation.IsCompleted);

			Assert.AreEqual(s_charlieOffset, editor.CaretOffset);
		}
	}

	[TestMethod]
	public void PlainClick_IsNotHandledAndDoesNotNavigate()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor(DocumentText);
		var provider = new GatedDefinitionProvider();

		using (hostWindow)
		{
			using (Attach(editor.TextArea, provider, () => s_bravoOffset))
			{
				Assert.IsFalse(Press(editor.TextArea, PointerModifiers.None));
				Assert.IsEmpty(provider.Calls);
			}
		}
	}

	[TestMethod]
	public void ClickWithOtherModifiers_IsNotHandledAndDoesNotNavigate()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor(DocumentText);
		var provider = new GatedDefinitionProvider();

		using (hostWindow)
		{
			using (Attach(editor.TextArea, provider, () => s_bravoOffset))
			{
				// The modifiers must match exactly, so a gesture that adds Shift stays the editor's.
				Assert.IsFalse(Press(editor.TextArea, PointerModifiers.Control | PointerModifiers.Shift));
				Assert.IsFalse(Press(editor.TextArea, PointerModifiers.Shift));
				Assert.IsEmpty(provider.Calls);
			}
		}
	}

	[TestMethod]
	public void ModifierClickOffTheText_IsNotHandledAndDoesNotNavigate()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor(DocumentText);
		var provider = new GatedDefinitionProvider();

		using (hostWindow)
		{
			using (Attach(editor.TextArea, provider, () => null))
			{
				Assert.IsFalse(Press(editor.TextArea, PointerModifiers.Control));
				Assert.IsEmpty(provider.Calls);
			}
		}
	}

	[TestMethod]
	public void ModifierClickThatResolvesNothing_PlacesTheCaretAtTheClickLikeAPlainClick()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor(DocumentText);
		var provider = new GatedDefinitionProvider();

		using (hostWindow)
		{
			using PointerNavigationSession session = Attach(editor.TextArea, provider, () => s_bravoOffset);
			editor.Select(0, 3);

			Press(editor.TextArea, PointerModifiers.Control);
			provider.Answer(0, null);
			DispatcherTestHelpers.PumpUntil(() => session.CurrentNavigation.IsCompleted);

			Assert.AreEqual(s_bravoOffset, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public void ModifierClickThatResolvesNothingAfterAnEdit_LeavesTheCaret()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor(DocumentText);
		var provider = new GatedDefinitionProvider();

		using (hostWindow)
		{
			using PointerNavigationSession session = Attach(editor.TextArea, provider, () => s_bravoOffset);
			Press(editor.TextArea, PointerModifiers.Control);

			// The clicked offset addresses the text before the edit, so the plain-click fallback must not place the caret there.
			editor.Document.Insert(editor.Document.TextLength, " edited");
			provider.Answer(0, null);
			DispatcherTestHelpers.PumpUntil(() => session.CurrentNavigation.IsCompleted);

			Assert.AreEqual(0, editor.CaretOffset);
		}
	}

	[TestMethod]
	public void ModifierClickOnACrossFileDefinition_ReachesTheCallbackAndLeavesTheCaret()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor(DocumentText);
		var provider = new GatedDefinitionProvider();
		DefinitionLocation? reported = null;

		Task<bool> navigateToDocument(DefinitionLocation location, CancellationToken _)
		{
			reported = location;
			return Task.FromResult(true);
		}

		using (hostWindow)
		{
			using PointerNavigationSession session = Attach(
				textArea: editor.TextArea,
				provider,
				clickedOffset: () => s_bravoOffset,
				navigateToDocumentAsync: navigateToDocument
			);

			Press(editor.TextArea, PointerModifiers.Control);
			provider.Answer(0, LocationAt(2, documentId: "other.lua"));
			DispatcherTestHelpers.PumpUntil(() => session.CurrentNavigation.IsCompleted);

			Assert.AreEqual("other.lua", reported?.DocumentId);
			Assert.AreEqual(0, editor.CaretOffset);
		}
	}

	[TestMethod]
	public void LaterModifierClick_CancelsTheEarlierNavigation_AndOnlyTheLaterOneApplies()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor(DocumentText);
		var provider = new GatedDefinitionProvider();
		int clicked = s_bravoOffset;

		using (hostWindow)
		{
			using PointerNavigationSession session = Attach(editor.TextArea, provider, () => clicked);
			Press(editor.TextArea, PointerModifiers.Control);
			Task first = session.CurrentNavigation;

			clicked = s_charlieOffset;
			Press(editor.TextArea, PointerModifiers.Control);

			Assert.IsTrue(provider.Calls[0].Token.IsCancellationRequested);
			Assert.IsFalse(provider.Calls[1].Token.IsCancellationRequested);

			// The later click resolves first, and the earlier provider then answers anyway, the way one that cannot cancel does.
			provider.Answer(1, LocationAt(1, 8));
			DispatcherTestHelpers.PumpUntil(() => session.CurrentNavigation.IsCompleted);
			provider.Answer(0, LocationAt(3, 8));
			DispatcherTestHelpers.PumpUntil(() => first.IsCompleted);

			Assert.AreEqual(s_alphaOffset, editor.CaretOffset);
		}
	}

	[TestMethod]
	public void LaterModifierClick_WhoseEarlierNavigationResolvesFirst_StillAppliesOnlyTheLaterOne()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor(DocumentText);
		var provider = new GatedDefinitionProvider();

		using (hostWindow)
		{
			using PointerNavigationSession session = Attach(editor.TextArea, provider, () => s_bravoOffset);
			Press(editor.TextArea, PointerModifiers.Control);
			Task first = session.CurrentNavigation;
			Press(editor.TextArea, PointerModifiers.Control);

			// The canceled navigation resolves nothing, so it would fall back to a plain click unless its cancellation stops it.
			provider.Answer(0, null);
			DispatcherTestHelpers.PumpUntil(() => first.IsCompleted);

			Assert.AreEqual(0, editor.CaretOffset);

			provider.Answer(1, LocationAt(1, 8));
			DispatcherTestHelpers.PumpUntil(() => session.CurrentNavigation.IsCompleted);

			Assert.AreEqual(s_alphaOffset, editor.CaretOffset);
		}
	}

	[TestMethod]
	public void Dispose_WhileADefinitionResolves_CancelsItAndAppliesNothing()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor(DocumentText);
		var provider = new GatedDefinitionProvider();

		using (hostWindow)
		{
			PointerNavigationSession session = Attach(editor.TextArea, provider, () => s_bravoOffset);

			Press(editor.TextArea, PointerModifiers.Control);
			session.Dispose();

			Assert.IsTrue(provider.Calls[0].Token.IsCancellationRequested);

			provider.Answer(0, LocationAt(3, 8));
			DispatcherTestHelpers.PumpUntil(() => session.CurrentNavigation.IsCompleted);

			Assert.AreEqual(0, editor.CaretOffset);
		}
	}

	[TestMethod]
	public void DocumentSwap_CancelsTheNavigationInFlight_AndLeavesLaterClicksPlain()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor(DocumentText);
		var provider = new GatedDefinitionProvider();

		using (hostWindow)
		{
			using PointerNavigationSession session = Attach(editor.TextArea, provider, () => s_bravoOffset);
			Press(editor.TextArea, PointerModifiers.Control);

			editor.Document = new TextDocument(DocumentText);

			Assert.IsTrue(provider.Calls[0].Token.IsCancellationRequested);

			provider.Answer(0, LocationAt(3, 8));
			DispatcherTestHelpers.PumpUntil(() => session.CurrentNavigation.IsCompleted);

			Assert.AreEqual(0, editor.CaretOffset);

			// A request would carry the new document's text under the identifier the session was attached for.
			Assert.IsFalse(Press(editor.TextArea, PointerModifiers.Control));
			Assert.HasCount(1, provider.Calls);
		}
	}

	[TestMethod]
	public void Dispose_DetachesTheHandler()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor(DocumentText);
		var provider = new GatedDefinitionProvider();

		using (hostWindow)
		{
			Attach(editor.TextArea, provider, () => s_bravoOffset).Dispose();

			Assert.IsFalse(Press(editor.TextArea, PointerModifiers.Control));
			Assert.IsEmpty(provider.Calls);
		}
	}

	[TestMethod]
	public void AttachPointerNavigation_NullOrBlankArguments_Throw()
	{
		var textArea = new TextArea();
		var provider = new GatedDefinitionProvider();

		Assert.ThrowsExactly<ArgumentNullException>(() => TextAreaNavigationExtensions.AttachPointerNavigation(null!, DocumentId, provider));
		Assert.ThrowsExactly<ArgumentNullException>(() => textArea.AttachPointerNavigation(DocumentId, null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => textArea.AttachPointerNavigation(null!, provider));
		Assert.ThrowsExactly<ArgumentException>(() => textArea.AttachPointerNavigation(" ", provider));
	}

#if AVALONIAEDIT
	[TestMethod]
	public void CommandModifiers_AreMetaOnMacOSAndControlElsewhere()
	{
		Assert.AreEqual(PointerModifiers.Meta, TextAreaNavigationExtensions.CommandModifiers(isMacOS: true));
		Assert.AreEqual(PointerModifiers.Control, TextAreaNavigationExtensions.CommandModifiers(isMacOS: false));
	}

	[TestMethod]
	public void MacOSCommandModifiers_NavigateOnMetaClickAndNotOnControlClick()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor(DocumentText);
		var provider = new GatedDefinitionProvider();

		using (hostWindow)
		{
			using (
				Attach(
					textArea: editor.TextArea,
					provider,
					clickedOffset: () => s_bravoOffset,
					modifiers: TextAreaNavigationExtensions.CommandModifiers(isMacOS: true)
				)
			)
			{
				Assert.IsFalse(Press(editor.TextArea, PointerModifiers.Control));
				Assert.IsTrue(Press(editor.TextArea, PointerModifiers.Meta));
				Assert.HasCount(1, provider.Calls);
			}
		}
	}
#endif

	private static DefinitionLocation LocationAt(int oneBasedLine, int oneBasedColumn = 1, string? documentId = null)
	{
		var position = new TextPosition(oneBasedLine - 1, oneBasedColumn - 1);
		return new DefinitionLocation(new TextPositionRange(position, position), documentId);
	}

	private PointerNavigationSession Attach(
		TextArea textArea,
		IDefinitionProvider provider,
		Func<int?> clickedOffset,
		Func<DefinitionLocation, CancellationToken, Task<bool>>? navigateToDocumentAsync = null,
		PointerModifiers modifiers = PointerModifiers.Control
	)
	{
		// Avalonia reads the modifiers from the press, and WPF from the keyboard, which a test replaces.
		return new PointerNavigationSession(
			textArea,
			DocumentId,
			provider,
			tryNavigateCrossFileDefinitionAsync: navigateToDocumentAsync,
			modifiers,
			textOffsetFromPoint: _ => clickedOffset(),
#if AVALONIAEDIT
			heldModifiers: null
#else
			heldModifiers: () => _held
#endif
		);
	}

	private bool Press(TextArea textArea, PointerModifiers modifiers)
	{
		_held = modifiers;

#if AVALONIAEDIT
		return TestPointerPressEvents.RaiseLeftPress(textArea, modifiers);
#else
		return TestPointerPressEvents.RaiseLeftPress(textArea);
#endif
	}

	// Holds every request until the test answers it and ignores the token, the way a provider that cannot cancel its work does.
	private sealed class GatedDefinitionProvider : IDefinitionProvider
	{
		private readonly List<(DefinitionRequest Request, CancellationToken Token, TaskCompletionSource<DefinitionLocation?> Answer)> _calls = [];

		internal IReadOnlyList<(DefinitionRequest Request, CancellationToken Token, TaskCompletionSource<DefinitionLocation?> Answer)> Calls =>
			_calls;

		public Task<DefinitionLocation?> GetDefinitionAsync(DefinitionRequest request, CancellationToken cancellationToken = default)
		{
			var answer = new TaskCompletionSource<DefinitionLocation?>(TaskCreationOptions.RunContinuationsAsynchronously);
			_calls.Add((request, cancellationToken, answer));
			return answer.Task;
		}

		internal void Answer(int call, DefinitionLocation? location) => _calls[call].Answer.SetResult(location);
	}
}
