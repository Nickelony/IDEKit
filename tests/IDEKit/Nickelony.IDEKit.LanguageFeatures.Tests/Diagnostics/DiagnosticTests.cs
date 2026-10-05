using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class DiagnosticTests
{
	private static readonly Diagnostic s_diagnostic = new(severity: DiagnosticSeverity.Error, message: "boom", range: TextRange.FromBounds(5, 10));

	[TestMethod]
	public void Constructor_BlankMessage_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			new Diagnostic(severity: DiagnosticSeverity.Error, message: "  ", range: TextRange.FromBounds(0, 2))
		);
	}

	[TestMethod]
	public void Constructor_EmptyRange_ProducesEmptySpanAtStart()
	{
		var diagnostic = new Diagnostic(severity: DiagnosticSeverity.Error, message: "boom", range: TextRange.FromBounds(5, 5));

		Assert.AreEqual(new TextRange(5, 0), diagnostic.Range);
		Assert.IsTrue(diagnostic.Range.IsEmpty);
		Assert.IsTrue(diagnostic.ContainsOffset(5));
		Assert.IsFalse(diagnostic.ContainsOffset(4));
		Assert.IsFalse(diagnostic.ContainsOffset(6));
	}

	[TestMethod]
	public void Constructor_RangeAtIntMaxValue_ProducesEmptySpanAtIntMaxValue()
	{
		var diagnostic = new Diagnostic(severity: DiagnosticSeverity.Error, message: "boom", range: TextRange.FromBounds(int.MaxValue, int.MaxValue));

		Assert.AreEqual(new TextRange(int.MaxValue, 0), diagnostic.Range);
		Assert.IsTrue(diagnostic.ContainsOffset(int.MaxValue));
	}

	[TestMethod]
	public void Constructor_TextRange_StoresTheSpan()
	{
		var diagnostic = new Diagnostic(DiagnosticSeverity.Warning, "boom", new TextRange(3, 4));
		Assert.AreEqual(new TextRange(3, 4), diagnostic.Range);
	}

	[TestMethod]
	public void Range_AtSaturatedStartOffset_IsEmpty()
	{
		var diagnostic = new Diagnostic(severity: DiagnosticSeverity.Error, message: "boom", range: TextRange.FromBounds(int.MaxValue, int.MaxValue));

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
		var emptySpan = new Diagnostic(severity: DiagnosticSeverity.Error, message: "empty", range: TextRange.FromBounds(0, 0));

		Assert.IsFalse(s_diagnostic.ContainsOffset(-1));
		Assert.IsFalse(emptySpan.ContainsOffset(-1));
	}

	[TestMethod]
	public void Range_Intersects_ReachesADiagnosticAtItsBoundariesAndInside()
	{
		var empty = new Diagnostic(severity: DiagnosticSeverity.Error, message: "empty", range: TextRange.FromBounds(7, 7));

		Assert.IsTrue(s_diagnostic.Range.Intersects(new TextRange(5, 0)), "A caret at the start of the diagnostic reaches it.");
		Assert.IsTrue(s_diagnostic.Range.Intersects(new TextRange(7, 0)), "A caret inside the diagnostic reaches it.");
		Assert.IsTrue(s_diagnostic.Range.Intersects(new TextRange(10, 0)), "A caret at the end of the diagnostic reaches it.");
		Assert.IsTrue(s_diagnostic.Range.Intersects(TextRange.FromBounds(10, 12)), "A range that starts where the diagnostic ends reaches it.");
		Assert.IsTrue(s_diagnostic.Range.Intersects(TextRange.FromBounds(3, 5)), "A range that ends where the diagnostic starts reaches it.");
		Assert.IsFalse(s_diagnostic.Range.Intersects(new TextRange(4, 0)), "A caret before the diagnostic does not reach it.");
		Assert.IsFalse(s_diagnostic.Range.Intersects(new TextRange(11, 0)), "A caret after the diagnostic does not reach it.");
		Assert.IsTrue(empty.Range.Intersects(TextRange.FromBounds(5, 10)));
		Assert.IsTrue(empty.Range.Intersects(TextRange.FromBounds(7, 9)), "An empty diagnostic at the start boundary of the query matches.");
		Assert.IsFalse(empty.Range.Intersects(TextRange.FromBounds(8, 10)));
	}

	[TestMethod]
	public void Constructor_StoresSeverityAndMessage()
	{
		Assert.AreEqual(DiagnosticSeverity.Error, s_diagnostic.Severity);
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
		var diagnostic = new Diagnostic(severity: DiagnosticSeverity.Error, message: "boom", range: TextRange.FromBounds(5, 10))
		{
			Source = " \t ",
			Code = "\r\n",
		};

		Assert.IsNull(diagnostic.Source);
		Assert.IsNull(diagnostic.Code);
	}

	[TestMethod]
	public void Initializer_StoresSourceAndCode()
	{
		var diagnostic = new Diagnostic(severity: DiagnosticSeverity.Error, message: "boom", range: TextRange.FromBounds(5, 10))
		{
			Source = "LuaLS",
			Code = "W211",
		};

		Assert.AreEqual("LuaLS", diagnostic.Source);
		Assert.AreEqual("W211", diagnostic.Code);
	}

	[TestMethod]
	public void Initializer_TextRangeConstructor_StoresSourceAndCode()
	{
		var diagnostic = new Diagnostic(DiagnosticSeverity.Warning, "boom", new TextRange(3, 4)) { Source = "LuaLS", Code = "W211" };

		Assert.AreEqual(new TextRange(3, 4), diagnostic.Range);
		Assert.AreEqual("LuaLS", diagnostic.Source);
		Assert.AreEqual("W211", diagnostic.Code);
	}

	[TestMethod]
	public void Initializer_SourceAndCode_AreTrimmed()
	{
		var diagnostic = new Diagnostic(severity: DiagnosticSeverity.Error, message: "boom", range: TextRange.FromBounds(5, 10))
		{
			Source = "  LuaLS  ",
			Code = " W211 ",
		};

		Assert.AreEqual("LuaLS", diagnostic.Source);
		Assert.AreEqual("W211", diagnostic.Code);
	}

	[TestMethod]
	public void Equality_ComparesEveryComponent()
	{
		var diagnostic = new Diagnostic(severity: DiagnosticSeverity.Error, message: "boom", range: TextRange.FromBounds(5, 10))
		{
			Source = "LuaLS",
			Code = "W211",
		};

		var equal = new Diagnostic(severity: DiagnosticSeverity.Error, message: "boom", range: TextRange.FromBounds(5, 10))
		{
			Source = "LuaLS",
			Code = "W211",
		};

		Assert.AreEqual(diagnostic, equal);
		Assert.AreEqual(diagnostic.GetHashCode(), equal.GetHashCode());

		Assert.AreNotEqual(
			diagnostic,

			new Diagnostic(severity: DiagnosticSeverity.Warning, message: "boom", range: TextRange.FromBounds(5, 10))
			{
				Source = "LuaLS",
				Code = "W211",
			}
		);

		Assert.AreNotEqual(
			diagnostic,

			new Diagnostic(severity: DiagnosticSeverity.Error, message: "other", range: TextRange.FromBounds(5, 10))
			{
				Source = "LuaLS",
				Code = "W211",
			}
		);

		Assert.AreNotEqual(
			diagnostic,

			new Diagnostic(severity: DiagnosticSeverity.Error, message: "boom", range: TextRange.FromBounds(4, 10))
			{
				Source = "LuaLS",
				Code = "W211",
			}
		);

		Assert.AreNotEqual(
			diagnostic,

			new Diagnostic(severity: DiagnosticSeverity.Error, message: "boom", range: TextRange.FromBounds(5, 11))
			{
				Source = "LuaLS",
				Code = "W211",
			}
		);

		Assert.AreNotEqual(
			diagnostic,

			new Diagnostic(severity: DiagnosticSeverity.Error, message: "boom", range: TextRange.FromBounds(5, 10))
			{
				Source = "other",
				Code = "W211",
			}
		);

		Assert.AreNotEqual(
			diagnostic,
			new Diagnostic(severity: DiagnosticSeverity.Error, message: "boom", range: TextRange.FromBounds(5, 10)) { Code = "W211" }
		);

		Assert.AreNotEqual(
			diagnostic,

			new Diagnostic(severity: DiagnosticSeverity.Error, message: "boom", range: TextRange.FromBounds(5, 10))
			{
				Source = "LuaLS",
				Code = "E999",
			}
		);
	}

	[TestMethod]
	public void EqualityOperators_MatchStructuralEquality()
	{
		var diagnostic = new Diagnostic(severity: DiagnosticSeverity.Error, message: "boom", range: TextRange.FromBounds(5, 10)) { Source = "LuaLS" };

		var equal = new Diagnostic(severity: DiagnosticSeverity.Error, message: "boom", range: TextRange.FromBounds(5, 10)) { Source = "LuaLS" };

		Assert.IsTrue(diagnostic == equal);
		Assert.IsFalse(diagnostic != equal);

		Assert.IsTrue(
			diagnostic
				!= new Diagnostic(severity: DiagnosticSeverity.Warning, message: "boom", range: TextRange.FromBounds(5, 10)) { Source = "LuaLS" }
		);

		Assert.IsFalse(diagnostic == null);
		Assert.IsTrue((Diagnostic?)null == null);
	}
}
