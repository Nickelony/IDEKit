using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.AutoClosing;
using Nickelony.IDEKit.AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.AutoClosing;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionInputTestHelpers;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionTestHelpers;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.AutoClosing;
using Nickelony.IDEKit.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.AutoClosing;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionInputTestHelpers;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>Verifies the commit-character input policy of the completion controller.</summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class CompletionPresenterCommitCharacterTests
{
	[TestMethod]
	public void TypedCommitCharacter_AcceptsTheSelectedItemAndTypesTheCharacter()
	{
		(
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) hosted = CreateHostedEditorController(text: "pr");

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					CompletionItemAdapter item = CreateCommitItem("print", "(");
					OpenWindowWithSelection(completion: hosted.Completion, presenter: hosted.Presenter, item, startOffset: 0, endOffset: 2);
					hosted.Editor.CaretOffset = 2;

					TypeThroughInputRoute(hosted.Editor, "(");

					// One keystroke both accepted the item and typed the character after the committed text.
					Assert.AreEqual("print(", hosted.Editor.Text);
					Assert.AreEqual("print(".Length, hosted.Editor.TextArea.Caret.Offset);
					Assert.IsFalse(hosted.Presenter.IsWindowOpen);
				}
			}
		}
	}

	[TestMethod]
	public void TypedCommitCharacter_OnASnippetItem_InsertsThePlainExpansionAndTypesTheCharacterAfterIt()
	{
		(
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) hosted = CreateHostedEditorController(text: "re");

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					var item = new CompletionItemAdapter(
						new CompletionItem("rep")
						{
							InsertText = "rep(${1:s}, ${2:n})",
							InsertTextFormat = CompletionInsertTextFormat.Snippet,
							CommitCharacters = ["("],
						}
					);

					OpenWindowWithSelection(completion: hosted.Completion, presenter: hosted.Presenter, item, startOffset: 0, endOffset: 2);
					hosted.Editor.CaretOffset = 2;

					TypeThroughInputRoute(hosted.Editor, "(");

					// The commit left the caret after the expansion, as a plain insertion does, so the character did not overwrite a selected tabstop.
					Assert.AreEqual("rep(s, n)(", hosted.Editor.Text);
					Assert.AreEqual("rep(s, n)(".Length, hosted.Editor.TextArea.Caret.Offset);
					Assert.IsTrue(hosted.Editor.TextArea.StackedInputHandlers.IsEmpty);
				}
			}
		}
	}

	[TestMethod]
	public void TypedCharacterOutsideTheDeclaredSet_LeavesTheItemUncommitted()
	{
		(
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) hosted = CreateHostedEditorController(text: "pri");

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					CompletionItemAdapter item = CreateCommitItem("print", "(");
					OpenWindowWithSelection(completion: hosted.Completion, presenter: hosted.Presenter, item, startOffset: 0, endOffset: 3);
					hosted.Editor.CaretOffset = 3;

					TypeThroughInputRoute(hosted.Editor, "n");

					// The character extended the query instead of accepting the item, so the window stays open.
					Assert.AreEqual("prin", hosted.Editor.Text);
					Assert.IsTrue(hosted.Presenter.IsWindowOpen);
				}
			}
		}
	}

	[TestMethod]
	public void MultiCharacterInput_NeverAcceptsTheItem()
	{
		(
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) hosted = CreateHostedEditorController(text: "pr");

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					CompletionItemAdapter item = CreateCommitItem("print", "(");
					OpenWindowWithSelection(completion: hosted.Completion, presenter: hosted.Presenter, item, startOffset: 0, endOffset: 2);
					hosted.Editor.CaretOffset = 2;

					TypeThroughInputRoute(hosted.Editor, "()");

					Assert.AreEqual("pr()", hosted.Editor.Text);
				}
			}
		}
	}

	[TestMethod]
	public void ItemWithoutDeclaredCharacters_IsNotAccepted()
	{
		(
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) hosted = CreateHostedEditorController(text: "pr");

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					CompletionItemAdapter item = CreateCommitItem("print");
					OpenWindowWithSelection(completion: hosted.Completion, presenter: hosted.Presenter, item, startOffset: 0, endOffset: 2);
					hosted.Editor.CaretOffset = 2;

					TypeThroughInputRoute(hosted.Editor, "(");

					Assert.AreEqual("pr(", hosted.Editor.Text);
				}
			}
		}
	}

	[TestMethod]
	public void AcceptOnCommitCharactersDisabled_LeavesTheItemUncommitted()
	{
		(
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) hosted = CreateHostedEditorController(presentation: CreatePresentation() with { AcceptOnCommitCharacters = false }, text: "pr");

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					CompletionItemAdapter item = CreateCommitItem("print", "(");
					OpenWindowWithSelection(completion: hosted.Completion, presenter: hosted.Presenter, item, startOffset: 0, endOffset: 2);
					hosted.Editor.CaretOffset = 2;

					TypeThroughInputRoute(hosted.Editor, "(");

					Assert.AreEqual("pr(", hosted.Editor.Text);
				}
			}
		}
	}

	[TestMethod]
	public void TypedThroughTheInputRoute_AcceptsTheItemBeforeTheCharacterIsInserted()
	{
		(
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) hosted = CreateHostedEditorController(text: "pr");

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					CompletionItemAdapter item = CreateCommitItem("print", "(");
					OpenWindowWithSelection(completion: hosted.Completion, presenter: hosted.Presenter, item, startOffset: 0, endOffset: 2);
					hosted.Editor.CaretOffset = 2;

					TypeThroughInputRoute(hosted.Editor, "(");

					Assert.AreEqual("print(", hosted.Editor.Text);
					Assert.IsFalse(hosted.Presenter.IsWindowOpen);
				}
			}
		}
	}

	[TestMethod]
	public void CustomCompletionDataDeclaringCommitCharacters_IsAccepted()
	{
		(
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) hosted = CreateHostedEditorController(text: "pr");

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					// A host item type opts into the policy through the interface, so it is not limited to the
					// package's default completion-data adapter.
					var item = new StubCompletionData("print", commitCharacters: ["("]);
					OpenWindowWithSelection(completion: hosted.Completion, presenter: hosted.Presenter, item, startOffset: 0, endOffset: 2);
					hosted.Editor.CaretOffset = 2;

					TypeThroughInputRoute(hosted.Editor, "(");

					Assert.AreEqual("print(", hosted.Editor.Text);
					Assert.IsFalse(hosted.Presenter.IsWindowOpen);
				}
			}
		}
	}

	[TestMethod]
	public void TypedCommitCharacter_AppliesTheCommitBatchAsOneUndoUnit()
	{
		(
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) hosted = CreateHostedEditorController(text: "pr");

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					var item = new CompletionItemAdapter(
						new CompletionItem("print")
						{
							InsertText = "print",
							CommitCharacters = ["("],
							AdditionalTextEdits = [new CompletionTextEdit(new TextRange(0, 0), newText: "import\n")],
						}
					);

					OpenWindowWithSelection(completion: hosted.Completion, presenter: hosted.Presenter, item, startOffset: 0, endOffset: 2);
					hosted.Editor.CaretOffset = 2;

					TypeThroughInputRoute(hosted.Editor, "(");

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
	}

	[TestMethod]
	public void TypedCommitCharacter_AutoClosingSubscribedAfterTheController_TypesTheCharacterAndItsClosingText()
	{
		(
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) hosted = CreateHostedEditorController(text: "pr");

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					var autoClosing = new AutoClosingService();
					hosted.Editor.TextArea.TextEntering += (_, e) => autoClosing.ApplyTextEntering(hosted.Editor, e, AutoClosingOptions.Default);

					CompletionItemAdapter item = CreateCommitItem("print", "(");
					OpenWindowWithSelection(completion: hosted.Completion, presenter: hosted.Presenter, item, startOffset: 0, endOffset: 2);
					hosted.Editor.CaretOffset = 2;

					TypeThroughInputRoute(hosted.Editor, "(");

					Assert.AreEqual("print()", hosted.Editor.Text);
					Assert.AreEqual("print(".Length, hosted.Editor.TextArea.Caret.Offset);
				}
			}
		}
	}

	[TestMethod]
	public void TypedCommitCharacter_AutoClosingSubscribedBeforeTheController_StillSeesTheCommittedItem()
	{
		(
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) hosted = CreateHostedEditorController(
			text: "pr",

			configureEditor: editor =>
			{
				// The pair is pinned explicitly instead of using the service's defaults, so this ordering
				// test only depends on the behaviors it actually exercises.
				AutoClosingOptions autoClosingOptions = AutoClosingOptions.Default with
				{
					Pairs = [AutoClosingPair.Parentheses],
				};

				var autoClosing = new AutoClosingService();
				editor.TextArea.TextEntering += (_, e) => autoClosing.ApplyTextEntering(editor, e, autoClosingOptions);
			}
		);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					CompletionItemAdapter item = CreateCommitItem("print", "(");
					OpenWindowWithSelection(completion: hosted.Completion, presenter: hosted.Presenter, item, startOffset: 0, endOffset: 2);
					hosted.Editor.CaretOffset = 2;

					TypeThroughInputRoute(hosted.Editor, "(");

					// The commit runs on the tunneling stage, before every TextEntering subscriber, so the service pairs the parenthesis after the committed item.
					Assert.AreEqual("print()", hosted.Editor.Text);
					Assert.AreEqual("print(".Length, hosted.Editor.TextArea.Caret.Offset);
				}
			}
		}
	}

	[TestMethod]
	public void TypedCommitCharacter_MatchingALaterEntry_AcceptsTheItem()
	{
		(
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) hosted = CreateHostedEditorController(text: "pr");

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					// The declared set carries multiple entries and the match is the second one, exercising the
					// list scan beyond its first entry.
					CompletionItemAdapter item = CreateCommitItem("print", ".", "(");
					OpenWindowWithSelection(completion: hosted.Completion, presenter: hosted.Presenter, item, startOffset: 0, endOffset: 2);
					hosted.Editor.CaretOffset = 2;

					TypeThroughInputRoute(hosted.Editor, "(");

					Assert.AreEqual("print(", hosted.Editor.Text);
					Assert.IsFalse(hosted.Presenter.IsWindowOpen);
				}
			}
		}
	}

	[TestMethod]
	public void HandledTextEntering_DoesNotAcceptTheItem()
	{
		(
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) hosted = CreateHostedEditorController(text: "pr", configureEditor: ConsumeInputBeforeController);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					CompletionItemAdapter item = CreateCommitItem("print", "(");
					OpenWindowWithSelection(completion: hosted.Completion, presenter: hosted.Presenter, item, startOffset: 0, endOffset: 2);
					hosted.Editor.CaretOffset = 2;

					// An input service subscribed ahead of the controller consumed the character, so the policy
					// must leave the handled event alone instead of committing over the service.
					TypeThroughInputRoute(hosted.Editor, "(");

					Assert.AreEqual("pr", hosted.Editor.Text);
					Assert.IsTrue(hosted.Presenter.IsWindowOpen);
				}
			}
		}
	}

	[TestMethod]
	public void TypedCommitCharacter_WithoutAnOpenWindow_IsIgnored()
	{
		(
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) hosted = CreateHostedEditorController(text: "pr");

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					hosted.Editor.CaretOffset = 2;

					TypeThroughInputRoute(hosted.Editor, "(");

					Assert.AreEqual("pr(", hosted.Editor.Text);
				}
			}
		}
	}

	private static CompletionWindow OpenWindowWithSelection(
		CompletionPresenter completion,
		CompletionWindowPresenter presenter,
		ICompletionData item,
		int startOffset,
		int endOffset
	)
	{
		Assert.IsTrue(completion.OpenOrRefresh([item], TextRange.FromBounds(startOffset, endOffset)));

		CompletionWindow window = presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

		window.CompletionList.SelectedItem = item;

		return window;
	}

	private static CompletionItemAdapter CreateCommitItem(string text, params string[] commitCharacters) =>
		new(new CompletionItem(text) { InsertText = text, CommitCharacters = commitCharacters });
}
