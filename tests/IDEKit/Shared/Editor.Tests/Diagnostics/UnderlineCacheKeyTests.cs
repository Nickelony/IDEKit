using System.Collections.ObjectModel;
#if AVALONIAEDIT
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Diagnostics;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Diagnostics;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

/// <summary>Pins that each component of the cache key decides on its own whether a cached geometry is still valid.</summary>
[TestClass]
public sealed class UnderlineCacheKeyTests
{
	private readonly ReadOnlyCollection<VisualLine> _visualLines = new([]);

	private readonly ITextSourceVersion _version;
	private readonly ITextSourceVersion _newerVersion;

	public UnderlineCacheKeyTests()
	{
		var document = new TextDocument("a");

		_version = document.Version;
		document.Insert(0, "b");
		_newerVersion = document.Version;
	}

	[TestMethod]
	public void Matches_EveryComponentTheSame_IsTrue()
	{
		var key = new UnderlineCacheKey(
			visualLines: _visualLines,
			scrollOffsetX: 3.0,
			scrollOffsetY: 4.0,
			documentVersion: _version,
			segmentGeneration: 2
		);

		var same = new UnderlineCacheKey(
			visualLines: _visualLines,
			scrollOffsetX: 3.0,
			scrollOffsetY: 4.0,
			documentVersion: _version,
			segmentGeneration: 2
		);

		Assert.IsTrue(key.Matches(same));
	}

	[TestMethod]
	public void Matches_AnotherVisualLineCollection_IsFalse()
	{
		var key = new UnderlineCacheKey(
			visualLines: _visualLines,
			scrollOffsetX: 3.0,
			scrollOffsetY: 4.0,
			documentVersion: _version,
			segmentGeneration: 2
		);

		var other = new UnderlineCacheKey(
			visualLines: new ReadOnlyCollection<VisualLine>([]),
			scrollOffsetX: 3.0,
			scrollOffsetY: 4.0,
			documentVersion: _version,
			segmentGeneration: 2
		);

		Assert.IsFalse(key.Matches(other));
	}

	[TestMethod]
	public void Matches_AnotherHorizontalScrollOffset_IsFalse()
	{
		var key = new UnderlineCacheKey(
			visualLines: _visualLines,
			scrollOffsetX: 3.0,
			scrollOffsetY: 4.0,
			documentVersion: _version,
			segmentGeneration: 2
		);

		var other = new UnderlineCacheKey(
			visualLines: _visualLines,
			scrollOffsetX: 3.5,
			scrollOffsetY: 4.0,
			documentVersion: _version,
			segmentGeneration: 2
		);

		Assert.IsFalse(key.Matches(other));
	}

	[TestMethod]
	public void Matches_AnotherVerticalScrollOffset_IsFalse()
	{
		var key = new UnderlineCacheKey(
			visualLines: _visualLines,
			scrollOffsetX: 3.0,
			scrollOffsetY: 4.0,
			documentVersion: _version,
			segmentGeneration: 2
		);

		var other = new UnderlineCacheKey(
			visualLines: _visualLines,
			scrollOffsetX: 3.0,
			scrollOffsetY: 4.5,
			documentVersion: _version,
			segmentGeneration: 2
		);

		Assert.IsFalse(key.Matches(other));
	}

	[TestMethod]
	public void Matches_AnotherDocumentVersion_IsFalse()
	{
		var key = new UnderlineCacheKey(
			visualLines: _visualLines,
			scrollOffsetX: 3.0,
			scrollOffsetY: 4.0,
			documentVersion: _version,
			segmentGeneration: 2
		);

		var other = new UnderlineCacheKey(
			visualLines: _visualLines,
			scrollOffsetX: 3.0,
			scrollOffsetY: 4.0,
			documentVersion: _newerVersion,
			segmentGeneration: 2
		);

		Assert.IsFalse(key.Matches(other));
	}

	[TestMethod]
	public void Matches_AnotherSegmentGeneration_IsFalse()
	{
		var key = new UnderlineCacheKey(
			visualLines: _visualLines,
			scrollOffsetX: 3.0,
			scrollOffsetY: 4.0,
			documentVersion: _version,
			segmentGeneration: 2
		);

		var other = new UnderlineCacheKey(
			visualLines: _visualLines,
			scrollOffsetX: 3.0,
			scrollOffsetY: 4.0,
			documentVersion: _version,
			segmentGeneration: 3
		);

		Assert.IsFalse(key.Matches(other));
	}
}
