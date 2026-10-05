namespace Nickelony.IDEKit.Markdown.Tests;

/// <summary>Pins the factories that build the edge widths a render plan carries.</summary>
[TestClass]
public sealed class MarkdownThicknessTests
{
	[TestMethod]
	public void Uniform_GivesEveryEdgeTheWidth() =>
		Assert.AreEqual(new MarkdownThickness(Left: 5.0, Top: 5.0, Right: 5.0, Bottom: 5.0), MarkdownThickness.Uniform(5.0));

	[TestMethod]
	public void Symmetric_PairsTheHorizontalEdgesAndTheVerticalEdges()
	{
		Assert.AreEqual(
			new MarkdownThickness(Left: 6.0, Top: 2.0, Right: 6.0, Bottom: 2.0),
			MarkdownThickness.Symmetric(horizontal: 6.0, vertical: 2.0)
		);
	}

	[TestMethod]
	public void Below_SetsOnlyTheBottomEdge() =>
		Assert.AreEqual(new MarkdownThickness(Left: 0.0, Top: 0.0, Right: 0.0, Bottom: 8.0), MarkdownThickness.Below(8.0));
}
