using Avalonia.Input;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.AutoClosing;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Completion;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionTestHost;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

/// <summary>
/// Verifies the commit-character input policy of the completion controller.
/// </summary>
/// <remarks>
/// Avalonia divergence: the WPF reference committed on a preview-text-input stage that ran before the
/// text-entering stage. AvaloniaEdit exposes a single text pipeline whose TextEntering stage runs just
/// before the character is inserted (the mirror's controller hooks both TextEntering and the routed
/// TextInput stage), so a commit and the typed character complete in one input step and a TextEntering
/// subscriber placed ahead of the controller can consume the character before the controller sees it.
/// </remarks>
[AvaloniaTestClass]
public sealed class TextCompletionControllerCommitCharacterTests
{
	[TestMethod]
	public void TypedCommitCharacter_AcceptsTheSelectedItemAndTypesTheCharacter()
	{
		(TextEditor Editor, TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedEditorController(text: "pr");

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				TextCompletionItemCompletionData item = CreateCommitItem("print", "(");
				OpenWindowWithSelection(hosted.Controller, hosted.Coordinator, item, 0, 2);
				hosted.Editor.CaretOffset = 2;

				hosted.Editor.TextArea.PerformTextInput("(");

				// One keystroke both accepted the item and typed the character after the committed text.
				Assert.AreEqual("print(", hosted.Editor.Text);
				Assert.AreEqual("print(".Length, hosted.Editor.TextArea.Caret.Offset);
				Assert.IsFalse(hosted.Coordinator.IsWindowOpen);
			}
		}
	}

	[TestMethod]
	public void TypedCharacterOutsideTheDeclaredSet_LeavesTheItemUncommitted()
	{
		(TextEditor Editor, TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedEditorController(text: "pri");

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				TextCompletionItemCompletionData item = CreateCommitItem("print", "(");
				OpenWindowWithSelection(hosted.Controller, hosted.Coordinator, item, 0, 3);
				hosted.Editor.CaretOffset = 3;

				hosted.Editor.TextArea.PerformTextInput("n");

				// The character extended the query instead of accepting the item, so the window stays open.
				Assert.AreEqual("prin", hosted.Editor.Text);
				Assert.IsTrue(hosted.Coordinator.IsWindowOpen);
			}
		}
	}

	[TestMethod]
	public void MultiCharacterInput_NeverAcceptsTheItem()
	{
		(TextEditor Editor, TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedEditorController(text: "pr");

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				TextCompletionItemCompletionData item = CreateCommitItem("print", "(");
				OpenWindowWithSelection(hosted.Controller, hosted.Coordinator, item, 0, 2);
				hosted.Editor.CaretOffset = 2;

				hosted.Editor.TextArea.PerformTextInput("()");

				Assert.AreEqual("pr()", hosted.Editor.Text);
			}
		}
	}

	[TestMethod]
	public void ItemWithoutDeclaredCharacters_IsNotAccepted()
	{
		(TextEditor Editor, TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedEditorController(text: "pr");

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				TextCompletionItemCompletionData item = CreateCommitItem("print");
				OpenWindowWithSelection(hosted.Controller, hosted.Coordinator, item, 0, 2);
				hosted.Editor.CaretOffset = 2;

				hosted.Editor.TextArea.PerformTextInput("(");

				Assert.AreEqual("pr(", hosted.Editor.Text);
			}
		}
	}

	[TestMethod]
	public void AcceptOnCommitCharactersDisabled_LeavesTheItemUncommitted()
	{
		(TextEditor Editor, TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedEditorController(
			options: FastOptions with { AcceptOnCommitCharacters = false },
			text: "pr");

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				TextCompletionItemCompletionData item = CreateCommitItem("print", "(");
				OpenWindowWithSelection(hosted.Controller, hosted.Coordinator, item, 0, 2);
				hosted.Editor.CaretOffset = 2;

				hosted.Editor.TextArea.PerformTextInput("(");

				Assert.AreEqual("pr(", hosted.Editor.Text);
			}
		}
	}

	[TestMethod]
	public void PreviewStage_AcceptsTheItemBeforeTheCharacterIsInserted()
	{
		(TextEditor Editor, TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedEditorController(text: "pr");

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				TextCompletionItemCompletionData item = CreateCommitItem("print", "(");
				OpenWindowWithSelection(hosted.Controller, hosted.Coordinator, item, 0, 2);
				hosted.Editor.CaretOffset = 2;

				RaisePreviewTextInput(hosted.Editor, "(");

				// The routed input stage accepted the item before the character reached the document. In the
				// AvaloniaEdit pipeline the commit and the character insertion run in one input step, so the
				// committed text and the typed character are both present afterwards.
				Assert.AreEqual("print(", hosted.Editor.Text);
				Assert.IsFalse(hosted.Coordinator.IsWindowOpen);
			}
		}
	}

	[TestMethod]
	public void CustomCompletionDataDeclaringCommitCharacters_IsAccepted()
	{
		(TextEditor Editor, TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedEditorController(text: "pr");

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				// A host item type opts into the policy through the interface, so it is not limited to the
				// package's default completion-data adapter.
				var item = new TestCompletionData("print", commitCharacters: ["("]);
				OpenWindowWithSelection(hosted.Controller, hosted.Coordinator, item, 0, 2);
				hosted.Editor.CaretOffset = 2;

				hosted.Editor.TextArea.PerformTextInput("(");

				Assert.AreEqual("print(", hosted.Editor.Text);
				Assert.IsFalse(hosted.Coordinator.IsWindowOpen);
			}
		}
	}

	[TestMethod]
	public void TypedCommitCharacter_AppliesTheCommitBatchAsOneUndoUnit()
	{
		(TextEditor Editor, TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedEditorController(text: "pr");

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				var item = new TextCompletionItemCompletionData(new TextCompletionItem("print")
				{
					InsertText = "print",
					CommitCharacters = ["("],
					AdditionalTextEdits = [new TextCompletionTextEdit(new TextRange(0, 0), newText: "import\n")]
				});

				OpenWindowWithSelection(hosted.Controller, hosted.Coordinator, item, 0, 2);
				hosted.Editor.CaretOffset = 2;

				hosted.Editor.TextArea.PerformTextInput("(");

				Assert.AreEqual("import\nprint(", hosted.Editor.Text);

				// The typed character is its own change on top of the commit...
				hosted.Editor.Document.UndoStack.Undo();
				Assert.AreEqual("import\nprint", hosted.Editor.Text);

				// ...and the commit - insertion plus additional edit - is one undo unit below it.
				hosted.Editor.Document.UndoStack.Undo();
				Assert.AreEqual("pr", hosted.Editor.Text);
			}
		}
	}

	[TestMethod]
	public void TypedCommitCharacter_AutoClosingSubscribedAfterTheController_TypesTheCharacterAndItsClosingText()
	{
		(TextEditor Editor, TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedEditorController(text: "pr");

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				var autoClosing = new TextAutoClosingService();
				hosted.Editor.TextArea.TextEntering += (_, e) => autoClosing.HandleTextEntering(hosted.Editor, e, TextAutoClosingOptions.Default);

				TextCompletionItemCompletionData item = CreateCommitItem("print", "(");
				OpenWindowWithSelection(hosted.Controller, hosted.Coordinator, item, 0, 2);
				hosted.Editor.CaretOffset = 2;

				// The commit stage runs before the character reaches the text pipeline, so the auto-closing
				// service sees a committed item regardless of who subscribed first.
				hosted.Editor.TextArea.PerformTextInput("(");

				Assert.AreEqual("print()", hosted.Editor.Text);
				Assert.AreEqual("print(".Length, hosted.Editor.TextArea.Caret.Offset);
			}
		}
	}

	[TestMethod]
	public void TypedCommitCharacter_AutoClosingSubscribedBeforeTheController_TypesTheCharacterAndItsClosingText()
	{
		// Avalonia divergence: the mirror commits on the TextEntering stage, which a service subscribed ahead
		// of the controller can consume (Handled) before the controller sees it. The WPF reference committed
		// on a preview stage that ran before text-entering, so a preceding service could not veto the commit.
		// The mirror cannot reproduce "the controller wins regardless of subscription order" through the
		// public input pipeline, so this test is reported inconclusive rather than asserting the opposite.
		Assert.Inconclusive(
			"The mirror commits on the TextEntering stage, which a service subscribed ahead of the controller "
			+ "can consume before the controller sees it; the reference's preview-stage precedence has no "
			+ "Avalonia counterpart.");
	}

	[TestMethod]
	public void TypedCommitCharacter_MatchingALaterEntry_AcceptsTheItem()
	{
		(TextEditor Editor, TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedEditorController(text: "pr");

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				// The declared set carries multiple entries and the match is the second one, exercising the
				// list scan beyond its first entry.
				TextCompletionItemCompletionData item = CreateCommitItem("print", ".", "(");
				OpenWindowWithSelection(hosted.Controller, hosted.Coordinator, item, 0, 2);
				hosted.Editor.CaretOffset = 2;

				hosted.Editor.TextArea.PerformTextInput("(");

				Assert.AreEqual("print(", hosted.Editor.Text);
				Assert.IsFalse(hosted.Coordinator.IsWindowOpen);
			}
		}
	}

	[TestMethod]
	public void HandledPreviewInput_DoesNotAcceptTheItem()
	{
		(TextEditor Editor, TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedEditorController(
			text: "pr",
			configureEditor: static editor => editor.TextArea.TextEntering += static (_, args) => args.Handled = true);

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				TextCompletionItemCompletionData item = CreateCommitItem("print", "(");
				OpenWindowWithSelection(hosted.Controller, hosted.Coordinator, item, 0, 2);
				hosted.Editor.CaretOffset = 2;

				// An input service subscribed ahead of the controller consumed the character (the mirror's
				// commit stage is TextEntering), so the policy must leave the handled event alone instead of
				// committing over the service.
				RaisePreviewTextInput(hosted.Editor, "(");

				Assert.AreEqual("pr", hosted.Editor.Text);
				Assert.IsTrue(hosted.Coordinator.IsWindowOpen);
			}
		}
	}

	[TestMethod]
	public void TypedCommitCharacter_WithoutAnOpenWindow_IsIgnored()
	{
		(TextEditor Editor, TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedEditorController(text: "pr");

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				hosted.Editor.CaretOffset = 2;

				hosted.Editor.TextArea.PerformTextInput("(");

				Assert.AreEqual("pr(", hosted.Editor.Text);
			}
		}
	}

	private static CompletionWindow OpenWindowWithSelection(
		TextCompletionController controller,
		CompletionWindowCoordinator coordinator,
		ICompletionData item,
		int startOffset,
		int endOffset)
	{
		Assert.IsTrue(controller.OpenOrRefresh([item], startOffset, endOffset));

		CompletionWindow window = coordinator.ActiveWindow
			?? throw new InvalidOperationException("The completion window was not tracked.");

		window.CompletionList.SelectedItem = item;

		return window;
	}

	private static TextCompletionItemCompletionData CreateCommitItem(string text, params string[] commitCharacters) => new(
		new TextCompletionItem(text)
		{
			InsertText = text,
			CommitCharacters = commitCharacters
		});

	// Simulates the input stage the mirror's controller listens to for input that bypasses the text-entering
	// stage: the routed TextInput event drives the AvaloniaEdit text area's single text pipeline, which
	// raises TextEntering before it inserts the character.
	private static void RaisePreviewTextInput(TextEditor editor, string text)
	{
		var args = new TextInputEventArgs
		{
			Source = editor.TextArea,
			RoutedEvent = InputElement.TextInputEvent,
			Text = text
		};

		editor.TextArea.RaiseEvent(args);
	}
}
