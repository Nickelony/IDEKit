namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins the diagnostic segment contract: construction, value semantics, and the documented
/// normalization that consumers apply when they render a segment.
/// </summary>
[TestClass]
public sealed class TextDiagnosticSegmentTests
{
	[TestMethod]
	public void DefaultSegment_CarriesZeroOffsetsAndNoneSeverity()
	{
		TextDiagnosticSegment segment = default;

		Assert.AreEqual(0, segment.StartOffset);
		Assert.AreEqual(0, segment.EndOffset);
		Assert.AreEqual(TextDiagnosticSeverity.None, segment.Severity);
	}

	[TestMethod]
	public void EqualityAndHashCode_FollowTheRecordValueSemantics()
	{
		var first = new TextDiagnosticSegment(2, 5, TextDiagnosticSeverity.Warning);
		var same = new TextDiagnosticSegment(2, 5, TextDiagnosticSeverity.Warning);
		var differentRange = new TextDiagnosticSegment(2, 6, TextDiagnosticSeverity.Warning);
		var differentSeverity = new TextDiagnosticSegment(2, 5, TextDiagnosticSeverity.Error);

		Assert.AreEqual(first, same);
		Assert.AreEqual(first.GetHashCode(), same.GetHashCode());
		Assert.AreNotEqual(first, differentRange);
		Assert.AreNotEqual(first, differentSeverity);
	}

	[TestMethod]
	public void ReversedRange_IsStoredUnchanged()
	{
		// The record stores the requested pair unchanged; the documented normalization (an empty or
		// reversed range becomes one code unit in a non-empty document) is the consumer's duty.
		var reversed = new TextDiagnosticSegment(5, 2, TextDiagnosticSeverity.Information);

		Assert.AreEqual(5, reversed.StartOffset);
		Assert.AreEqual(2, reversed.EndOffset);
	}
}
