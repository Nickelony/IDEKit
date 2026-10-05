using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Editing;
using Nickelony.Testing;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>
/// Covers how the controller runs a chosen action: the provider resolves an action without an edit, the host's hook applies it, and the provider
/// then runs its command, each only for the action the user chose and only while the editor still holds the text the action was requested for.
/// </summary>
public sealed partial class CodeActionControllerTests
{
	private static readonly WorkspaceEdit s_edit = new([
		new TextDocumentEdit("a.lua", [new TextEdit(new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 1)), "x")]),
	]);

	private static readonly CodeActionCommand s_command = new("Remove spaces", "lua.removeSpace", ["file:///a.lua"]);

	/// <summary>
	/// A provider that offers what the test publishes, resolves and runs commands as the test scripts, and logs each call in the order it happens.
	/// </summary>
	private sealed class ScriptedProvider(CodeActionTestHost host, List<string> events)
		: ICodeActionProvider,
			ICodeActionResolver,
			ICodeActionCommandRunner
	{
		internal List<CodeAction> Resolved { get; } = [];

		internal List<CodeActionCommand> Ran { get; } = [];

		internal Func<CodeAction, CancellationToken, Task<CodeAction>> Resolve { get; set; } = static (action, _) => Task.FromResult(action);

		internal Func<bool> RunCommand { get; set; } = static () => true;

		public Task<IReadOnlyList<CodeAction>> GetCodeActionsAsync(CodeActionRequest request, CancellationToken cancellationToken = default) =>
			host.GetCodeActionsAsync(request, cancellationToken);

		public Task<CodeAction> ResolveAsync(CodeAction action, CancellationToken cancellationToken = default)
		{
			events.Add("resolve");
			Resolved.Add(action);

			return Resolve(action, cancellationToken);
		}

		public Task<bool> TryRunAsync(CodeActionCommand command, CancellationToken cancellationToken = default)
		{
			events.Add("run");
			Ran.Add(command);

			return Task.FromResult(RunCommand());
		}
	}

	private static (
		CodeActionTestHost Host,
		ScriptedProvider Provider,
		CodeActionController Controller,
		List<string> Events
	) CreateResolvingController(RecordingLogger? logger = null)
	{
		var host = new CodeActionTestHost();
		var events = new List<string>();
		var provider = new ScriptedProvider(host, events);

		host.ExecuteActionAsync = _ =>
		{
			events.Add("apply");
			return Task.CompletedTask;
		};

		return (host, provider, host.CreateController(logger: logger, provider: provider), events);
	}

	private static void Publish(CodeActionTestHost host, CodeActionController controller, params CodeAction[] actions)
	{
		host.RequestCodeActionsAsync = (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>(actions);
		host.RunToCompletion(controller.RequestAsync());
	}

	private static CodeActionExecutionOutcome Execute(CodeActionTestHost host, CodeActionController controller, CodeAction action)
	{
		Task<CodeActionExecutionOutcome> execution = controller.ExecuteActionAsync(action);

		host.RunToCompletion(execution);

		return execution.Result;
	}

	[TestMethod]
	public void ExecuteActionAsync_ResolvesAnActionWithoutAnEditBeforeTheHookApplies()
	{
		(CodeActionTestHost host, ScriptedProvider provider, CodeActionController controller, List<string> events) = CreateResolvingController();

		using (host)
		{
			using (controller)
			{
				var resolved = new CodeAction("Add using", s_edit, kind: "quickfix");

				provider.Resolve = (_, _) => Task.FromResult(resolved);

				Publish(host, controller, new CodeAction("Add using", kind: "quickfix", data: "offer"));

				Assert.AreEqual(CodeActionExecutionOutcome.Completed, Execute(host, controller, controller.Actions[0]));
				Assert.AreSequenceEqual(["resolve", "apply"], events);
				Assert.HasCount(1, host.Executed);
				Assert.AreSame(resolved, host.Executed[0]);
				Assert.AreEqual("Add using", provider.Resolved[0].Title);
			}
		}
	}

	[TestMethod]
	public void ExecuteActionAsync_ResolvesNothingUntilTheUserChoosesAnAction_AndThenOnlyThatOne()
	{
		(CodeActionTestHost host, ScriptedProvider provider, CodeActionController controller, List<string> events) = CreateResolvingController();

		using (host)
		{
			using (controller)
			{
				Publish(
					host,
					controller,
					new CodeAction("First", data: "one"),
					new CodeAction("Second", data: "two"),
					new CodeAction("Third", data: "three")
				);

				Assert.HasCount(3, controller.Actions);
				Assert.IsEmpty(provider.Resolved);
				Assert.IsEmpty(events);

				Execute(host, controller, controller.Actions[1]);

				Assert.HasCount(1, provider.Resolved);
				Assert.AreEqual("Second", provider.Resolved[0].Title);
			}
		}
	}

	[TestMethod]
	public void ExecuteActionAsync_DoesNotResolveAnActionThatCarriesAnEdit()
	{
		(CodeActionTestHost host, ScriptedProvider provider, CodeActionController controller, List<string> events) = CreateResolvingController();

		using (host)
		{
			using (controller)
			{
				Publish(host, controller, new CodeAction("Fix it", s_edit, data: "offer"));

				Assert.AreEqual(CodeActionExecutionOutcome.Completed, Execute(host, controller, controller.Actions[0]));
				Assert.IsEmpty(provider.Resolved);
				Assert.AreSequenceEqual(["apply"], events);
			}
		}
	}

	[TestMethod]
	public void ExecuteActionAsync_RunsTheCommandOnlyAfterTheHookApplied()
	{
		(CodeActionTestHost host, ScriptedProvider provider, CodeActionController controller, List<string> events) = CreateResolvingController();

		using (host)
		{
			using (controller)
			{
				Publish(host, controller, new CodeAction("Fix and clean", s_edit, command: s_command));

				Assert.AreEqual(CodeActionExecutionOutcome.Completed, Execute(host, controller, controller.Actions[0]));
				Assert.AreSequenceEqual(["apply", "run"], events);
				Assert.AreSame(s_command, provider.Ran[0]);
			}
		}
	}

	[TestMethod]
	public void ExecuteActionAsync_RunsTheCommandOfAnActionWithoutAnEdit()
	{
		(CodeActionTestHost host, ScriptedProvider provider, CodeActionController controller, List<string> events) = CreateResolvingController();

		using (host)
		{
			using (controller)
			{
				Publish(host, controller, new CodeAction("Mark as defined global", command: s_command));

				Assert.AreEqual(CodeActionExecutionOutcome.Completed, Execute(host, controller, controller.Actions[0]));
				Assert.AreSequenceEqual(["apply", "run"], events);
				Assert.IsNull(host.Executed[0].Edit);
			}
		}
	}

	[TestMethod]
	public void ExecuteActionAsync_DoesNotRunTheCommandWhenTheHookFails()
	{
		var logger = new RecordingLogger();

		(CodeActionTestHost host, ScriptedProvider provider, CodeActionController controller, List<string> events) = CreateResolvingController(
			logger
		);

		using (host)
		{
			using (controller)
			{
				host.ExecuteActionAsync = _ =>
				{
					events.Add("apply");
					throw new InvalidOperationException("The edit was refused.");
				};

				Publish(host, controller, new CodeAction("Fix and clean", s_edit, command: s_command));

				Assert.AreEqual(CodeActionExecutionOutcome.HookFailed, Execute(host, controller, controller.Actions[0]));
				Assert.AreSequenceEqual(["apply"], events);
				Assert.IsEmpty(provider.Ran);
				Assert.AreEqual(1031, logger.Entries[^1].EventId.Id);
			}
		}
	}

	[TestMethod]
	public void ExecuteActionAsync_ReportsACommandThatTheProviderDidNotRun()
	{
		var logger = new RecordingLogger();
		(CodeActionTestHost host, ScriptedProvider provider, CodeActionController controller, _) = CreateResolvingController(logger);

		using (host)
		{
			using (controller)
			{
				provider.RunCommand = static () => false;

				Publish(host, controller, new CodeAction("Mark as defined global", command: s_command));

				Assert.AreEqual(CodeActionExecutionOutcome.CommandFailed, Execute(host, controller, controller.Actions[0]));
				Assert.AreEqual(1034, logger.Entries[^1].EventId.Id);
			}
		}
	}

	[TestMethod]
	public void ExecuteActionAsync_ReportsACommandThatTheProviderFailedToRun()
	{
		var logger = new RecordingLogger();
		(CodeActionTestHost host, ScriptedProvider provider, CodeActionController controller, _) = CreateResolvingController(logger);

		using (host)
		{
			using (controller)
			{
				provider.RunCommand = static () => throw new InvalidOperationException("The server failed.");

				Publish(host, controller, new CodeAction("Mark as defined global", command: s_command));

				Assert.AreEqual(CodeActionExecutionOutcome.CommandFailed, Execute(host, controller, controller.Actions[0]));
				Assert.AreEqual(1030, logger.Entries[^1].EventId.Id);
			}
		}
	}

	[TestMethod]
	public void ExecuteActionAsync_RefusesAnActionWhoseDocumentChangedSinceItWasOffered()
	{
		var logger = new RecordingLogger();

		(CodeActionTestHost host, ScriptedProvider provider, CodeActionController controller, List<string> events) = CreateResolvingController(
			logger
		);

		using (host)
		{
			using (controller)
			{
				Publish(host, controller, new CodeAction("Add using", data: "offer"), new CodeAction("Mark as defined global", command: s_command));

				host.Source.EditDocument("one\ntwo\nthree\nfour");

				Assert.AreEqual(CodeActionExecutionOutcome.Stale, Execute(host, controller, controller.Actions[0]));
				Assert.AreEqual(CodeActionExecutionOutcome.Stale, Execute(host, controller, controller.Actions[1]));

				// Neither the resolution, the hook nor the command ran against the newer text.
				Assert.IsEmpty(events);
				Assert.IsEmpty(host.Executed);
				Assert.AreEqual(1032, logger.Entries[^1].EventId.Id);
			}
		}
	}

	[TestMethod]
	public void ExecuteActionAsync_RefusesAnEditOnlyActionWhoseDocumentChangedSinceItWasOffered()
	{
		var logger = new RecordingLogger();
		(CodeActionTestHost host, _, CodeActionController controller, List<string> events) = CreateResolvingController(logger);

		using (host)
		{
			using (controller)
			{
				Publish(host, controller, new CodeAction("Fix it", s_edit));

				host.Source.EditDocument("one\ntwo\nthree\nfour");

				Assert.AreEqual(CodeActionExecutionOutcome.Stale, Execute(host, controller, controller.Actions[0]));
				Assert.IsEmpty(events);
				Assert.IsEmpty(host.Executed);
				Assert.AreEqual(1032, logger.Entries[^1].EventId.Id);
			}
		}
	}

	[TestMethod]
	public void ExecuteActionAsync_ComparesTheEditorWithTheTextOfTheSelectionNotWithTheTextTheHostPutInTheRequest()
	{
		(CodeActionTestHost host, _, CodeActionController controller, List<string> events) = CreateResolvingController();

		using (host)
		{
			using (controller)
			{
				// A request builder that reports other text than the editor holds cannot make a stale action look current.
				host.BuildRequest = static _ => new CodeActionRequest(CodeActionTestHost.DocumentId, "unrelated", new TextRange(0, 0));

				Publish(host, controller, new CodeAction("Fix it", s_edit));

				Assert.AreEqual(CodeActionExecutionOutcome.Completed, Execute(host, controller, controller.Actions[0]));
				Assert.AreSequenceEqual(["apply"], events);

				host.Source.EditDocument("one\ntwo\nthree\nfour");

				Assert.AreEqual(CodeActionExecutionOutcome.Stale, Execute(host, controller, controller.Actions[0]));
				Assert.AreSequenceEqual(["apply"], events);
			}
		}
	}

	[TestMethod]
	public void ExecuteActionAsync_RefusesAnActionThatANewerPublicationReplaced()
	{
		(CodeActionTestHost host, ScriptedProvider provider, CodeActionController controller, List<string> events) = CreateResolvingController();

		using (host)
		{
			using (controller)
			{
				Publish(host, controller, new CodeAction("Old", data: "old"));

				CodeAction old = controller.Actions[0];

				Publish(host, controller, new CodeAction("New", data: "new"));

				Assert.AreEqual(CodeActionExecutionOutcome.Stale, Execute(host, controller, old));
				Assert.IsEmpty(events);
				Assert.IsEmpty(provider.Resolved);
			}
		}
	}

	[TestMethod]
	public void ExecuteActionAsync_RefusesAnActionThatWasNeverPublished()
	{
		(CodeActionTestHost host, _, CodeActionController controller, List<string> events) = CreateResolvingController();

		using (host)
		{
			using (controller)
			{
				Assert.AreEqual(CodeActionExecutionOutcome.Stale, Execute(host, controller, new CodeAction("Fix it", s_edit)));
				Assert.IsEmpty(events);
			}
		}
	}

	[TestMethod]
	public void ExecuteActionAsync_WhenTheTextChangesWhileTheProviderResolves_AppliesNothing()
	{
		var logger = new RecordingLogger();

		(CodeActionTestHost host, ScriptedProvider provider, CodeActionController controller, List<string> events) = CreateResolvingController(
			logger
		);

		using (host)
		{
			using (controller)
			{
				var gate = new TaskCompletionSource<CodeAction>(TaskCreationOptions.RunContinuationsAsynchronously);

				provider.Resolve = (_, _) => gate.Task;

				Publish(host, controller, new CodeAction("Add using", data: "offer"));

				Task<CodeActionExecutionOutcome> execution = controller.ExecuteActionAsync(controller.Actions[0]);

				host.Source.EditDocument("one\ntwo\nthree\nfour");
				gate.SetResult(new CodeAction("Add using", s_edit));
				host.RunToCompletion(execution);

				Assert.AreEqual(CodeActionExecutionOutcome.Stale, execution.Result);
				Assert.AreSequenceEqual(["resolve"], events);
				Assert.IsEmpty(host.Executed);
				Assert.AreEqual(1032, logger.Entries[^1].EventId.Id);
			}
		}
	}

	[TestMethod]
	public void ExecuteActionAsync_WhenTheTextChangesAndTheDebounceRepublishesWhileTheProviderResolves_AppliesNothing()
	{
		(CodeActionTestHost host, ScriptedProvider provider, CodeActionController controller, List<string> events) = CreateResolvingController();

		using (host)
		{
			using (controller)
			{
				var gate = new TaskCompletionSource<CodeAction>(TaskCreationOptions.RunContinuationsAsynchronously);

				provider.Resolve = (_, _) => gate.Task;

				Publish(host, controller, new CodeAction("Add using", data: "offer"));

				CodeAction offered = controller.Actions[0];
				Task<CodeActionExecutionOutcome> execution = controller.ExecuteActionAsync(offered);

				// The user types, the debounce settles, and the controller publishes the actions it requested for the newer text.
				host.Source.EditDocument("one\ntwo\nthree\nfour");

				host.RequestCodeActionsAsync = static (_, _) =>
					Task.FromResult<IReadOnlyList<CodeAction>>([new CodeAction("Add using", data: "newer offer")]);

				host.SettleDebounce();
				host.Dispatcher.PumpUntilIdle();

				Assert.AreEqual("one\ntwo\nthree\nfour", host.Requests[^1].DocumentText);
				Assert.AreNotEqual(offered, controller.Actions[0]);

				gate.SetResult(new CodeAction("Add using", s_edit));
				host.RunToCompletion(execution);

				Assert.AreEqual(CodeActionExecutionOutcome.Stale, execution.Result);
				Assert.AreSequenceEqual(["resolve"], events);
				Assert.IsEmpty(host.Executed);
			}
		}
	}

	[TestMethod]
	public void ExecuteActionAsync_WhenOnlyTheCaretMovesWhileTheProviderResolves_AppliesTheAction()
	{
		(CodeActionTestHost host, ScriptedProvider provider, CodeActionController controller, List<string> events) = CreateResolvingController();

		using (host)
		{
			using (controller)
			{
				var gate = new TaskCompletionSource<CodeAction>(TaskCreationOptions.RunContinuationsAsynchronously);

				provider.Resolve = (_, _) => gate.Task;

				Publish(host, controller, new CodeAction("Add using", data: "offer"));

				Task<CodeActionExecutionOutcome> execution = controller.ExecuteActionAsync(controller.Actions[0]);

				host.Source.MoveCaret(2);
				gate.SetResult(new CodeAction("Add using", s_edit));
				host.RunToCompletion(execution);

				Assert.AreEqual(CodeActionExecutionOutcome.Completed, execution.Result);
				Assert.AreSequenceEqual(["resolve", "apply"], events);
			}
		}
	}

	[TestMethod]
	public void ExecuteActionAsync_ASecondCallForAnActionThatIsStillRunning_DoesNothing()
	{
		(CodeActionTestHost host, ScriptedProvider provider, CodeActionController controller, List<string> events) = CreateResolvingController();

		using (host)
		{
			using (controller)
			{
				var gate = new TaskCompletionSource<CodeAction>(TaskCreationOptions.RunContinuationsAsynchronously);

				provider.Resolve = (_, _) => gate.Task;

				Publish(host, controller, new CodeAction("Add using", data: "offer"));

				CodeAction action = controller.Actions[0];

				Task<CodeActionExecutionOutcome> first = controller.ExecuteActionAsync(action);
				Task<CodeActionExecutionOutcome> second = controller.ExecuteActionAsync(action);

				Assert.IsTrue(second.IsCompleted);
				Assert.AreEqual(CodeActionExecutionOutcome.AlreadyRunning, second.Result);

				gate.SetResult(new CodeAction("Add using", s_edit));
				host.RunToCompletion(first);

				Assert.AreEqual(CodeActionExecutionOutcome.Completed, first.Result);
				Assert.AreSequenceEqual(["resolve", "apply"], events);
				Assert.HasCount(1, host.Executed);
			}
		}
	}

	[TestMethod]
	public void ExecuteActionAsync_AfterTheFirstCallEnded_AllowsTheSameActionAgain()
	{
		(CodeActionTestHost host, _, CodeActionController controller, List<string> events) = CreateResolvingController();

		using (host)
		{
			using (controller)
			{
				int calls = 0;

				// The first hook call fails, so the guard for the action must clear after a failure and after a success.
				host.ExecuteActionAsync = _ =>
				{
					events.Add("apply");
					return ++calls == 1 ? throw new InvalidOperationException("The edit was refused.") : Task.CompletedTask;
				};

				Publish(host, controller, new CodeAction("Fix it", s_edit));

				CodeAction action = controller.Actions[0];

				Assert.AreEqual(CodeActionExecutionOutcome.HookFailed, Execute(host, controller, action));
				Assert.AreEqual(CodeActionExecutionOutcome.Completed, Execute(host, controller, action));
				Assert.AreEqual(CodeActionExecutionOutcome.Completed, Execute(host, controller, action));
				Assert.AreSequenceEqual(["apply", "apply", "apply"], events);
			}
		}
	}

	[TestMethod]
	public void ExecuteActionAsync_WhenTheResolutionLeavesNeitherAnEditNorACommand_AppliesNothingAndLogsIt()
	{
		var logger = new RecordingLogger();
		(CodeActionTestHost host, _, CodeActionController controller, List<string> events) = CreateResolvingController(logger);

		using (host)
		{
			using (controller)
			{
				Publish(host, controller, new CodeAction("Add using", data: "offer"));

				Assert.AreEqual(CodeActionExecutionOutcome.NotResolved, Execute(host, controller, controller.Actions[0]));
				Assert.AreSequenceEqual(["resolve"], events);
				Assert.IsEmpty(host.Executed);
				Assert.AreEqual(1033, logger.Entries[^1].EventId.Id);
				Assert.AreEqual("CodeActionNotResolved", logger.Entries[^1].EventId.Name);
			}
		}
	}

	[TestMethod]
	public void ExecuteActionAsync_WhenTheProviderFailsToResolve_AppliesNothingAndLogsIt()
	{
		var logger = new RecordingLogger();

		(CodeActionTestHost host, ScriptedProvider provider, CodeActionController controller, List<string> events) = CreateResolvingController(
			logger
		);

		using (host)
		{
			using (controller)
			{
				provider.Resolve = static (_, _) => throw new InvalidOperationException("The server failed.");

				Publish(host, controller, new CodeAction("Add using", data: "offer"));

				Assert.AreEqual(CodeActionExecutionOutcome.NotResolved, Execute(host, controller, controller.Actions[0]));
				Assert.AreSequenceEqual(["resolve"], events);
				Assert.AreEqual(1030, logger.Entries[^1].EventId.Id);
			}
		}
	}

	[TestMethod]
	public void ExecuteActionAsync_RunsTheCommandThatTheResolutionAdded()
	{
		(CodeActionTestHost host, ScriptedProvider provider, CodeActionController controller, List<string> events) = CreateResolvingController();

		using (host)
		{
			using (controller)
			{
				provider.Resolve = (_, _) => Task.FromResult(new CodeAction("Add using", command: s_command));

				Publish(host, controller, new CodeAction("Add using", data: "offer"));

				Assert.AreEqual(CodeActionExecutionOutcome.Completed, Execute(host, controller, controller.Actions[0]));
				Assert.AreSequenceEqual(["resolve", "apply", "run"], events);
			}
		}
	}

	[TestMethod]
	public void ExecuteActionAsync_ForAProviderThatCannotResolveOrRun_HandsTheActionToTheHookAsItIs()
	{
		using var host = new CodeActionTestHost();
		using CodeActionController controller = host.CreateController();

		Publish(host, controller, new CodeAction("Add using", data: "offer", command: s_command));

		CodeAction action = controller.Actions[0];

		Assert.AreEqual(CodeActionExecutionOutcome.Completed, Execute(host, controller, action));
		Assert.HasCount(1, host.Executed);
		Assert.AreSame(action, host.Executed[0]);
	}

	[TestMethod]
	public void ExecuteActionAsync_DisposedWhileTheProviderResolves_StopsWithoutLoggingAFailure()
	{
		var logger = new RecordingLogger();

		(CodeActionTestHost host, ScriptedProvider provider, CodeActionController controller, List<string> events) = CreateResolvingController(
			logger
		);

		using (host)
		{
			provider.Resolve = async (action, cancellationToken) =>
			{
				await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
				return action;
			};

			Publish(host, controller, new CodeAction("Add using", data: "offer"));

			Task<CodeActionExecutionOutcome> execution = controller.ExecuteActionAsync(controller.Actions[0]);

			controller.Dispose();
			host.RunToCompletion(execution);

			Assert.AreEqual(CodeActionExecutionOutcome.Disposed, execution.Result);
			Assert.AreSequenceEqual(["resolve"], events);
			Assert.IsEmpty(logger.Entries.Where(static entry => entry.EventId.Id is 1030 or 1031 or 1033));
		}
	}

	[TestMethod]
	public void ExecuteActionAsync_DisposedBetweenTheEditAndTheCommand_RunsNoCommand()
	{
		(CodeActionTestHost host, ScriptedProvider provider, CodeActionController controller, List<string> events) = CreateResolvingController();

		using (host)
		{
			host.ExecuteActionAsync = _ =>
			{
				events.Add("apply");
				controller.Dispose();

				return Task.CompletedTask;
			};

			Publish(host, controller, new CodeAction("Fix and clean", s_edit, command: s_command));

			Assert.AreEqual(CodeActionExecutionOutcome.Disposed, Execute(host, controller, controller.Actions[0]));
			Assert.AreSequenceEqual(["apply"], events);
			Assert.IsEmpty(provider.Ran);
		}
	}

	[TestMethod]
	public void ExecuteActionAsync_AfterDisposal_ReportsTheDisposal()
	{
		using var host = new CodeActionTestHost();
		CodeActionController controller = host.CreateController();

		controller.Dispose();

		Assert.AreEqual(CodeActionExecutionOutcome.Disposed, Execute(host, controller, new CodeAction("Fix it", s_edit)));
		Assert.IsEmpty(host.Executed);
	}
}
