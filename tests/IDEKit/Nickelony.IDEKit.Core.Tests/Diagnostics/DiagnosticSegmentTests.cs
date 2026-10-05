namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins the diagnostic segment contract: construction, value semantics, and the documented normalization that consumers apply when they render
/// a segment.
/// </summary>
[TestClass]
public sealed class DiagnosticSegmentTests
{
	[TestMethod]
	public void DefaultSegment_CarriesZeroOffsetsAndNoneSeverity()
	{
		DiagnosticSegment segment = default;

		Assert.AreEqual(default, segment.Range);
		Assert.AreEqual(DiagnosticSeverity.None, segment.Severity);
	}

	[TestMethod]
	public void EqualityAndHashCode_FollowTheRecordValueSemantics()
	{
		var first = new DiagnosticSegment(TextRange.FromBounds(2, 5), DiagnosticSeverity.Warning);
		var same = new DiagnosticSegment(TextRange.FromBounds(2, 5), DiagnosticSeverity.Warning);
		var differentRange = new DiagnosticSegment(TextRange.FromBounds(2, 6), DiagnosticSeverity.Warning);
		var differentSeverity = new DiagnosticSegment(TextRange.FromBounds(2, 5), DiagnosticSeverity.Error);

		Assert.AreEqual(first, same);
		Assert.AreEqual(first.GetHashCode(), same.GetHashCode());
		Assert.AreNotEqual(first, differentRange);
		Assert.AreNotEqual(first, differentSeverity);
	}

	[TestMethod]
	public void EmptyRange_IsStoredUnchanged()
	{
		// The record stores the requested range unchanged; the documented normalization (an empty
		// range becomes one code unit in a non-empty document) is the consumer's duty.
		var empty = new DiagnosticSegment(new TextRange(5, 0), DiagnosticSeverity.Information);
		Assert.AreEqual(new TextRange(5, 0), empty.Range);
	}
}
