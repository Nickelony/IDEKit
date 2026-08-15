#if AVALONIAEDIT
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Editing;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.Tests;
#else
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Editing;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.Tests;
#endif

/// <summary>
/// Verifies the extracted per-document insertion tracker in isolation: what it records, how a recorded
/// closing text follows edits, and when a recorded entry stops qualifying. The end-to-end tracking
/// behavior through the service is covered by <see cref="TextAutoClosingServiceTests"/>.
/// </summary>
[STATestClass]
public sealed class AutoClosingInsertionTrackerTests
{
	[TestMethod]
	public void GetResolverCallback_NullDocument_ReturnsNull()
	{
		var tracker = new AutoClosingInsertionTracker();

		Assert.IsNull(tracker.GetResolverCallback(null));
	}

	[TestMethod]
	public void GetResolverCallback_UntrackedDocument_ReturnsNull()
	{
		var tracker = new AutoClosingInsertionTracker();
		var document = new TextDocument("ab");

		Assert.IsNull(tracker.GetResolverCallback(document));
	}

	[TestMethod]
	public void GetResolverCallback_TrackedTextPresent_ReportsTrueAtItsOffset()
	{
		var tracker = new AutoClosingInsertionTracker();
		var document = new TextDocument("ab()");

		tracker.Track(document, 3, ")");

		Func<int, bool>? callback = tracker.GetResolverCallback(document);

		Assert.IsNotNull(callback);
		Assert.IsTrue(callback(3));
		Assert.IsFalse(callback(2));
		Assert.IsFalse(callback(4));
	}

	[TestMethod]
	public void GetResolverCallback_TrackedTextReplaced_ReportsFalseAndRetiresTheEntry()
	{
		var tracker = new AutoClosingInsertionTracker();
		var document = new TextDocument("ab");

		document.Insert(2, ")");
		tracker.Track(document, 2, ")");

		Func<int, bool>? callback = tracker.GetResolverCallback(document);

		Assert.IsNotNull(callback);
		Assert.IsTrue(callback(2));

		// Replacing the recorded text in place means it is no longer present, so the probe reports false and
		// the stale entry is retired.
		document.Replace(2, 1, "]");

		Assert.IsFalse(callback(2));
		Assert.AreEqual(0, tracker.GetTrackedCount(document));
	}

	[TestMethod]
	public void GetResolverCallback_EditBeforeTheRecordedText_FollowsTheAnchor()
	{
		var tracker = new AutoClosingInsertionTracker();
		var document = new TextDocument("ab");

		document.Insert(2, ")");
		tracker.Track(document, 2, ")");

		// An edit before the recorded text shifts it; the anchor follows it, so it is still reported at its
		// new offset.
		document.Insert(0, "xy");

		Func<int, bool>? callback = tracker.GetResolverCallback(document);

		Assert.IsNotNull(callback);
		Assert.IsTrue(callback(4));
		Assert.IsFalse(callback(2));
	}

	[TestMethod]
	public void GetTrackedCount_UntrackedDocument_ReturnsZero()
	{
		var tracker = new AutoClosingInsertionTracker();
		var document = new TextDocument("ab");

		Assert.AreEqual(0, tracker.GetTrackedCount(document));
	}

	[TestMethod]
	public void GetTrackedCount_MultipleTrackedTexts_CountsThemAll()
	{
		var tracker = new AutoClosingInsertionTracker();
		var document = new TextDocument("ab");

		document.Insert(2, ")");
		tracker.Track(document, 2, ")");
		document.Insert(3, ")");
		tracker.Track(document, 3, ")");

		Assert.AreEqual("ab))", document.Text);
		Assert.AreEqual(2, tracker.GetTrackedCount(document));
	}

	[TestMethod]
	public void GetTrackedCount_StaleEntries_AreRetiredOnRead()
	{
		var tracker = new AutoClosingInsertionTracker();
		var document = new TextDocument("ab");

		document.Insert(2, ")");
		tracker.Track(document, 2, ")");
		Assert.AreEqual(1, tracker.GetTrackedCount(document));

		// The recorded text is gone, so reading the count retires the stale entry instead of reporting it.
		document.Text = string.Empty;

		Assert.AreEqual(0, tracker.GetTrackedCount(document));
	}

	[TestMethod]
	public void GetTrackedCount_NullDocument_Throws()
	{
		var tracker = new AutoClosingInsertionTracker();

		Assert.ThrowsExactly<ArgumentNullException>(() => tracker.GetTrackedCount(null!));
	}
}
