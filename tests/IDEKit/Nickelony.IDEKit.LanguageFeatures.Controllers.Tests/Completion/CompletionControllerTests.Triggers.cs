using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

public sealed partial class CompletionControllerTests
{
	[TestMethod]
	public void HandleCharacterTyped_LetterInCode_SchedulesARequestAtTheCaretAfterTheLetter()
	{
		using var host = new CompletionControllerTestHost(text: "pr");
		using CompletionController controller = host.CreateController();

		host.Type("i");
		controller.HandleCharacterTyped('i', host.CaretOffset);

		Assert.IsTrue(controller.CurrentPresentation.IsRequestScheduled);
		Assert.IsEmpty(host.Provider.Requests);

		host.Dispatcher.Advance(CompletionControllerTestHost.s_debounceDelay);

		// The word request ran after the debounce, as an invoked request at the caret after the letter.
		Assert.AreEqual("pri", host.Provider.Requests.Single().DocumentText);
		Assert.AreEqual(3, host.Provider.Requests[0].CaretOffset);
		Assert.AreEqual(CompletionContext.Invoked, host.Provider.Requests[0].Context);
	}

	[TestMethod]
	public void HandleCharacterTyped_LetterInAComment_SchedulesNothingUnderTheDefaultPolicy()
	{
		List<int> scopeOffsets = [];

		using var host = new CompletionControllerTestHost(text: "-- pri")
		{
			ScopeAt = offset =>
			{
				scopeOffsets.Add(offset);
				return CompletionScopes.Comment;
			},
		};

		using CompletionController controller = host.CreateController();

		controller.HandleCharacterTyped('i', 6);
		host.Dispatcher.Advance(CompletionControllerTestHost.s_debounceDelay * 5);

		// The hook received the caret after the letter, and a comment is not a word scope of the default policy.
		Assert.AreSequenceEqual([6], scopeOffsets);
		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
		Assert.IsEmpty(host.Provider.Requests);
	}

	[TestMethod]
	[DataRow(CompletionScopes.Code)]
	[DataRow(CompletionScopes.Comment)]
	[DataRow(CompletionScopes.StringLiteral)]
	public void HandleCharacterTyped_TriggerCharacter_RequestsAtOnceInEveryScope(CompletionScopes scope)
	{
		using var host = new CompletionControllerTestHost(text: "string.")
		{
			Provider = new ScriptedCompletionProvider { TriggerCharacters = ["."] },
			ScopeAt = _ => scope,
			Options = CompletionControllerOptions.Default with { RequestDebounceDelay = TimeSpan.FromMinutes(10.0) },
		};

		using CompletionController controller = host.CreateController();

		controller.HandleCharacterTyped('.', 7);

		// The debounce is ten minutes, so only the immediate path can have asked the provider.
		Assert.AreEqual(CompletionContext.ForCharacter('.'), host.Provider.Requests.Single().Context);
		Assert.AreEqual("string.", host.Provider.Requests[0].DocumentText);
		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
	}

	[TestMethod]
	public void HandleCharacterTyped_TriggerCharacterAfterALetter_CancelsTheScheduledWordRequest()
	{
		using var host = new CompletionControllerTestHost { Provider = new ScriptedCompletionProvider { TriggerCharacters = ["."] } };
		using CompletionController controller = host.CreateController();

		controller.HandleCharacterTyped('a', 1);

		Assert.IsTrue(controller.CurrentPresentation.IsRequestScheduled);

		controller.HandleCharacterTyped('.', 2);
		host.Dispatcher.Advance(CompletionControllerTestHost.s_debounceDelay * 5);

		// The word request the letter scheduled never ran, so the request of the trigger character stays the newest one.
		Assert.AreEqual(CompletionContext.ForCharacter('.'), host.Provider.Requests.Single().Context);
	}

	[TestMethod]
	public void HandleCharacterTyped_TriggerRequestSupersededByTheNextKeystroke_IsCanceledAndItsAnswerIsDiscarded()
	{
		using var host = new CompletionControllerTestHost { Provider = new ScriptedCompletionProvider { TriggerCharacters = ["."] } };
		using CompletionController controller = host.CreateController();

		var first = new CompletionControllerTestHost.GatedAnswer("stale");

		host.Provider.Answer = (request, cancellationToken) =>
			host.Provider.Requests.Count == 1
				? first.AnswerAsync(request, cancellationToken)
				: Task.FromResult<IReadOnlyList<CompletionItem>>([new CompletionItem("fresh")]);

		controller.HandleCharacterTyped('.', 1);
		host.Dispatcher.PumpUntil(() => first.Started.IsCompleted);
		controller.HandleCharacterTyped('.', 2);
		host.Dispatcher.PumpUntil(() => host.Presenter.Applied.Count == 1);

		Assert.IsTrue(first.Token.IsCancellationRequested);

		// The superseded provider ignores its token and answers late; its items must not replace the newer list's.
		first.Release();
		host.Dispatcher.PumpUntilIdle();

		Assert.HasCount(1, host.Presenter.Applied);
		Assert.AreEqual("fresh", host.Presenter.Applied[0].Items![0].Label);
	}

	[TestMethod]
	public void HandleCharacterTyped_FailingScopeHook_IsContainedAndLogged()
	{
		using var host = new CompletionControllerTestHost { ScopeAt = static _ => throw new InvalidOperationException("The scope hook failed.") };
		using CompletionController controller = host.CreateController();

		controller.HandleCharacterTyped('a', 1);

		Assert.AreEqual(1003, host.Logger.Entries.Single().EventId.Id);
		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
	}

	[TestMethod]
	public void HandleCharacterTyped_ScopeAnsweringNone_SuppressesTriggerCharactersToo()
	{
		using var host = new CompletionControllerTestHost
		{
			Provider = new ScriptedCompletionProvider { TriggerCharacters = ["."] },
			ScopeAt = static _ => CompletionScopes.None,
		};

		using CompletionController controller = host.CreateController();

		controller.HandleCharacterTyped('a', 1);
		controller.HandleCharacterTyped('.', 2);

		host.Dispatcher.Advance(CompletionControllerTestHost.s_debounceDelay * 5);

		Assert.IsEmpty(host.Provider.Requests);
		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
	}

	[TestMethod]
	public void HandleCharacterTyped_ManualPolicy_OpensNothingAndTheHostStillRequests()
	{
		using var host = new CompletionControllerTestHost
		{
			Provider = new ScriptedCompletionProvider { TriggerCharacters = ["."], Answer = ScriptedCompletionProvider.Answering("member").Answer },
			Options = CompletionControllerOptions.Default with { Triggers = CompletionTriggerPolicy.Manual },
		};

		using CompletionController controller = host.CreateController();

		controller.HandleCharacterTyped('a', 1);
		controller.HandleCharacterTyped('.', 2);

		host.Dispatcher.Advance(CompletionControllerTestHost.s_debounceDelay * 5);

		Assert.IsEmpty(host.Provider.Requests);

		Assert.IsTrue(host.RunToCompletion(controller.RequestAsync()));
		Assert.HasCount(1, host.Provider.Requests);
	}

	[TestMethod]
	public void HandleCharacterTyped_ProviderWithoutTriggerCharacters_TreatsADotAsNoWord()
	{
		using var host = new CompletionControllerTestHost();
		using CompletionController controller = host.CreateController();

		controller.HandleCharacterTyped('.', 1);
		host.Dispatcher.Advance(CompletionControllerTestHost.s_debounceDelay * 5);

		Assert.IsEmpty(host.Provider.Requests);
		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
	}

	[TestMethod]
	public void HandleCharacterTyped_AfterTheDocumentSwap_RequestsNothing()
	{
		int scopeReads = 0;

		using var host = new CompletionControllerTestHost
		{
			Provider = new ScriptedCompletionProvider { TriggerCharacters = ["."] },

			ScopeAt = _ =>
			{
				scopeReads++;
				return CompletionScopes.Code;
			},
		};

		using CompletionController controller = host.CreateController();

		controller.HandleDocumentReplaced();

		controller.HandleCharacterTyped('.', 1);
		controller.HandleCharacterTyped('a', 2);

		host.Dispatcher.Advance(CompletionControllerTestHost.s_debounceDelay * 5);

		// The character is not even evaluated, so the scope hook of the host is not asked.
		Assert.AreEqual(0, scopeReads);
		Assert.IsEmpty(host.Provider.Requests);
		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
	}

	[TestMethod]
	public void HandleCharacterTyped_AfterDisposal_RequestsNothing()
	{
		int scopeReads = 0;

		using var host = new CompletionControllerTestHost
		{
			Provider = new ScriptedCompletionProvider { TriggerCharacters = ["."] },

			ScopeAt = _ =>
			{
				scopeReads++;
				return CompletionScopes.Code;
			},
		};

		CompletionController controller = host.CreateController();

		controller.Dispose();

		controller.HandleCharacterTyped('.', 1);
		controller.HandleCharacterTyped('a', 2);

		host.Dispatcher.Advance(CompletionControllerTestHost.s_debounceDelay * 5);

		// The character is not even evaluated, so the scope hook of the host is not asked.
		Assert.AreEqual(0, scopeReads);
		Assert.IsEmpty(host.Provider.Requests);
		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
	}
}
