namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the pending-close tracker's mark, claim, cancel and move rules directly.</summary>
[TestClass]
public sealed class PendingCloseTrackerTests
{
	[TestMethod]
	public void MarkPending_MakesTheCloseVisible()
	{
		var tracker = new PendingCloseTracker();

		Assert.IsFalse(tracker.IsPending(TestPaths.Script("a.lua")));

		tracker.MarkPending(TestPaths.Script("a.lua"));

		Assert.IsTrue(tracker.IsPending(TestPaths.Script("a.lua")));
		Assert.IsFalse(tracker.IsPending(TestPaths.Script("b.lua")));
	}

	[TestMethod]
	public void TryClaim_TakesThePendingCloseExactlyOnce()
	{
		var tracker = new PendingCloseTracker();
		tracker.MarkPending(TestPaths.Script("a.lua"));

		Assert.IsTrue(tracker.TryClaim(TestPaths.Script("a.lua")));
		Assert.IsFalse(tracker.TryClaim(TestPaths.Script("a.lua")));
		Assert.IsFalse(tracker.IsPending(TestPaths.Script("a.lua")));
	}

	[TestMethod]
	public void TryClaim_WithoutAPendingClose_ReturnsFalse()
	{
		var tracker = new PendingCloseTracker();
		Assert.IsFalse(tracker.TryClaim(TestPaths.Script("a.lua")));
	}

	[TestMethod]
	public void MarkPending_AfterAClaim_PendsTheCloseAgain()
	{
		var tracker = new PendingCloseTracker();
		tracker.MarkPending(TestPaths.Script("a.lua"));

		Assert.IsTrue(tracker.TryClaim(TestPaths.Script("a.lua")));

		tracker.MarkPending(TestPaths.Script("a.lua"));

		Assert.IsTrue(tracker.IsPending(TestPaths.Script("a.lua")));
	}

	[TestMethod]
	public void Cancel_ForgetsThePendingClose()
	{
		var tracker = new PendingCloseTracker();
		tracker.MarkPending(TestPaths.Script("a.lua"));

		tracker.Cancel(TestPaths.Script("a.lua"));
		tracker.Cancel(TestPaths.Script("a.lua"));

		Assert.IsFalse(tracker.IsPending(TestPaths.Script("a.lua")));
	}

	[TestMethod]
	public void Rename_CarriesAPendingCloseToTheNewPath()
	{
		var tracker = new PendingCloseTracker();
		tracker.MarkPending(TestPaths.Script("old.lua"));

		tracker.Rename(TestPaths.Script("old.lua"), TestPaths.Script("new.lua"));

		Assert.IsFalse(tracker.IsPending(TestPaths.Script("old.lua")));
		Assert.IsTrue(tracker.IsPending(TestPaths.Script("new.lua")));
	}

	[TestMethod]
	public void Rename_WithoutAPendingClose_MarksNothing()
	{
		var tracker = new PendingCloseTracker();

		tracker.Rename(TestPaths.Script("old.lua"), TestPaths.Script("new.lua"));

		Assert.IsFalse(tracker.IsPending(TestPaths.Script("new.lua")));
	}

	[TestMethod]
	public void MarkPending_UnderACaseSensitiveComparison_KeepsPathsThatDifferOnlyByCaseApart()
	{
		var tracker = new PendingCloseTracker(TestOptions.CaseSensitive);

		tracker.MarkPending(TestPaths.Script("Case.lua"));

		Assert.IsFalse(tracker.IsPending(TestPaths.Script("CASE.lua")));
	}

	[TestMethod]
	public void MarkPending_UnderACaseInsensitiveComparison_TreatsPathsThatDifferOnlyByCaseAsOne()
	{
		var tracker = new PendingCloseTracker(TestOptions.CaseInsensitive);

		tracker.MarkPending(TestPaths.Script("Case.lua"));

		Assert.IsTrue(tracker.IsPending(TestPaths.Script("CASE.lua")));
	}
}
