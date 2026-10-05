#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.AutoClosing;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.AutoClosing;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionInputTestHelpers;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionTestHelpers;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.AutoClosing;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.AutoClosing;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionInputTestHelpers;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

public sealed partial class CompletionControllerFactoryTests
{
	private static readonly AutoClosingOptions s_parentheses = AutoClosingOptions.Default with { Pairs = [AutoClosingPair.Parentheses] };

	[TestMethod]
	public void TypedLetterInCode_SchedulesARequest()
	{
		var provider = new ScriptedCompletionProvider();

		(
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) hosted = CreateHostedEditorController(text: "pr", provider: provider);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					hosted.Editor.CaretOffset = 2;

					TypeThroughInputRoute(hosted.Editor, "i");
					DispatcherTestHelpers.PumpUntil(() => provider.Requests.Count > 0);

					// The word request ran after the debounce, as an invoked request at the caret after the letter.
					Assert.AreEqual("pri", provider.Requests[0].DocumentText);
					Assert.AreEqual(3, provider.Requests[0].CaretOffset);
					Assert.AreEqual(CompletionContext.Invoked, provider.Requests[0].Context);
				}
			}
		}
	}

	[TestMethod]
	public void TypedLetterInAComment_SchedulesNothingUnderTheDefaultPolicy()
	{
		var provider = new ScriptedCompletionProvider();
		List<int> scopeOffsets = [];

		(
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) hosted = CreateHostedEditorController(
			controllerHooks: new CompletionControllerHooks
			{
				ScopeAt = offset =>
				{
					scopeOffsets.Add(offset);
					return CompletionScopes.Comment;
				},
			},

			text: "-- pr",
			provider: provider
		);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					hosted.Editor.CaretOffset = 5;

					TypeThroughInputRoute(hosted.Editor, "i");
					DispatcherTestHelpers.PumpUntil(() => scopeOffsets.Count > 0);
					DispatcherTestHelpers.PumpFrames(TimeSpan.FromMilliseconds(30.0));

					// The hook received the caret after the letter, and a comment is not a word scope of the default policy.
					Assert.AreSequenceEqual([6], scopeOffsets);
					Assert.IsFalse(hosted.Controller.CurrentPresentation.IsRequestScheduled);
					Assert.IsEmpty(provider.Requests);
				}
			}
		}
	}

	[TestMethod]
	[DataRow(CompletionScopes.Code)]
	[DataRow(CompletionScopes.Comment)]
	[DataRow(CompletionScopes.StringLiteral)]
	public void TypedTriggerCharacter_RequestsAtOnceInEveryScope(CompletionScopes scope)
	{
		var provider = new ScriptedCompletionProvider { TriggerCharacters = ["."] };

		(
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) hosted = CreateHostedEditorController(
			controllerHooks: new CompletionControllerHooks { ScopeAt = _ => scope },

			options: s_fastOptions with
			{
				RequestDebounceDelay = TimeSpan.FromMinutes(10.0),
			},

			text: "string",
			provider: provider
		);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					hosted.Editor.CaretOffset = 6;

					TypeThroughInputRoute(hosted.Editor, ".");
					DispatcherTestHelpers.PumpUntil(() => provider.Requests.Count > 0);

					// The debounce is ten minutes, so only the immediate path can have asked the provider.
					Assert.AreEqual(CompletionContext.ForCharacter('.'), provider.Requests[0].Context);
					Assert.AreEqual("string.", provider.Requests[0].DocumentText);
					Assert.IsFalse(hosted.Controller.CurrentPresentation.IsRequestScheduled);
				}
			}
		}
	}

	[TestMethod]
	public void AutoClosedBracket_ThatTheServiceHandled_TriggersARequest()
	{
		var provider = new ScriptedCompletionProvider { TriggerCharacters = ["("] };
		var autoClosing = new AutoClosingService();

		// The service subscribes before the controller, so it applies the pair and marks the input handled first.
		(
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) hosted = CreateHostedEditorController(
			text: "print",
			configureEditor: editor => editor.TextArea.TextEntering += (_, e) => autoClosing.ApplyTextEntering(editor, e, s_parentheses),
			provider: provider
		);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					hosted.Editor.CaretOffset = 5;

					TypeThroughInputRoute(hosted.Editor, "(");
					DispatcherTestHelpers.PumpUntil(() => provider.Requests.Count > 0);

					Assert.AreEqual("print()", provider.Requests[0].DocumentText);
					Assert.AreEqual(6, provider.Requests[0].CaretOffset);
					Assert.AreEqual(CompletionContext.ForCharacter('('), provider.Requests[0].Context);
				}
			}
		}
	}

	[TestMethod]
	public void OvertypeSkip_ThatOnlyMovesTheCaret_TriggersARequest()
	{
		var provider = new ScriptedCompletionProvider { TriggerCharacters = [")"] };
		var autoClosing = new AutoClosingService();

		(
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) hosted = CreateHostedEditorController(
			text: "print",
			configureEditor: editor => editor.TextArea.TextEntering += (_, e) => autoClosing.ApplyTextEntering(editor, e, s_parentheses),
			provider: provider
		);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					hosted.Editor.CaretOffset = 5;

					TypeThroughInputRoute(hosted.Editor, "(");
					DispatcherTestHelpers.PumpUntilIdle();
					TypeThroughInputRoute(hosted.Editor, ")");
					DispatcherTestHelpers.PumpUntil(() => provider.Requests.Count > 0);

					// The closing parenthesis skipped the inserted one, so the document did not change and the caret moved past it.
					Assert.AreEqual("print()", provider.Requests[0].DocumentText);
					Assert.AreEqual(7, provider.Requests[0].CaretOffset);
					Assert.AreEqual(CompletionContext.ForCharacter(')'), provider.Requests[0].Context);
				}
			}
		}
	}

	[TestMethod]
	public void CommitCharacterThatIsATriggerCharacter_CommitsBeforeTheTriggerReopensTheWindow()
	{
		var provider = ScriptedCompletionProvider.Answering("member");
		provider.TriggerCharacters = ["."];

		(
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) hosted = CreateHostedEditorController(text: "pr", provider: provider);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					var item = new CompletionItemAdapter(new CompletionItem("print") { InsertText = "print", CommitCharacters = ["."] });
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([item], TextRange.FromBounds(0, 2)));

					CompletionWindow window =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					window.CompletionList.SelectedItem = item;
					hosted.Editor.CaretOffset = 2;

					TypeThroughInputRoute(hosted.Editor, ".");

					// The window committed the item on the keystroke itself, and the trigger has not asked yet.
					Assert.AreEqual("print.", hosted.Editor.Text);
					Assert.IsFalse(hosted.Presenter.IsWindowOpen);
					Assert.IsEmpty(provider.Requests);

					DispatcherTestHelpers.PumpUntil(() => hosted.Presenter.IsWindowOpen);

					// The trigger saw the committed text, and its answer opened a new window.
					Assert.HasCount(1, provider.Requests);
					Assert.AreEqual("print.", provider.Requests[0].DocumentText);
					Assert.AreEqual(CompletionContext.ForCharacter('.'), provider.Requests[0].Context);
					Assert.AreEqual("member", hosted.Presenter.ActiveWindow!.CompletionList.CompletionData[0].Text);
				}
			}
		}
	}

	[TestMethod]
	public void FailingScopeHook_IsContainedAndLogged()
	{
		var logger = new RecordingLogger();
		var provider = new ScriptedCompletionProvider();

		(
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) hosted = CreateHostedEditorController(
			controllerHooks: new CompletionControllerHooks { ScopeAt = static _ => throw new InvalidOperationException("The scope hook failed.") },
			logger: logger,
			text: string.Empty,
			provider: provider
		);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					TypeThroughInputRoute(hosted.Editor, "a");
					DispatcherTestHelpers.PumpUntil(() => logger.Entries.Count > 0);

					Assert.AreEqual(1003, logger.Entries[0].EventId.Id);
					Assert.IsFalse(hosted.Controller.CurrentPresentation.IsRequestScheduled);
				}
			}
		}
	}

	[TestMethod]
	public void DocumentSwap_WithATypedCharacterPending_RequestsNothing()
	{
		var provider = new ScriptedCompletionProvider { TriggerCharacters = ["."] };

		(
			TextEditor Editor,
			CompletionController Controller,
			CompletionWindowPresenter Presenter,
			HostWindow HostWindow,
			CompletionPresenter Completion
		) hosted = CreateHostedEditorController(text: string.Empty, provider: provider);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					// The report of the typed character is posted, and the swap lands before it runs.
					TypeThroughInputRoute(hosted.Editor, ".");
					hosted.Editor.Document = new TextDocument("other.");

					DispatcherTestHelpers.PumpFrames(TimeSpan.FromMilliseconds(50.0));

					Assert.IsEmpty(provider.Requests);

					// The controller serves the document it was created for, so it requests nothing for the new one either.
					hosted.Editor.CaretOffset = hosted.Editor.Document.TextLength;
					TypeThroughInputRoute(hosted.Editor, ".");
					hosted.Controller.ScheduleRequest();
					Task<bool> request = hosted.Controller.RequestAsync();
					DispatcherTestHelpers.PumpFrames(TimeSpan.FromMilliseconds(50.0));

					Assert.IsTrue(request.IsCompleted);
					Assert.IsFalse(request.Result);
					Assert.IsEmpty(provider.Requests);
					Assert.IsFalse(hosted.Controller.CurrentPresentation.IsRequestScheduled);
				}
			}
		}
	}
}
