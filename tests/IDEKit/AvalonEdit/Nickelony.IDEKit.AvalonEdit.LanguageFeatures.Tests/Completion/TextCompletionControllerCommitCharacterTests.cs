using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.AutoClosing;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Completion;
using System.Windows;
using System.Windows.Input;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHost;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

/// <summary>
/// Verifies the commit-character input policy of the completion controller.
/// </summary>
[STATestClass]
[TestCategory(TestCategories.InteractiveWindow)]
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

				// The preview stage accepted the item before the character reached the text pipeline.
				Assert.AreEqual("print", hosted.Editor.Text);
				Assert.IsFalse(hosted.Coordinator.IsWindowOpen);

				// The character itself is still typed through the normal text-input path.
				hosted.Editor.TextArea.PerformTextInput("(");

				Assert.AreEqual("print(", hosted.Editor.Text);
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

				// The preview stage accepts the item before the character reaches the text pipeline, so
				// the auto-closing service sees a committed item regardless of who subscribed first.
				RaisePreviewTextInput(hosted.Editor, "(");
				Assert.AreEqual("print", hosted.Editor.Text);

				hosted.Editor.TextArea.PerformTextInput("(");

				Assert.AreEqual("print()", hosted.Editor.Text);
				Assert.AreEqual("print(".Length, hosted.Editor.TextArea.Caret.Offset);
			}
		}
	}

	[TestMethod]
	public void TypedCommitCharacter_AutoClosingSubscribedBeforeTheController_TypesTheCharacterAndItsClosingText()
	{
		(TextEditor Editor, TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedEditorController(
			text: "pr",
			configureEditor: editor =>
			{
				// The pair is pinned explicitly instead of using the service's defaults, so this ordering
				// test only depends on the behaviors it actually exercises.
				var autoClosingOptions = TextAutoClosingOptions.Default with
				{
					Pairs = [TextAutoClosingPair.Parentheses]
				};

				var autoClosing = new TextAutoClosingService();
				editor.TextArea.TextEntering += (_, e) => autoClosing.HandleTextEntering(editor, e, autoClosingOptions);
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				TextCompletionItemCompletionData item = CreateCommitItem("print", "(");
				OpenWindowWithSelection(hosted.Controller, hosted.Coordinator, item, 0, 2);
				hosted.Editor.CaretOffset = 2;

				// The preview stage still wins against a service that subscribed before the controller,
				// because it runs before the text-entering stage either way.
				RaisePreviewTextInput(hosted.Editor, "(");
				Assert.AreEqual("print", hosted.Editor.Text);

				hosted.Editor.TextArea.PerformTextInput("(");

				Assert.AreEqual("print()", hosted.Editor.Text);
				Assert.AreEqual("print(".Length, hosted.Editor.TextArea.Caret.Offset);
			}
		}
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
			configureEditor: static editor => editor.TextArea.PreviewTextInput += static (_, args) => args.Handled = true);

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				TextCompletionItemCompletionData item = CreateCommitItem("print", "(");
				OpenWindowWithSelection(hosted.Controller, hosted.Coordinator, item, 0, 2);
				hosted.Editor.CaretOffset = 2;

				// An input service subscribed ahead of the controller consumed the character, so the policy
				// must leave the handled event alone instead of committing over the service.
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

	// Simulates the stage WPF raises before the text area handles input; the character itself is typed
	// separately through PerformTextInput, like the real input pipeline does.
	private static void RaisePreviewTextInput(TextEditor editor, string text)
	{
		var composition = new TextComposition(InputManager.Current, editor.TextArea, text);
		var args = new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
		{
			RoutedEvent = UIElement.PreviewTextInputEvent
		};

		editor.TextArea.RaiseEvent(args);
	}
}
