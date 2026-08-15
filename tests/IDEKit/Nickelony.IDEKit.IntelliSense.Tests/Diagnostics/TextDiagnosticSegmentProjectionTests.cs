using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.IDEKit.IntelliSense.Tests.Diagnostics;

/// <summary>
/// Pins the editor-neutral <see cref="TextDiagnosticSegmentProjection"/> that every editor binding
/// composes into its diagnostic renderer.
/// </summary>
[TestClass]
public sealed class TextDiagnosticSegmentProjectionTests
{
	[TestMethod]
	public void Project_MapsOffsetsAndSeverityInOrder()
	{
		var diagnostics = new List<TextDiagnostic>
		{
			new(TextDiagnosticSeverity.Error, "first", 1, 5),
			new(TextDiagnosticSeverity.Hint, "second", 7, 7),
		};

		IReadOnlyList<TextDiagnosticSegment> segments = TextDiagnosticSegmentProjection.Project(diagnostics);

		Assert.HasCount(2, segments);
		Assert.AreEqual(1, segments[0].StartOffset);
		Assert.AreEqual(5, segments[0].EndOffset);
		Assert.AreEqual(TextDiagnosticSeverity.Error, segments[0].Severity);
		Assert.AreEqual(7, segments[1].StartOffset);
		Assert.AreEqual(7, segments[1].EndOffset);
		Assert.AreEqual(TextDiagnosticSeverity.Hint, segments[1].Severity);
	}

	[TestMethod]
	public void Project_NullDiagnostics_ThrowsArgumentNullException()
		=> Assert.ThrowsExactly<ArgumentNullException>(() => TextDiagnosticSegmentProjection.Project(null!));

	[TestMethod]
	public void Project_NullEntry_ThrowsArgumentException()
	{
		// The direct projection keeps its fail-fast contract: a null entry is a caller error, not a
		// render-pass tolerance case (which only the tolerating overload covers).
		IReadOnlyList<TextDiagnostic> diagnostics =
			[new TextDiagnostic(TextDiagnosticSeverity.Error, "first", 1, 2), null!];

		Assert.ThrowsExactly<ArgumentException>(() => TextDiagnosticSegmentProjection.Project(diagnostics));
	}

	[TestMethod]
	public void ProjectToleratingNulls_NullList_YieldsNoSegments()
	{
		IReadOnlyList<TextDiagnosticSegment> segments = TextDiagnosticSegmentProjection.ProjectToleratingNulls(null);

		Assert.IsEmpty(segments);
	}

	[TestMethod]
	public void ProjectToleratingNulls_ListWithNullEntry_SkipsNullEntries()
	{
		IReadOnlyList<TextDiagnostic> diagnostics =
			[new TextDiagnostic(TextDiagnosticSeverity.Error, "first", 1, 2), null!, new TextDiagnostic(TextDiagnosticSeverity.Hint, "second", 4, 6)];

		IReadOnlyList<TextDiagnosticSegment> segments = TextDiagnosticSegmentProjection.ProjectToleratingNulls(diagnostics);

		Assert.HasCount(2, segments);
		Assert.AreEqual(1, segments[0].StartOffset);
		Assert.AreEqual(4, segments[1].StartOffset);
	}
}
