using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>Covers the conversions of lower-layer outcomes onto document operation outcomes, one explicit mapping per defined value.</summary>
[TestClass]
public sealed class WorkspaceOutcomeConversionTests
{
	[TestMethod]
	[DataRow(WorkspaceFileMoveOutcome.Unknown, WorkspaceDocumentRenameOutcome.MoveFailed)]
	[DataRow(WorkspaceFileMoveOutcome.Moved, WorkspaceDocumentRenameOutcome.MoveFailed)]
	[DataRow(WorkspaceFileMoveOutcome.DestinationExists, WorkspaceDocumentRenameOutcome.DestinationExists)]
	[DataRow(WorkspaceFileMoveOutcome.ExternalFileConflict, WorkspaceDocumentRenameOutcome.ExternalFileConflict)]
	[DataRow(WorkspaceFileMoveOutcome.MoveFailed, WorkspaceDocumentRenameOutcome.MoveFailed)]
	[DataRow(WorkspaceFileMoveOutcome.MoveStateUnknown, WorkspaceDocumentRenameOutcome.MoveStateUnknown)]
	[DataRow(WorkspaceFileMoveOutcome.Canceled, WorkspaceDocumentRenameOutcome.Canceled)]
	public void ToRenameOutcome_FromAFileMove_ConvertsEachDefinedOutcome(WorkspaceFileMoveOutcome move, WorkspaceDocumentRenameOutcome expected) =>
		Assert.AreEqual(expected, WorkspaceOutcomeConversion.ToRenameOutcome(move));

	[TestMethod]
	[DataRow(WorkspaceFileMoveOutcome.Unknown, WorkspaceDocumentDirectoryRenameOutcome.MoveFailed)]
	[DataRow(WorkspaceFileMoveOutcome.Moved, WorkspaceDocumentDirectoryRenameOutcome.MoveFailed)]
	[DataRow(WorkspaceFileMoveOutcome.DestinationExists, WorkspaceDocumentDirectoryRenameOutcome.DestinationExists)]
	[DataRow(WorkspaceFileMoveOutcome.ExternalFileConflict, WorkspaceDocumentDirectoryRenameOutcome.ExternalFileConflict)]
	[DataRow(WorkspaceFileMoveOutcome.MoveFailed, WorkspaceDocumentDirectoryRenameOutcome.MoveFailed)]
	[DataRow(WorkspaceFileMoveOutcome.MoveStateUnknown, WorkspaceDocumentDirectoryRenameOutcome.MoveStateUnknown)]
	[DataRow(WorkspaceFileMoveOutcome.Canceled, WorkspaceDocumentDirectoryRenameOutcome.Canceled)]
	public void ToDirectoryRenameOutcome_ConvertsEachDefinedOutcome(
		WorkspaceFileMoveOutcome move,
		WorkspaceDocumentDirectoryRenameOutcome expected
	) => Assert.AreEqual(expected, WorkspaceOutcomeConversion.ToDirectoryRenameOutcome(move));

	[TestMethod]
	[DataRow(WorkspaceFileDeleteOutcome.Unknown, WorkspaceDocumentDirectoryDeleteOutcome.DeleteFailed)]
	[DataRow(WorkspaceFileDeleteOutcome.Deleted, WorkspaceDocumentDirectoryDeleteOutcome.DeleteFailed)]
	[DataRow(WorkspaceFileDeleteOutcome.ExternalFileConflict, WorkspaceDocumentDirectoryDeleteOutcome.ExternalFileConflict)]
	[DataRow(WorkspaceFileDeleteOutcome.DeleteFailed, WorkspaceDocumentDirectoryDeleteOutcome.DeleteFailed)]
	[DataRow(WorkspaceFileDeleteOutcome.Canceled, WorkspaceDocumentDirectoryDeleteOutcome.Canceled)]
	public void ToDirectoryDeleteOutcome_ConvertsEachDefinedOutcome(
		WorkspaceFileDeleteOutcome delete,
		WorkspaceDocumentDirectoryDeleteOutcome expected
	) => Assert.AreEqual(expected, WorkspaceOutcomeConversion.ToDirectoryDeleteOutcome(delete));

	[TestMethod]
	public void TheFileOutcomeTables_NameEveryDefinedFileSystemOutcome()
	{
		// A new file-system outcome has to be added to the tables above, where its conversion is decided.
		Assert.HasCount(7, Enum.GetValues<WorkspaceFileMoveOutcome>());
		Assert.HasCount(5, Enum.GetValues<WorkspaceFileDeleteOutcome>());
	}

	// The pre-gate outcome is internal, so the rows carry its value as an integer.
	[TestMethod]
	[DataRow((int)OperationPreGateOutcome.Proceed, WorkspaceDocumentRenameOutcome.DestinationBusy)]
	[DataRow((int)OperationPreGateOutcome.NoChange, WorkspaceDocumentRenameOutcome.NoChange)]
	[DataRow((int)OperationPreGateOutcome.DestinationInUse, WorkspaceDocumentRenameOutcome.DestinationInUse)]
	[DataRow((int)OperationPreGateOutcome.DestinationBusy, WorkspaceDocumentRenameOutcome.DestinationBusy)]
	public void ToRenameOutcome_FromAPreGateOutcome_ConvertsEachDefinedOutcome(int preGate, WorkspaceDocumentRenameOutcome expected) =>
		Assert.AreEqual(expected, WorkspaceOutcomeConversion.ToRenameOutcome((OperationPreGateOutcome)preGate));

	[TestMethod]
	[DataRow((int)OperationPreGateOutcome.Proceed, WorkspaceDocumentSaveAsOutcome.DestinationBusy)]
	[DataRow((int)OperationPreGateOutcome.NoChange, WorkspaceDocumentSaveAsOutcome.DestinationBusy)]
	[DataRow((int)OperationPreGateOutcome.DestinationInUse, WorkspaceDocumentSaveAsOutcome.DestinationInUse)]
	[DataRow((int)OperationPreGateOutcome.DestinationBusy, WorkspaceDocumentSaveAsOutcome.DestinationBusy)]
	public void ToSaveAsOutcome_ConvertsEachDefinedOutcome(int preGate, WorkspaceDocumentSaveAsOutcome expected) =>
		Assert.AreEqual(expected, WorkspaceOutcomeConversion.ToSaveAsOutcome((OperationPreGateOutcome)preGate));

	[TestMethod]
	public void ToFailureResult_MapsEachBeginOutcomeToTheCallersOutcome()
	{
		WorkspaceDocumentSnapshot snapshot = TestSnapshots.Create("doc", "content");

		var document = new LogicalDocument(
			syncRoot: new object(),
			documentKey: new WorkspaceDocumentKey(Guid.NewGuid()),
			documentId: "doc",
			displayPath: "doc",
			content: "content",
			fileFormat: TestSnapshots.FileFormat,
			onDiskStamp: FileStamp.Missing,
			noBomEncodings: [TextEncoding.Utf8]
		);

		static (string Outcome, WorkspaceDocumentSnapshot? Snapshot) Convert(OperationBeginResult begin)
		{
			return WorkspaceOutcomeConversion.ToFailureResult(
				begin,
				documentNotFound: "NotFound",
				staleDocumentInstance: "StaleInstance",
				staleDocument: "Stale",
				operationInProgress: "InProgress",
				factory: (outcome, failureSnapshot) => (outcome, failureSnapshot)
			);
		}

		Assert.AreEqual(("NotFound", null), Convert(OperationBeginResult.NotFound()));

		Assert.AreEqual(
			("StaleInstance", snapshot),
			Convert(OperationBeginResult.Rejected(OperationBeginOutcome.StaleDocumentInstance, document, snapshot))
		);

		Assert.AreEqual(("Stale", snapshot), Convert(OperationBeginResult.Rejected(OperationBeginOutcome.StaleDocument, document, snapshot)));

		Assert.AreEqual(
			("InProgress", snapshot),
			Convert(OperationBeginResult.Rejected(OperationBeginOutcome.OperationInProgress, document, snapshot))
		);
	}
}
