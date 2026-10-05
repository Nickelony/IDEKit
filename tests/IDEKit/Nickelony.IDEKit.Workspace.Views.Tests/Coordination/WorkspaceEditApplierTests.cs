using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Editing;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;
using Nickelony.IDEKit.Workspace.Replacement;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

[TestClass]
public sealed class WorkspaceEditApplierTests
{
	private const string FirstText = "local value = 1\nprint(value)";
	private const string SecondText = "return value";

	private static readonly WorkspaceDocumentOpenOptions s_openOptions = new([TextEncoding.Utf8], TestSnapshots.FileFormat);

	public required TestContext TestContext { get; set; }

	private static TextEdit Edit(int line, int startCharacter, int endCharacter, string newText) =>
		new(new TextPositionRange(new TextPosition(line, startCharacter), new TextPosition(line, endCharacter)), newText);

	// Renames "value" to "renamed" in both files.
	private static WorkspaceEdit Rename(TestHost host)
	{
		return new([
			new TextDocumentEdit(
				host.First,

				[
					Edit(line: 0, startCharacter: 6, endCharacter: 11, newText: "renamed"),
					Edit(line: 1, startCharacter: 6, endCharacter: 11, newText: "renamed"),
				]
			),

			new TextDocumentEdit(host.Second, [Edit(line: 0, startCharacter: 7, endCharacter: 12, newText: "renamed")]),
		]);
	}

	[TestMethod]
	public async Task ApplyAsync_AnEditOverTwoDocuments_UpdatesEveryViewOfBoth()
	{
		await using var host = new TestHost();

		TextView left = await host.AttachAsync(host.First, "left");
		TextView right = await host.AttachAsync(host.First, "right");
		TextView other = await host.AttachAsync(host.Second, "other");

		WorkspaceEditApplyResult result = await new WorkspaceEditApplier(host.Coordinator).ApplyAsync(Rename(host), TestContext.CancellationToken);

		Assert.IsTrue(result.Plan.Succeeded);
		Assert.AreEqual(DocumentReplacementOutcome.Completed, result.Replacement?.Outcome);
		Assert.AreSequenceEqual([host.First, host.Second], result.Replacement!.ChangedDocumentIds);
		Assert.AreEqual("local renamed = 1\nprint(renamed)", host.ContentOf(host.First));
		Assert.AreEqual("return renamed", host.ContentOf(host.Second));
		Assert.AreEqual("local renamed = 1\nprint(renamed)", left.Text);
		Assert.AreEqual("local renamed = 1\nprint(renamed)", right.Text);
		Assert.AreEqual("return renamed", other.Text);
		Assert.IsFalse(left.HasPendingEdits || right.HasPendingEdits || other.HasPendingEdits);
	}

	[TestMethod]
	public async Task ApplyAsync_PublishesThePendingEditsBeforeThePlan_SoTheEditAddressesTheTypedText()
	{
		await using var host = new TestHost();
		TextView view = await host.AttachAsync(host.First, "typing");
		view.Type("local value = 2\nprint(value)");

		WorkspaceEditApplyResult result = await new WorkspaceEditApplier(host.Coordinator).ApplyAsync(
			new WorkspaceEdit([new TextDocumentEdit(host.First, [Edit(line: 0, startCharacter: 6, endCharacter: 11, newText: "renamed")])]),
			TestContext.CancellationToken
		);

		Assert.AreEqual(DocumentReplacementOutcome.Completed, result.Replacement?.Outcome);
		Assert.AreEqual("local value = 2\nprint(value)", result.Plan.Targets.Single().BeforeContent);
		Assert.AreEqual("local renamed = 2\nprint(value)", host.ContentOf(host.First));
		Assert.AreEqual("local renamed = 2\nprint(value)", view.Text);
	}

	[TestMethod]
	public async Task ApplyAsync_AFailedPlan_AppliesNothing()
	{
		await using var host = new TestHost();
		TextView first = await host.AttachAsync(host.First, "first");

		WorkspaceEditApplyResult result = await new WorkspaceEditApplier(host.Coordinator).ApplyAsync(
			new WorkspaceEdit([
				new TextDocumentEdit(host.First, [Edit(line: 0, startCharacter: 6, endCharacter: 11, newText: "renamed")]),
				new TextDocumentEdit(host.Second, [new TextEdit(new TextPositionRange(new TextPosition(0, 5), new TextPosition(0, 2)), "x")]),
			]),

			TestContext.CancellationToken
		);

		Assert.IsFalse(result.Plan.Succeeded);
		Assert.IsNull(result.Replacement);
		Assert.AreEqual(FirstText, host.ContentOf(host.First));
		Assert.AreEqual(FirstText, first.Text);
	}

	[TestMethod]
	public async Task ApplyAsync_AViewThatCannotPublish_IsAPlanIssue_AndNothingApplies()
	{
		await using var host = new TestHost();
		var held = new FakeWorkspaceView("held");

		await host.Coordinator.OpenWithViewAsync(host.First, s_openOptions, held);
		held.HasPendingEdits = true;
		TextView other = await host.AttachAsync(host.Second, "other");

		WorkspaceEditApplyResult result = await new WorkspaceEditApplier(host.Coordinator).ApplyAsync(Rename(host), TestContext.CancellationToken);

		WorkspaceEditPlanIssue issue = Assert.ContainsSingle(result.Plan.Issues);

		Assert.AreEqual(host.First, issue.DocumentId);
		Assert.AreEqual(WorkspaceEditPlanIssueKind.PendingEditsNotPublished, issue.Kind);
		Assert.IsNull(result.Replacement);
		Assert.AreEqual(FirstText, host.ContentOf(host.First));
		Assert.AreEqual(SecondText, host.ContentOf(host.Second));
		Assert.AreEqual(SecondText, other.Text);
	}

	[TestMethod]
	public async Task ApplyAsync_ALaterDocumentTheStoreRejects_KeepsTheEarlierChangeAndReportsBoth()
	{
		await using var host = new TestHost();
		TextView first = await host.AttachAsync(host.First, "first");
		await host.AttachAsync(host.Second, "second");

		// The user's typing reaches the second document while the first one is replaced, after the plan read it.
		first.OnReplaced = () => host.Type(host.Second, "return typed");

		WorkspaceEditApplyResult result = await new WorkspaceEditApplier(host.Coordinator).ApplyAsync(Rename(host), TestContext.CancellationToken);

		DocumentReplacementResult replacement = result.Replacement!;
		Assert.AreEqual(DocumentReplacementOutcome.PartiallyApplied, replacement.Outcome);

		Assert.AreSequenceEqual(
			[DocumentReplacementTargetOutcome.Applied, DocumentReplacementTargetOutcome.NotApplied],
			replacement.TargetResults.Select(target => target.Outcome)
		);

		Assert.AreEqual(DocumentReplacementFailureCodes.TargetNotChanged, replacement.Failure?.Code);
		Assert.AreSequenceEqual([host.First], replacement.ChangedDocumentIds);
		Assert.AreEqual("local renamed = 1\nprint(renamed)", host.ContentOf(host.First));
		Assert.AreEqual("return typed", host.ContentOf(host.Second));
	}

	[TestMethod]
	public async Task ApplyAsync_ATokenCanceledBeforeTheApply_ThrowsAndChangesNothing()
	{
		await using var host = new TestHost();
		using var cancellation = new CancellationTokenSource();

		TextView first = await host.AttachAsync(host.First, "first");
		await host.AttachAsync(host.Second, "second");
		await cancellation.CancelAsync();

		await Assert.ThrowsAsync<OperationCanceledException>(() =>
			new WorkspaceEditApplier(host.Coordinator).ApplyAsync(Rename(host), cancellation.Token)
		);

		Assert.AreEqual(FirstText, host.ContentOf(host.First));
		Assert.AreEqual(SecondText, host.ContentOf(host.Second));
		Assert.AreEqual(FirstText, first.Text);
	}

	[TestMethod]
	public async Task ApplyAsync_ATokenCanceledWhileADocumentPublishes_PublishesNoLaterDocumentAndThrows()
	{
		await using var host = new TestHost();
		using var cancellation = new CancellationTokenSource();

		TextView typing = await host.AttachAsync(host.First, "typing");
		TextView peer = await host.AttachAsync(host.First, "peer");
		TextView later = await host.AttachAsync(host.Second, "later");

		typing.Type("local value = 2\nprint(value)");
		later.Type("return typed");

		peer.OnReplaced = cancellation.Cancel;

		await Assert.ThrowsAsync<OperationCanceledException>(() =>
			new WorkspaceEditApplier(host.Coordinator).ApplyAsync(Rename(host), cancellation.Token)
		);

		Assert.AreEqual("local value = 2\nprint(value)", host.ContentOf(host.First));
		Assert.AreEqual(SecondText, host.ContentOf(host.Second));
		Assert.IsTrue(later.HasPendingEdits);
	}

	[TestMethod]
	public async Task ApplyAsync_ATokenCanceledWhileTheLastDocumentPublishes_ThrowsBeforeThePlan()
	{
		await using var host = new TestHost();
		using var cancellation = new CancellationTokenSource();

		TextView typing = await host.AttachAsync(host.First, "typing");
		TextView peer = await host.AttachAsync(host.First, "peer");

		typing.Type("local value = 2\nprint(value)");
		peer.OnReplaced = cancellation.Cancel;

		await Assert.ThrowsAsync<OperationCanceledException>(() =>
			new WorkspaceEditApplier(host.Coordinator).ApplyAsync(
				new WorkspaceEdit([new TextDocumentEdit(host.First, [Edit(line: 0, startCharacter: 6, endCharacter: 11, newText: "renamed")])]),
				cancellation.Token
			)
		);

		Assert.AreEqual("local value = 2\nprint(value)", host.ContentOf(host.First));
	}

	[TestMethod]
	public async Task ApplyAsync_ATokenCanceledBetweenTwoDocuments_KeepsTheFirstAndReportsTheCancellation()
	{
		await using var host = new TestHost();
		using var cancellation = new CancellationTokenSource();

		TextView first = await host.AttachAsync(host.First, "first");
		await host.AttachAsync(host.Second, "second");
		first.OnReplaced = cancellation.Cancel;

		WorkspaceEditApplyResult result = await new WorkspaceEditApplier(host.Coordinator).ApplyAsync(Rename(host), cancellation.Token);

		Assert.AreEqual(DocumentReplacementOutcome.PartiallyApplied, result.Replacement?.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.Canceled, result.Replacement!.Failure?.Code);
		Assert.AreSequenceEqual([host.First], result.Replacement.ChangedDocumentIds);
		Assert.AreEqual(SecondText, host.ContentOf(host.Second));
	}

	[TestMethod]
	public async Task ApplyAsync_AViewThatTypesAfterThePlan_KeepsItsTextAndItsDocumentIsNotReplaced()
	{
		await using var host = new TestHost();

		TextView first = await host.AttachAsync(host.First, "first");
		TextView second = await host.AttachAsync(host.Second, "second");

		// The user types in the second document while the first one is replaced, after the plan read the second document.
		first.OnReplaced = () => second.Type("return typed");

		WorkspaceEditApplyResult result = await new WorkspaceEditApplier(host.Coordinator).ApplyAsync(Rename(host), TestContext.CancellationToken);

		DocumentReplacementResult replacement = result.Replacement!;

		Assert.AreEqual(DocumentReplacementOutcome.PartiallyApplied, replacement.Outcome);
		Assert.AreEqual(DocumentReplacementTargetOutcome.NotApplied, replacement.TargetResults[1].Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewHoldsPendingEdits, replacement.TargetResults[1].Failure?.Code);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, result.ViewSynchronization[host.Second].Outcome);
		Assert.AreEqual("second", Assert.ContainsSingle(result.ViewSynchronization[host.Second].Issues).ViewId);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, result.ViewSynchronization[host.First].Outcome);
		Assert.AreSequenceEqual([host.First], replacement.ChangedDocumentIds);
		Assert.AreEqual(SecondText, host.ContentOf(host.Second));
		Assert.AreEqual("return typed", second.Text);
		Assert.IsTrue(second.HasPendingEdits);
	}

	[TestMethod]
	public async Task ApplyAsync_APublicationAfterTheDocumentPublishedAndBeforeThePlan_IsADocumentChangedIssue()
	{
		await using var host = new TestHost();

		TextView first = await host.AttachAsync(host.First, "first", publishDelay: TimeSpan.Zero);
		TextView typing = await host.AttachAsync(host.Second, "typing");
		TextView peer = await host.AttachAsync(host.Second, "peer");

		typing.Type("return value + 1");

		// The second document's publication refreshes its peer, and a publication of the first document lands then, after it published.
		peer.OnReplaced = () => first.Type("local value = 2\nprint(value)");

		WorkspaceEditApplyResult result = await new WorkspaceEditApplier(host.Coordinator).ApplyAsync(Rename(host), TestContext.CancellationToken);

		WorkspaceEditPlanIssue issue = Assert.ContainsSingle(result.Plan.Issues);

		Assert.AreEqual(host.First, issue.DocumentId);
		Assert.AreEqual(WorkspaceEditPlanIssueKind.DocumentChanged, issue.Kind);
		Assert.IsNull(result.Replacement);
		Assert.AreEqual("local value = 2\nprint(value)", host.ContentOf(host.First));
		Assert.AreEqual("return value + 1", host.ContentOf(host.Second));
	}

	[TestMethod]
	public async Task ApplyAsync_ACoordinatorStopWhileTheDocumentsPublish_ThrowsAndChangesNothing()
	{
		await using var host = new TestHost();

		TextView typing = await host.AttachAsync(host.First, "typing");
		TextView peer = await host.AttachAsync(host.First, "peer");

		await host.AttachAsync(host.Second, "second");
		typing.Type("local value = 2\nprint(value)");
		peer.OnReplaced = host.StopOutsideTheOperation;

		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
			new WorkspaceEditApplier(host.Coordinator).ApplyAsync(Rename(host), TestContext.CancellationToken)
		);

		Assert.AreEqual("local value = 2\nprint(value)", host.ContentOf(host.First));
		Assert.AreEqual(SecondText, host.ContentOf(host.Second));
	}

	[TestMethod]
	public async Task ApplyAsync_ACoordinatorStopBetweenTwoReplacements_KeepsTheFirstAndReportsACancellation()
	{
		await using var host = new TestHost();
		TextView first = await host.AttachAsync(host.First, "first");
		await host.AttachAsync(host.Second, "second");
		first.OnReplaced = host.StopOutsideTheOperation;

		WorkspaceEditApplyResult result = await new WorkspaceEditApplier(host.Coordinator).ApplyAsync(Rename(host), TestContext.CancellationToken);

		DocumentReplacementResult replacement = result.Replacement!;

		Assert.AreEqual(DocumentReplacementOutcome.PartiallyApplied, replacement.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.Canceled, replacement.Failure?.Code);

		Assert.AreSequenceEqual(
			[DocumentReplacementTargetOutcome.Applied, DocumentReplacementTargetOutcome.NotApplied],
			replacement.TargetResults.Select(target => target.Outcome)
		);

		Assert.IsEmpty(replacement.UnknownDocumentIds);
		Assert.AreEqual(SecondText, host.ContentOf(host.Second));
	}

	[TestMethod]
	public async Task ApplyAsync_ADispatcherThatRejectsTheSecondReplacement_KeepsTheFirstAndReportsACancellationWithTheFault()
	{
		await using var host = new TestHost();
		TextView first = await host.AttachAsync(host.First, "first");
		await host.AttachAsync(host.Second, "second");
		first.OnReplaced = () => host.FailBeforeRunning = true;

		WorkspaceEditApplyResult result = await new WorkspaceEditApplier(host.Coordinator).ApplyAsync(Rename(host), TestContext.CancellationToken);

		DocumentReplacementResult replacement = result.Replacement!;
		Assert.AreEqual(DocumentReplacementOutcome.PartiallyApplied, replacement.Outcome);

		Assert.AreSequenceEqual(
			[DocumentReplacementTargetOutcome.Applied, DocumentReplacementTargetOutcome.NotApplied],
			replacement.TargetResults.Select(target => target.Outcome)
		);

		Assert.AreEqual(WorkspaceOperationFailureCodes.Canceled, replacement.Failure?.Code);
		Assert.IsInstanceOfType<InvalidOperationException>(replacement.Failure?.Exception?.InnerException);
		Assert.AreEqual("local renamed = 1\nprint(renamed)", host.ContentOf(host.First));
		Assert.AreEqual(SecondText, host.ContentOf(host.Second));
	}

	[TestMethod]
	public async Task ApplyAsync_ADispatcherThatFaultsAfterEachReplacement_ReportsEveryChangedDocumentAndItsViews()
	{
		await using var host = new TestHost();
		TextView first = await host.AttachAsync(host.First, "first");
		await host.AttachAsync(host.Second, "second");
		first.OnReplaced = () => host.FailAfterRunning = true;

		WorkspaceEditApplyResult result = await new WorkspaceEditApplier(host.Coordinator).ApplyAsync(Rename(host), TestContext.CancellationToken);

		Assert.AreEqual(DocumentReplacementOutcome.Completed, result.Replacement?.Outcome);
		Assert.AreSequenceEqual([host.First, host.Second], result.Replacement!.ChangedDocumentIds);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, result.ViewSynchronization[host.First].Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, result.ViewSynchronization[host.Second].Outcome);
		Assert.AreEqual("return renamed", host.ContentOf(host.Second));
	}

	[TestMethod]
	public async Task ApplyAsync_AnOpenThatFails_IsADocumentNotTrackedIssueThatCarriesTheOpenResult()
	{
		await using var host = new TestHost();
		await host.AttachAsync(host.First, "first");
		string missing = host.PathOf("missing.lua");

		var applier = new WorkspaceEditApplier(
			host.Coordinator,
			new WorkspaceEditApplierOptions { OpenOptions = s_openOptions with { CreateIfMissing = false } }
		);

		WorkspaceEditApplyResult result = await applier.ApplyAsync(
			new WorkspaceEdit([
				new TextDocumentEdit(host.First, [Edit(line: 0, startCharacter: 6, endCharacter: 11, newText: "renamed")]),
				new TextDocumentEdit(missing, [Edit(line: 0, startCharacter: 0, endCharacter: 0, newText: "x")]),
			]),

			TestContext.CancellationToken
		);

		WorkspaceEditPlanIssue issue = Assert.ContainsSingle(result.Plan.Issues);

		Assert.AreEqual(missing, issue.DocumentId);
		Assert.AreEqual(WorkspaceEditPlanIssueKind.DocumentNotTracked, issue.Kind);
		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.NotFound, issue.OpenResult?.Outcome);
		Assert.IsNull(result.Replacement);
		Assert.IsFalse(File.Exists(missing));
		Assert.AreEqual(FirstText, host.ContentOf(host.First));
	}

	[TestMethod]
	public async Task ApplyAsync_WithOpenOptions_OpensAnUntrackedDocumentAndChangesIt()
	{
		await using var host = new TestHost();
		var applier = new WorkspaceEditApplier(host.Coordinator, new WorkspaceEditApplierOptions { OpenOptions = s_openOptions });

		WorkspaceEditApplyResult result = await applier.ApplyAsync(Rename(host), TestContext.CancellationToken);

		Assert.AreEqual(DocumentReplacementOutcome.Completed, result.Replacement?.Outcome);
		Assert.IsTrue(host.Store.TryGetSnapshot(host.First, out _));
		Assert.AreEqual("return renamed", host.ContentOf(host.Second));
	}

	[TestMethod]
	public async Task ApplyAsync_WithoutOpenOptions_AnUntrackedDocumentIsAPlanIssue()
	{
		await using var host = new TestHost();
		await host.AttachAsync(host.First, "first");

		WorkspaceEditApplyResult result = await new WorkspaceEditApplier(host.Coordinator).ApplyAsync(Rename(host), TestContext.CancellationToken);

		WorkspaceEditPlanIssue issue = Assert.ContainsSingle(result.Plan.Issues);

		Assert.AreEqual(host.Second, issue.DocumentId);
		Assert.AreEqual(WorkspaceEditPlanIssueKind.DocumentNotTracked, issue.Kind);
		Assert.IsNull(result.Replacement);
		Assert.AreEqual(FirstText, host.ContentOf(host.First));
	}

	[TestMethod]
	public async Task ApplyAsync_ContinueOnFailure_ReachesTheDocumentAfterARejectedOne()
	{
		await using var host = new TestHost();
		TextView first = await host.AttachAsync(host.First, "first");
		await host.AttachAsync(host.Second, "second");
		string third = host.PathOf("third.lua");
		File.WriteAllText(third, "return value");
		await host.AttachAsync(third, "third");
		first.OnReplaced = () => host.Type(host.Second, "return typed");

		var applier = new WorkspaceEditApplier(
			host.Coordinator,
			new WorkspaceEditApplierOptions { Replacement = new DocumentReplacementApplierOptions { ContinueOnFailure = true } }
		);

		WorkspaceEditApplyResult result = await applier.ApplyAsync(
			new WorkspaceEdit([
				.. Rename(host).DocumentEdits,
				new TextDocumentEdit(third, [Edit(line: 0, startCharacter: 7, endCharacter: 12, newText: "renamed")]),
			]),

			TestContext.CancellationToken
		);

		Assert.AreSequenceEqual([host.First, third], result.Replacement!.ChangedDocumentIds);
		Assert.AreEqual("return renamed", host.ContentOf(third));
	}

	[TestMethod]
	public void Constructor_NullCoordinator_IsAnArgumentError() => Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceEditApplier(null!));

	[TestMethod]
	public void ApplierOptions_NullReplacement_IsAnArgumentError() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceEditApplierOptions { Replacement = null! });

	[TestMethod]
	public async Task ApplyAsync_NullEdit_IsAnArgumentError()
	{
		await using var host = new TestHost();

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new WorkspaceEditApplier(host.Coordinator).ApplyAsync(null!, TestContext.CancellationToken)
		);
	}

	// Two Lua files and a coordinator whose dispatcher runs each action inline on the calling thread.
	private sealed class TestHost : IAsyncDisposable
	{
		private readonly TemporaryDirectory _directory = new();

		internal TestHost()
		{
			First = PathOf("first.lua");
			Second = PathOf("second.lua");

			File.WriteAllText(First, FirstText);
			File.WriteAllText(Second, SecondText);

			Store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());
			Coordinator = new WorkspaceViewCoordinator(Store, new ScriptedUiDispatcher(Dispatch));
		}

		// Whether the coordinator's dispatcher rejects each action before it runs it.
		internal bool FailBeforeRunning { get; set; }

		// Whether the coordinator's dispatcher faults after it ran each action.
		internal bool FailAfterRunning { get; set; }

		internal static ScriptedUiDispatcher Dispatcher { get; } =
			new(action =>
			{
				action();
				return Task.CompletedTask;
			});

		internal string First { get; }

		internal string Second { get; }

		internal WorkspaceDocumentStore Store { get; }

		internal WorkspaceViewCoordinator Coordinator { get; }

		internal string PathOf(string name) => _directory.GetFilePath(name);

		private Task Dispatch(Action action)
		{
			if (FailBeforeRunning)
				throw new InvalidOperationException("The dispatcher rejected the action.");

			action();

			return FailAfterRunning
				? Task.FromException(new InvalidOperationException("The dispatcher faulted after the action."))
				: Task.CompletedTask;
		}

		internal async Task<TextView> AttachAsync(string path, string viewId, TimeSpan? publishDelay = null)
		{
			var view = new TextView(viewId, publishDelay);
			WorkspaceViewCoordinatorOpenResult opened = await Coordinator.OpenWithViewAsync(path, s_openOptions, view);

			Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.Opened, opened.Outcome);
			return view;
		}

		internal string ContentOf(string path)
		{
			return Store.TryGetSnapshot(path, out WorkspaceDocumentSnapshot? snapshot)
				? snapshot.Content
				: throw new AssertFailedException($"{path} is not open.");
		}

		// Changes a document in the store directly, as a publication of another view does.
		internal void Type(string path, string text)
		{
			Assert.IsTrue(Store.TryGetSnapshot(path, out WorkspaceDocumentSnapshot? snapshot));

			Assert.AreEqual(
				WorkspaceDocumentMutationOutcome.Changed,
				Store.Replace(new WorkspaceDocumentReplaceRequest(snapshot.Identity, text, snapshot.FileFormat)).Outcome
			);
		}

		// Starts the stop in a flow outside the running operation, because a stop from inside it would wait for that operation, and returns once
		// the coordinator admits no new operation.
		internal void StopOutsideTheOperation()
		{
			using var started = new ManualResetEventSlim();

			using (ExecutionContext.SuppressFlow())
			{
				_ = Task.Run(() =>
				{
					Task stop = Coordinator.StopAsync();
					started.Set();
					return stop;
				});
			}

			started.Wait();
		}

		public async ValueTask DisposeAsync()
		{
			FailBeforeRunning = false;
			FailAfterRunning = false;

			await Coordinator.DisposeAsync();
			await Store.DisposeAsync();

			_directory.Dispose();
		}
	}

	// An editor view over a string; OnReplaced runs after each replacement the coordinator makes, inside the coordinator's operation. Without a
	// publish delay it publishes only when the coordinator asks; with TimeSpan.Zero it publishes each change at once.
	private sealed class TextView(string viewId, TimeSpan? publishDelay = null)
		: WorkspaceDocumentTextViewBase(viewId, TestHost.Dispatcher, publishDelay)
	{
		public string Text { get; private set; } = string.Empty;

		public Action? OnReplaced { get; set; }

		protected override bool IsReadOnly { get; set; }

		public void Type(string text)
		{
			Text = text;
			HandleTextChanged();
		}

		protected override string ReadText() => Text;

		protected override void ReplaceText(string text)
		{
			Text = text;
			HandleTextChanged();
			OnReplaced?.Invoke();
		}
	}
}
