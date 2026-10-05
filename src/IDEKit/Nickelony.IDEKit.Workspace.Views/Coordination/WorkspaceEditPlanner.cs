using System.Text;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Editing;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Replacement;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>
/// Turns the text edits of a language-server <see cref="WorkspaceEdit"/> into whole-content replacements for the documents the store tracks.
/// </summary>
/// <remarks>
/// <para>
/// A language server addresses a document by line and character, and the store replaces whole content against a document version.
/// The planner converts each document's edits against the snapshot the store holds now and records that snapshot's identity, so applying a target
/// after the document changed reports a stale document instead of overwriting the newer text. <see cref="WorkspaceEditApplier"/> is its one caller,
/// so the views publish before every plan.
/// </para>
/// <para>
/// Several entries for one document are validated together, because their edits address the same text. A document whose edits change nothing yields
/// no target.
/// </para>
/// </remarks>
internal static class WorkspaceEditPlanner
{
	// Plans the replacements that apply a language-server edit to the documents the store tracks. A document with an issue the applier found while
	// it opened and published the documents keeps that issue instead of a plan, and a tracked document whose version moved past the one its
	// publication left is DocumentChanged, because the edit would address text the language server did not see.
	internal static WorkspaceEditPlan Plan(
		WorkspaceEdit edit,
		IWorkspaceDocumentReader documents,
		IReadOnlyDictionary<string, WorkspaceEditPlanIssue>? preparationIssues = null,
		IReadOnlyDictionary<string, long>? publishedVersions = null
	)
	{
		List<DocumentReplacementTarget> targets = [];
		List<WorkspaceEditPlanIssue> issues = [];

		foreach ((string documentId, List<TextEdit> textEdits) in GroupByDocument(edit, documents.Options.PathComparison.Comparer))
		{
			if (preparationIssues is not null && preparationIssues.TryGetValue(documentId, out WorkspaceEditPlanIssue? preparationIssue))
			{
				issues.Add(preparationIssue);
				continue;
			}

			if (!documents.TryGetSnapshot(documentId, out WorkspaceDocumentSnapshot? snapshot) || snapshot is null)
			{
				issues.Add(new WorkspaceEditPlanIssue(documentId, WorkspaceEditPlanIssueKind.DocumentNotTracked));
				continue;
			}

			if (
				publishedVersions is not null
				&& (!publishedVersions.TryGetValue(documentId, out long publishedVersion) || publishedVersion != snapshot.Version)
			)
			{
				issues.Add(new WorkspaceEditPlanIssue(documentId, WorkspaceEditPlanIssueKind.DocumentChanged));
				continue;
			}

			string before = snapshot.Content;

			if (!TryApply(text: before, textEdits, result: out string after, out WorkspaceEditPlanIssueKind? failure))
			{
				issues.Add(new WorkspaceEditPlanIssue(documentId, failure ?? WorkspaceEditPlanIssueKind.EditsConflict));
				continue;
			}

			if (string.Equals(before, after, StringComparison.Ordinal))
				continue;

			targets.Add(
				new DocumentReplacementTarget
				{
					Identity = new WorkspaceDocumentRequestIdentity(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
					BeforeContent = before,
					BeforeFileFormat = snapshot.FileFormat,
					AfterContent = after,
					AfterFileFormat = snapshot.FileFormat,
				}
			);
		}

		return new WorkspaceEditPlan(targets, issues);
	}

	// Gathers the entries that name one document, in the order the edit first names each document, so the edits
	// of a document are validated against the same text.
	private static List<(string DocumentId, List<TextEdit> TextEdits)> GroupByDocument(WorkspaceEdit edit, StringComparer comparer)
	{
		List<(string DocumentId, List<TextEdit> TextEdits)> groups = [];
		Dictionary<string, int> indexByDocument = new(comparer);

		foreach (TextDocumentEdit documentEdit in edit.DocumentEdits)
		{
			if (!indexByDocument.TryGetValue(documentEdit.DocumentId, out int index))
			{
				index = groups.Count;

				indexByDocument.Add(documentEdit.DocumentId, index);
				groups.Add((documentEdit.DocumentId, []));
			}

			groups[index].TextEdits.AddRange(documentEdit.TextEdits);
		}

		return groups;
	}

	private static bool TryApply(string text, List<TextEdit> textEdits, out string result, out WorkspaceEditPlanIssueKind? failure)
	{
		result = text;
		failure = null;

		if (textEdits.Count == 0)
			return true;

		switch (TextEditConversion.Convert(text, textEdits, out PreparedTextEdits prepared))
		{
			case TextEditConversionOutcome.InvalidRange:
				failure = WorkspaceEditPlanIssueKind.InvalidRange;
				return false;

			case TextEditConversionOutcome.Conflict:
				failure = WorkspaceEditPlanIssueKind.EditsConflict;
				return false;
		}

		result = prepared.ApplyTo(text);
		return true;
	}
}
