namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class CommentSpanEnumeratorTests
{
	private static readonly CommentSyntax s_cStyleSyntax = CommentSyntaxTestHelpers.CStyle;

	[TestMethod]
	public void Enumerator_DirectConstruction_YieldsExpectedSpans()
	{
		const string text = "/* a */ x // b";

		var expectedSpans = new List<CommentSpan>
		{
			new(TextRange.FromBounds(0, 7), delimiterStart: 0, kind: CommentKind.Block),
			new(TextRange.FromBounds(9, 14), delimiterStart: 10, kind: CommentKind.Line),
		};

		var spans = new List<CommentSpan>();
		var enumerator = new CommentSpanEnumerator(text, s_cStyleSyntax);

		while (enumerator.MoveNext())
			spans.Add(enumerator.Current);

		Assert.AreSequenceEqual(expectedSpans, spans);
	}

	[TestMethod]
	public void Current_BeforeFirstMove_IsDefault()
	{
		var enumerator = new CommentSpanEnumerator("// x", s_cStyleSyntax);
		Assert.AreEqual(default, enumerator.Current);
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
