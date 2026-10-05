using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

/// <summary>
/// Pins the editor-neutral <see cref="DiagnosticSegmentConversion"/> that every editor binding composes into its diagnostic renderer.
/// </summary>
[TestClass]
public sealed class DiagnosticSegmentConversionTests
{
	[TestMethod]
	public void Project_MapsOffsetsAndSeverityInOrder()
	{
		var diagnostics = new List<Diagnostic>
		{
			new(severity: DiagnosticSeverity.Error, message: "first", range: TextRange.FromBounds(1, 5)),
			new(severity: DiagnosticSeverity.Hint, message: "second", range: TextRange.FromBounds(7, 7)),
		};

		IReadOnlyList<DiagnosticSegment> segments = DiagnosticSegmentConversion.Project(diagnostics);

		Assert.HasCount(2, segments);
		Assert.AreEqual(1, segments[0].Range.Offset);
		Assert.AreEqual(5, segments[0].Range.EndOffset);
		Assert.AreEqual(DiagnosticSeverity.Error, segments[0].Severity);
		Assert.AreEqual(7, segments[1].Range.Offset);
		Assert.AreEqual(7, segments[1].Range.EndOffset);
		Assert.AreEqual(DiagnosticSeverity.Hint, segments[1].Severity);
	}

	[TestMethod]
	public void Project_NullDiagnostics_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => DiagnosticSegmentConversion.Project(null!));

	[TestMethod]
	public void Project_NullEntry_ThrowsArgumentException()
	{
		// A null entry is a caller error: the host finds out where it projects, not inside a render pass.
		IReadOnlyList<Diagnostic> diagnostics =
		[
			new Diagnostic(severity: DiagnosticSeverity.Error, message: "first", range: TextRange.FromBounds(1, 2)),
			null!,
		];

		Assert.ThrowsExactly<ArgumentException>(() => DiagnosticSegmentConversion.Project(diagnostics));
	}
}
