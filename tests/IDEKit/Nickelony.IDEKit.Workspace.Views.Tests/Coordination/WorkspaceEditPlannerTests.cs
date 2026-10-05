using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Editing;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;
using Nickelony.IDEKit.Workspace.Replacement;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

/// <summary>Covers the planner that turns a language-server edit into whole-content replacements for the tracked documents.</summary>
[TestClass]
public sealed class WorkspaceEditPlannerTests
{
	private static readonly WorkspaceDocumentOpenOptions s_openOptions = new([TextEncoding.Utf8], TestSnapshots.FileFormat);

	public required TestContext TestContext { get; set; }

	private static TextEdit Edit(int line, int startCharacter, int endCharacter, string newText) =>
		new(new TextPositionRange(new TextPosition(line, startCharacter), new TextPosition(line, endCharacter)), newText);

	private sealed class OpenDocuments : IAsyncDisposable
	{
		private readonly TemporaryDirectory _directory = new();

		internal WorkspaceDocumentStore Store { get; } = new(new LocalWorkspaceFileSystem());

		internal string First => Path.Combine(_directory.Path, "first.lua");

		internal string Second => Path.Combine(_directory.Path, "second.lua");

		internal string Untracked => Path.Combine(_directory.Path, "untracked.lua");

		internal async Task OpenAsync(CancellationToken cancellationToken)
		{
			await File.WriteAllTextAsync(First, "local value = 1\nprint(value)", cancellationToken);
			await File.WriteAllTextAsync(Second, "return value", cancellationToken);

			await Store.OpenAsync(First, s_openOptions, cancellationToken);
			await Store.OpenAsync(Second, s_openOptions, cancellationToken);
		}

		public async ValueTask DisposeAsync()
		{
			await Store.DisposeAsync();
			_directory.Dispose();
		}
	}

	[TestMethod]
	public async Task Plan_ForATwoDocumentRename_PreparesOneReplacementPerDocumentAndTheApplierAppliesBoth()
	{
		await using var documents = new OpenDocuments();
		await documents.OpenAsync(TestContext.CancellationToken);

		WorkspaceDocumentStore store = documents.Store;

		string first = documents.First;
		string second = documents.Second;

		var edit = new WorkspaceEdit([
			new TextDocumentEdit(
				first,

				[
					Edit(line: 0, startCharacter: 6, endCharacter: 11, newText: "renamed"),
					Edit(line: 1, startCharacter: 6, endCharacter: 11, newText: "renamed"),
				]
			),

			new TextDocumentEdit(second, [Edit(line: 0, startCharacter: 7, endCharacter: 12, newText: "renamed")]),
		]);

		WorkspaceEditPlan plan = WorkspaceEditPlanner.Plan(edit, store);

		Assert.IsTrue(plan.Succeeded);
		Assert.AreSequenceEqual([first, second], plan.Targets.Select(target => target.Identity.DocumentId));
		Assert.AreEqual("local renamed = 1\nprint(renamed)", plan.Targets[0].AfterContent);
		Assert.AreEqual("return renamed", plan.Targets[1].AfterContent);

		DocumentReplacementResult result = await new DocumentReplacementApplier(
			(request, _) => Task.FromResult(DocumentReplacementAttemptResult.Reached(store.Replace(request))),
			store.Options
		).ApplyAsync(plan.Targets, TestContext.CancellationToken);

		Assert.AreEqual(DocumentReplacementOutcome.Completed, result.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(first, out WorkspaceDocumentSnapshot? firstSnapshot));
		Assert.IsTrue(store.TryGetSnapshot(second, out WorkspaceDocumentSnapshot? secondSnapshot));
		Assert.AreEqual("local renamed = 1\nprint(renamed)", firstSnapshot?.Content);
		Assert.AreEqual("return renamed", secondSnapshot?.Content);
	}

	[TestMethod]
	public async Task Plan_RecordsTheIdentityOfTheCurrentSnapshot_SoAnApplyAfterAnEditIsStale()
	{
		await using var documents = new OpenDocuments();
		await documents.OpenAsync(TestContext.CancellationToken);

		WorkspaceDocumentStore store = documents.Store;
		string first = documents.First;

		WorkspaceEditPlan plan = WorkspaceEditPlanner.Plan(
			new WorkspaceEdit([new TextDocumentEdit(first, [Edit(line: 0, startCharacter: 6, endCharacter: 11, newText: "renamed")])]),
			store
		);

		Assert.IsTrue(store.TryGetSnapshot(first, out WorkspaceDocumentSnapshot? current));

		WorkspaceDocumentMutationResult typed = store.Replace(
			new WorkspaceDocumentReplaceRequest(
				new WorkspaceDocumentRequestIdentity(current!.DocumentKey, current.DocumentId, current.Version),
				"typed meanwhile",
				current.FileFormat
			)
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, typed.Outcome);

		WorkspaceDocumentMutationResult stale = store.Replace(
			new WorkspaceDocumentReplaceRequest(plan.Targets[0].Identity, plan.Targets[0].AfterContent, plan.Targets[0].AfterFileFormat)
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.StaleDocument, stale.Outcome);
	}

	[TestMethod]
	public async Task Plan_ForADocumentTheStoreDoesNotTrack_ReportsAnIssueAndKeepsThePlannedDocuments()
	{
		await using var documents = new OpenDocuments();
		await documents.OpenAsync(TestContext.CancellationToken);

		WorkspaceDocumentStore store = documents.Store;

		string first = documents.First;
		string untracked = documents.Untracked;

		WorkspaceEditPlan plan = WorkspaceEditPlanner.Plan(
			new WorkspaceEdit([
				new TextDocumentEdit(untracked, [Edit(line: 0, startCharacter: 0, endCharacter: 0, newText: "x")]),
				new TextDocumentEdit(first, [Edit(line: 0, startCharacter: 6, endCharacter: 11, newText: "renamed")]),
			]),

			store
		);

		Assert.IsFalse(plan.Succeeded);

		WorkspaceEditPlanIssue issue = Assert.ContainsSingle(plan.Issues);

		Assert.AreEqual(untracked, issue.DocumentId);
		Assert.AreEqual(WorkspaceEditPlanIssueKind.DocumentNotTracked, issue.Kind);
		Assert.AreEqual(first, Assert.ContainsSingle(plan.Targets).Identity.DocumentId);
	}

	[TestMethod]
	public async Task Plan_ForAReversedRangeOrOverlappingEdits_ReportsAnIssueForThatDocument()
	{
		await using var documents = new OpenDocuments();
		await documents.OpenAsync(TestContext.CancellationToken);

		WorkspaceDocumentStore store = documents.Store;

		string first = documents.First;
		string second = documents.Second;

		WorkspaceEditPlan plan = WorkspaceEditPlanner.Plan(
			new WorkspaceEdit([
				new TextDocumentEdit(first, [new TextEdit(new TextPositionRange(new TextPosition(0, 5), new TextPosition(0, 2)), "x")]),

				new TextDocumentEdit(
					second,
					[Edit(line: 0, startCharacter: 0, endCharacter: 6, newText: "a"), Edit(line: 0, startCharacter: 3, endCharacter: 8, newText: "b")]
				),
			]),

			store
		);

		Assert.IsEmpty(plan.Targets);

		Assert.AreSequenceEqual(
			[WorkspaceEditPlanIssueKind.InvalidRange, WorkspaceEditPlanIssueKind.EditsConflict],
			plan.Issues.Select(issue => issue.Kind)
		);
	}

	[TestMethod]
	public async Task Plan_ForSeveralEntriesOfOneDocument_ValidatesTheirEditsTogether()
	{
		await using var documents = new OpenDocuments();
		await documents.OpenAsync(TestContext.CancellationToken);

		WorkspaceDocumentStore store = documents.Store;
		string first = documents.First;

		WorkspaceEditPlan merged = WorkspaceEditPlanner.Plan(
			new WorkspaceEdit([
				new TextDocumentEdit(first, [Edit(line: 0, startCharacter: 6, endCharacter: 11, newText: "renamed")]),
				new TextDocumentEdit(first, [Edit(line: 1, startCharacter: 6, endCharacter: 11, newText: "renamed")]),
			]),

			store
		);

		Assert.AreEqual("local renamed = 1\nprint(renamed)", Assert.ContainsSingle(merged.Targets).AfterContent);

		WorkspaceEditPlan overlapping = WorkspaceEditPlanner.Plan(
			new WorkspaceEdit([
				new TextDocumentEdit(first, [Edit(line: 0, startCharacter: 6, endCharacter: 11, newText: "a")]),
				new TextDocumentEdit(first, [Edit(line: 0, startCharacter: 8, endCharacter: 13, newText: "b")]),
			]),

			store
		);

		Assert.AreEqual(WorkspaceEditPlanIssueKind.EditsConflict, Assert.ContainsSingle(overlapping.Issues).Kind);
	}

	[TestMethod]
	public async Task Plan_ForEditsThatChangeNothing_YieldsNoTarget()
	{
		await using var documents = new OpenDocuments();
		await documents.OpenAsync(TestContext.CancellationToken);

		WorkspaceDocumentStore store = documents.Store;
		string first = documents.First;

		WorkspaceEditPlan plan = WorkspaceEditPlanner.Plan(
			new WorkspaceEdit([
				new TextDocumentEdit(first, [Edit(line: 0, startCharacter: 6, endCharacter: 11, newText: "value")]),
				new TextDocumentEdit(first, []),
			]),

			store
		);

		Assert.IsTrue(plan.Succeeded);
		Assert.IsEmpty(plan.Targets);
	}

	[TestMethod]
	public async Task Plan_ADocumentWhoseVersionMovedPastItsPublication_IsDocumentChanged_AndAPreparationIssueStays()
	{
		await using var documents = new OpenDocuments();
		await documents.OpenAsync(TestContext.CancellationToken);

		WorkspaceDocumentStore store = documents.Store;

		string first = documents.First;
		string second = documents.Second;

		Assert.IsTrue(store.TryGetSnapshot(first, out WorkspaceDocumentSnapshot? firstSnapshot));
		var preparationIssue = new WorkspaceEditPlanIssue(second, WorkspaceEditPlanIssueKind.PendingEditsNotPublished);

		WorkspaceEditPlan plan = WorkspaceEditPlanner.Plan(
			edit: new WorkspaceEdit([
				new TextDocumentEdit(first, [Edit(line: 0, startCharacter: 6, endCharacter: 11, newText: "renamed")]),
				new TextDocumentEdit(second, [Edit(line: 0, startCharacter: 7, endCharacter: 12, newText: "renamed")]),
			]),

			documents: store,
			preparationIssues: new Dictionary<string, WorkspaceEditPlanIssue> { [second] = preparationIssue },
			publishedVersions: new Dictionary<string, long> { [first] = firstSnapshot.Version - 1 }
		);

		Assert.IsEmpty(plan.Targets);
		Assert.AreEqual(WorkspaceEditPlanIssueKind.DocumentChanged, plan.Issues[0].Kind);
		Assert.AreSame(preparationIssue, plan.Issues[1]);
	}
}
