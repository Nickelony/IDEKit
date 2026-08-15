using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.IDEKit.IntelliSense.Tests.Diagnostics;

[TestClass]
public sealed class TextDiagnosticTests
{
	private static readonly TextDiagnostic s_diagnostic = new(TextDiagnosticSeverity.Error, "boom", 5, 10);

	[TestMethod]
	public void Constructor_NegativeStartOffset_Throws()
		=> Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextDiagnostic(TextDiagnosticSeverity.Error, "boom", -4, 6));

	[TestMethod]
	public void Constructor_BlankMessage_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new TextDiagnostic(TextDiagnosticSeverity.Error, "  ", 0, 2));

	[TestMethod]
	public void Constructor_EndOffsetAtStart_ProducesEmptySpanAtStart()
	{
		var diagnostic = new TextDiagnostic(TextDiagnosticSeverity.Error, "boom", 5, 5);

		Assert.AreEqual(5, diagnostic.StartOffset);
		Assert.AreEqual(5, diagnostic.EndOffset);
		Assert.IsTrue(diagnostic.Range.IsEmpty);
		Assert.IsTrue(diagnostic.ContainsOffset(5));
		Assert.IsFalse(diagnostic.ContainsOffset(4));
		Assert.IsFalse(diagnostic.ContainsOffset(6));
	}

	[TestMethod]
	public void Constructor_EndOffsetBeforeStart_Throws()
		=> Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextDiagnostic(TextDiagnosticSeverity.Error, "boom", 5, 2));

	[TestMethod]
	public void Constructor_StartOffsetAtIntMaxValue_ProducesEmptySpanAtIntMaxValue()
	{
		var diagnostic = new TextDiagnostic(TextDiagnosticSeverity.Error, "boom", int.MaxValue, int.MaxValue);

		Assert.AreEqual(int.MaxValue, diagnostic.StartOffset);
		Assert.AreEqual(int.MaxValue, diagnostic.EndOffset);
		Assert.IsTrue(diagnostic.ContainsOffset(int.MaxValue));
		Assert.IsFalse(diagnostic.Intersects(int.MaxValue - 1, int.MaxValue));
	}

	[TestMethod]
	public void Constructor_TextRange_StoresNormalizedSpan()
	{
		var diagnostic = new TextDiagnostic(TextDiagnosticSeverity.Warning, "boom", new TextRange(3, 4));

		Assert.AreEqual(3, diagnostic.StartOffset);
		Assert.AreEqual(7, diagnostic.EndOffset);
		Assert.AreEqual(new TextRange(3, 4), diagnostic.Range);
	}

	[TestMethod]
	public void Constructor_EmptyTextRange_ProducesEmptySpan()
	{
		var diagnostic = new TextDiagnostic(TextDiagnosticSeverity.Warning, "boom", new TextRange(3, 0));

		Assert.AreEqual(3, diagnostic.StartOffset);
		Assert.AreEqual(3, diagnostic.EndOffset);
		Assert.IsTrue(diagnostic.Range.IsEmpty);
	}

	[TestMethod]
	public void Range_AtSaturatedStartOffset_IsEmpty()
	{
		var diagnostic = new TextDiagnostic(TextDiagnosticSeverity.Error, "boom", int.MaxValue, int.MaxValue);

		Assert.AreEqual(int.MaxValue, diagnostic.Range.Offset);
		Assert.IsTrue(diagnostic.Range.IsEmpty);
	}

	[TestMethod]
	public void ContainsOffset_StartIsInclusiveAndEndIsExclusive()
	{
		Assert.IsFalse(s_diagnostic.ContainsOffset(4));
		Assert.IsTrue(s_diagnostic.ContainsOffset(5));
		Assert.IsTrue(s_diagnostic.ContainsOffset(9));
		Assert.IsFalse(s_diagnostic.ContainsOffset(10));
	}

	[TestMethod]
	public void ContainsOffset_NegativeOffset_IsFalse()
	{
		var emptySpan = new TextDiagnostic(TextDiagnosticSeverity.Error, "empty", 0, 0);

		Assert.IsFalse(s_diagnostic.ContainsOffset(-1));
		Assert.IsFalse(emptySpan.ContainsOffset(-1));
	}

	[TestMethod]
	public void Intersects_OverlappingRange_IsTrue()
	{
		Assert.IsTrue(s_diagnostic.Intersects(0, 6));
		Assert.IsTrue(s_diagnostic.Intersects(9, 20));
		Assert.IsTrue(s_diagnostic.Intersects(5, 10));
	}

	[TestMethod]
	public void Intersects_EmptyDiagnosticSpan_TreatsItsOffsetAsAPoint()
	{
		var empty = new TextDiagnostic(TextDiagnosticSeverity.Error, "empty", 7, 7);

		Assert.IsTrue(empty.Intersects(5, 10));
		Assert.IsFalse(empty.Intersects(7, 7));
		Assert.IsFalse(empty.Intersects(8, 10));
	}

	[TestMethod]
	public void Intersects_TouchingRange_IsFalse()
	{
		Assert.IsFalse(s_diagnostic.Intersects(0, 5));
		Assert.IsFalse(s_diagnostic.Intersects(10, 20));
	}

	[TestMethod]
	public void Intersects_EmptyOrReversedRange_IsFalse()
	{
		Assert.IsFalse(s_diagnostic.Intersects(6, 6));
		Assert.IsFalse(s_diagnostic.Intersects(8, 6));
	}

	[TestMethod]
	public void Constructor_StoresSeverityAndMessage()
	{
		Assert.AreEqual(TextDiagnosticSeverity.Error, s_diagnostic.Severity);
		Assert.AreEqual("boom", s_diagnostic.Message);
	}

	[TestMethod]
	public void Constructor_WithoutSourceAndCode_LeavesThemNull()
	{
		Assert.IsNull(s_diagnostic.Source);
		Assert.IsNull(s_diagnostic.Code);
	}

	[TestMethod]
	public void Initializer_BlankSourceAndCode_AreNormalizedToNull()
	{
		var diagnostic = new TextDiagnostic(TextDiagnosticSeverity.Error, "boom", 5, 10)
		{
			Source = " \t ",
			Code = "\r\n"
		};

		Assert.IsNull(diagnostic.Source);
		Assert.IsNull(diagnostic.Code);
	}

	[TestMethod]
	public void Initializer_StoresSourceAndCode()
	{
		var diagnostic = new TextDiagnostic(TextDiagnosticSeverity.Error, "boom", 5, 10)
		{
			Source = "LuaLS",
			Code = "W211"
		};

		Assert.AreEqual("LuaLS", diagnostic.Source);
		Assert.AreEqual("W211", diagnostic.Code);
	}

	[TestMethod]
	public void Initializer_TextRangeConstructor_StoresSourceAndCode()
	{
		var diagnostic = new TextDiagnostic(TextDiagnosticSeverity.Warning, "boom", new TextRange(3, 4))
		{
			Source = "LuaLS",
			Code = "W211"
		};

		Assert.AreEqual(3, diagnostic.StartOffset);
		Assert.AreEqual(7, diagnostic.EndOffset);
		Assert.AreEqual("LuaLS", diagnostic.Source);
		Assert.AreEqual("W211", diagnostic.Code);
	}

	[TestMethod]
	public void Initializer_SourceAndCode_AreTrimmed()
	{
		var diagnostic = new TextDiagnostic(TextDiagnosticSeverity.Error, "boom", 5, 10)
		{
			Source = "  LuaLS  ",
			Code = " W211 "
		};

		Assert.AreEqual("LuaLS", diagnostic.Source);
		Assert.AreEqual("W211", diagnostic.Code);
	}

	[TestMethod]
	public void Equality_ComparesEveryComponent()
	{
		var diagnostic = new TextDiagnostic(TextDiagnosticSeverity.Error, "boom", 5, 10) { Source = "LuaLS", Code = "W211" };
		var equal = new TextDiagnostic(TextDiagnosticSeverity.Error, "boom", 5, 10) { Source = "LuaLS", Code = "W211" };

		Assert.AreEqual(diagnostic, equal);
		Assert.AreEqual(diagnostic.GetHashCode(), equal.GetHashCode());
		Assert.AreNotEqual(diagnostic, new TextDiagnostic(TextDiagnosticSeverity.Warning, "boom", 5, 10) { Source = "LuaLS", Code = "W211" });
		Assert.AreNotEqual(diagnostic, new TextDiagnostic(TextDiagnosticSeverity.Error, "other", 5, 10) { Source = "LuaLS", Code = "W211" });
		Assert.AreNotEqual(diagnostic, new TextDiagnostic(TextDiagnosticSeverity.Error, "boom", 4, 10) { Source = "LuaLS", Code = "W211" });
		Assert.AreNotEqual(diagnostic, new TextDiagnostic(TextDiagnosticSeverity.Error, "boom", 5, 11) { Source = "LuaLS", Code = "W211" });
		Assert.AreNotEqual(diagnostic, new TextDiagnostic(TextDiagnosticSeverity.Error, "boom", 5, 10) { Source = "other", Code = "W211" });
		Assert.AreNotEqual(diagnostic, new TextDiagnostic(TextDiagnosticSeverity.Error, "boom", 5, 10) { Code = "W211" });
		Assert.AreNotEqual(diagnostic, new TextDiagnostic(TextDiagnosticSeverity.Error, "boom", 5, 10) { Source = "LuaLS", Code = "E999" });
	}

	[TestMethod]
	public void EqualityOperators_MatchStructuralEquality()
	{
		var diagnostic = new TextDiagnostic(TextDiagnosticSeverity.Error, "boom", 5, 10) { Source = "LuaLS" };
		var equal = new TextDiagnostic(TextDiagnosticSeverity.Error, "boom", 5, 10) { Source = "LuaLS" };

		Assert.IsTrue(diagnostic == equal);
		Assert.IsFalse(diagnostic != equal);
		Assert.IsTrue(diagnostic != new TextDiagnostic(TextDiagnosticSeverity.Warning, "boom", 5, 10) { Source = "LuaLS" });
		Assert.IsFalse(diagnostic == null);
		Assert.IsTrue((TextDiagnostic?)null == null);
	}

	[TestMethod]
	public void Intersects_NegativeRangeStart_StillIntersectsSpansAtTheDocumentStart()
	{
		var diagnostic = new TextDiagnostic(TextDiagnosticSeverity.Error, "boom", 0, 3);

		Assert.IsTrue(diagnostic.Intersects(-5, 1), "A range starting before the document still intersects the first span.");
		Assert.IsFalse(diagnostic.Intersects(-5, 0));
	}
}
