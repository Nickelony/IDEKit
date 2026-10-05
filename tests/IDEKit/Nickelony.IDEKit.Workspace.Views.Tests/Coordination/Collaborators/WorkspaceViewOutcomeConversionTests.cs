using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

/// <summary>Covers the conversion of a store open outcome and open result onto the coordinator's.</summary>
[TestClass]
public sealed class WorkspaceViewOutcomeConversionTests
{
	[TestMethod]
	[DataRow(WorkspaceDocumentOpenOutcome.Opened, WorkspaceViewCoordinatorOpenOutcome.Opened)]
	[DataRow(WorkspaceDocumentOpenOutcome.AlreadyOpen, WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen)]
	[DataRow(WorkspaceDocumentOpenOutcome.InvalidPath, WorkspaceViewCoordinatorOpenOutcome.InvalidPath)]
	[DataRow(WorkspaceDocumentOpenOutcome.NotFound, WorkspaceViewCoordinatorOpenOutcome.NotFound)]
	[DataRow(WorkspaceDocumentOpenOutcome.IsDirectory, WorkspaceViewCoordinatorOpenOutcome.IsDirectory)]
	[DataRow(WorkspaceDocumentOpenOutcome.LoadFailed, WorkspaceViewCoordinatorOpenOutcome.LoadFailed)]
	[DataRow(WorkspaceDocumentOpenOutcome.Canceled, WorkspaceViewCoordinatorOpenOutcome.Canceled)]
	public void ToOpenOutcome_ConvertsEachDefinedStoreOutcome(WorkspaceDocumentOpenOutcome store, WorkspaceViewCoordinatorOpenOutcome expected) =>
		Assert.AreEqual(expected, WorkspaceViewOutcomeConversion.ToOpenOutcome(store));

	[TestMethod]
	public void ToOpenOutcome_NamesEveryDefinedStoreOutcome()
	{
		// A new store outcome has to be added to the rows above, where its conversion is decided.
		Assert.HasCount(7, Enum.GetValues<WorkspaceDocumentOpenOutcome>());
	}

	[TestMethod]
	[DataRow(WorkspaceDocumentOpenOutcome.Opened, WorkspaceViewCoordinatorOpenOutcome.Opened)]
	[DataRow(WorkspaceDocumentOpenOutcome.AlreadyOpen, WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen)]
	public void ToOpenResult_ForALoadedOrTrackedDocument_CarriesTheSnapshotAndWrapsTheStoreResult(
		WorkspaceDocumentOpenOutcome store,
		WorkspaceViewCoordinatorOpenOutcome expected
	)
	{
		WorkspaceDocumentSnapshot snapshot = TestSnapshots.Create("doc", "content");
		var storeResult = new WorkspaceDocumentOpenResult(store, snapshot);

		WorkspaceViewCoordinatorOpenResult result = WorkspaceViewOutcomeConversion.ToOpenResult(storeResult);

		Assert.AreEqual(expected, result.Outcome);
		Assert.AreSame(snapshot, result.Snapshot);
		Assert.AreSame(storeResult, result.StoreResult);
		Assert.IsNull(result.Failure);
	}

	[TestMethod]
	[DataRow(WorkspaceDocumentOpenOutcome.InvalidPath, WorkspaceViewCoordinatorOpenOutcome.InvalidPath)]
	[DataRow(WorkspaceDocumentOpenOutcome.NotFound, WorkspaceViewCoordinatorOpenOutcome.NotFound)]
	[DataRow(WorkspaceDocumentOpenOutcome.IsDirectory, WorkspaceViewCoordinatorOpenOutcome.IsDirectory)]
	[DataRow(WorkspaceDocumentOpenOutcome.LoadFailed, WorkspaceViewCoordinatorOpenOutcome.LoadFailed)]
	[DataRow(WorkspaceDocumentOpenOutcome.Canceled, WorkspaceViewCoordinatorOpenOutcome.Canceled)]
	public void ToOpenResult_ForAnyOtherOutcome_CarriesTheFailureAndNoSnapshot(
		WorkspaceDocumentOpenOutcome store,
		WorkspaceViewCoordinatorOpenOutcome expected
	)
	{
		var failure = new WorkspaceOperationFailure("code", "The load failed.");
		var storeResult = new WorkspaceDocumentOpenResult(store, Snapshot: null, failure);

		WorkspaceViewCoordinatorOpenResult result = WorkspaceViewOutcomeConversion.ToOpenResult(storeResult);

		Assert.AreEqual(expected, result.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.AreSame(failure, result.Failure);
		Assert.AreSame(storeResult, result.StoreResult);
	}

	[TestMethod]
	public void ToOpenOutcome_ForAnUndefinedStoreOutcome_Throws()
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			WorkspaceViewOutcomeConversion.ToOpenOutcome((WorkspaceDocumentOpenOutcome)999)
		);

		Assert.AreEqual("outcome", exception.ParamName);
	}
}
