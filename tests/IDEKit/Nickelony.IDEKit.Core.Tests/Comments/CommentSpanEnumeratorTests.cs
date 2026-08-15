namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class CommentSpanEnumeratorTests
{
	private static readonly CommentSyntax s_cStyleSyntax = CommentSyntaxFixtures.CStyle;

	[TestMethod]
	public void Enumerator_DirectConstruction_YieldsExpectedSpans()
	{
		const string text = "/* a */ x // b";
		var expectedSpans = new List<CommentSpan>
		{
			new(0, 0, 7, CommentKind.Block),
			new(9, 10, 14, CommentKind.Line)
		};

		var spans = new List<CommentSpan>();
		var enumerator = new CommentSpanEnumerator(text, s_cStyleSyntax);

		while (enumerator.MoveNext())
			spans.Add(enumerator.Current);

		CollectionAssert.AreEqual(expectedSpans, spans);
	}

	[TestMethod]
	public void Current_BeforeFirstMove_IsDefault()
	{
		var enumerator = new CommentSpanEnumerator("// x", s_cStyleSyntax);

		Assert.AreEqual(default(CommentSpan), enumerator.Current);
	}

	[TestMethod]
	public void MoveNext_AfterExhaustion_KeepsReturningFalse()
	{
		var enumerator = new CommentSpanEnumerator("// x", s_cStyleSyntax);

		Assert.IsTrue(enumerator.MoveNext());
		Assert.IsFalse(enumerator.MoveNext());
		Assert.IsFalse(enumerator.MoveNext());
	}
}
