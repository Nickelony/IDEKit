using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class SemanticTokenTests
{
	[TestMethod]
	public void Constructor_StoresRangeAndType()
	{
		var token = new SemanticToken(new TextRange(6, 5), SemanticTokenTypes.Variable);

		Assert.AreEqual(6, token.Range.Offset);
		Assert.AreEqual(5, token.Range.Length);
		Assert.AreEqual("variable", token.Type);
		Assert.IsEmpty(token.Modifiers);
	}

	[TestMethod]
	public void Constructor_BlankType_Throws()
	{
		// A token without a category cannot be mapped by a host, so blank types are rejected.
		Assert.ThrowsExactly<ArgumentException>(() => new SemanticToken(new TextRange(0, 4), string.Empty));
		Assert.ThrowsExactly<ArgumentException>(() => new SemanticToken(new TextRange(0, 4), "   "));
	}

	[TestMethod]
	public void Constructor_NullType_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => new SemanticToken(new TextRange(0, 4), null!));

	[TestMethod]
	public void Constructor_Modifiers_AreOwnedSnapshot()
	{
		var modifiers = new List<string> { SemanticTokenModifiers.Declaration, SemanticTokenModifiers.Static };
		var token = new SemanticToken(new TextRange(0, 4), SemanticTokenTypes.Method, modifiers);

		modifiers.Clear();

		Assert.HasCount(2, token.Modifiers);
		Assert.IsTrue(token.HasModifier(SemanticTokenModifiers.Declaration));
		Assert.IsTrue(token.HasModifier(SemanticTokenModifiers.Static));
	}

	[TestMethod]
	public void Constructor_DuplicateModifiers_AreDeduplicated()
	{
		var token = new SemanticToken(new TextRange(0, 4), SemanticTokenTypes.Variable, ["global", "global"]);

		Assert.HasCount(1, token.Modifiers);
		Assert.AreEqual("global", token.Modifiers[0]);
		Assert.IsTrue(token.HasModifier("global"));
	}

	[TestMethod]
	public void Constructor_PaddedModifiers_AreTrimmed()
	{
		var token = new SemanticToken(new TextRange(0, 4), SemanticTokenTypes.Variable, ["  global  "]);

		Assert.AreEqual("global", token.Modifiers[0]);
		Assert.IsTrue(token.HasModifier(" global "));
	}

	[TestMethod]
	public void Constructor_NullModifierElement_Throws() =>
		Assert.ThrowsExactly<ArgumentException>(() => new SemanticToken(new TextRange(0, 4), SemanticTokenTypes.Variable, ["global", null!]));

	[TestMethod]
	public void Constructor_BlankModifierElement_Throws() =>
		Assert.ThrowsExactly<ArgumentException>(() => new SemanticToken(new TextRange(0, 4), SemanticTokenTypes.Variable, ["  "]));

	[TestMethod]
	public void Constructor_NullOrEmptyModifiers_CollapseToNoModifiers()
	{
		var nullModifiers = new SemanticToken(new TextRange(0, 4), SemanticTokenTypes.Variable);
		var emptyModifiers = new SemanticToken(new TextRange(0, 4), SemanticTokenTypes.Variable, []);

		Assert.IsEmpty(nullModifiers.Modifiers);
		Assert.IsEmpty(emptyModifiers.Modifiers);
		Assert.AreEqual(nullModifiers, emptyModifiers);
	}

	[TestMethod]
	public void HasModifier_Null_ReturnsFalse()
	{
		var token = new SemanticToken(new TextRange(0, 4), SemanticTokenTypes.Variable, ["global"]);
		Assert.IsFalse(token.HasModifier(null));
	}

	[TestMethod]
	public void HasModifier_UnknownModifier_ReturnsFalse()
	{
		var token = new SemanticToken(new TextRange(0, 4), SemanticTokenTypes.Variable, ["global"]);

		Assert.IsFalse(token.HasModifier(SemanticTokenModifiers.Deprecated));
		Assert.IsFalse(token.HasModifier(string.Empty));
		Assert.IsFalse(token.HasModifier("   "));
	}

	[TestMethod]
	public void HasModifier_PaddedModifierName_IsTrimmed()
	{
		var token = new SemanticToken(new TextRange(0, 4), SemanticTokenTypes.Variable, ["global"]);
		Assert.IsTrue(token.HasModifier("  global  "));
	}

	[TestMethod]
	public void HasModifier_NoModifiers_ReturnsFalse()
	{
		var token = new SemanticToken(new TextRange(0, 4), SemanticTokenTypes.Number);
		Assert.IsFalse(token.HasModifier(SemanticTokenModifiers.Declaration));
	}

	[TestMethod]
	public void HasModifier_IsOrdinalCaseSensitive()
	{
		var token = new SemanticToken(new TextRange(0, 4), SemanticTokenTypes.Variable, ["global"]);
		Assert.IsFalse(token.HasModifier("Global"));
	}

	[TestMethod]
	public void TypeConstants_MatchLspTokenTypeNames()
	{
		// Derived from the public const surface so a renamed or added constant cannot escape the check.
		string[] declaredNames = StaticMemberTestHelpers.GetStringConstantValues(typeof(SemanticTokenTypes));

		string[] expectedNames =
		[
			"namespace",
			"type",
			"class",
			"enum",
			"interface",
			"struct",
			"typeParameter",
			"parameter",
			"variable",
			"property",
			"enumMember",
			"event",
			"function",
			"method",
			"macro",
			"keyword",
			"modifier",
			"comment",
			"string",
			"number",
			"regexp",
			"operator",
			"decorator",
			"label",
		];

		Assert.AreSequenceEqual(expectedNames, declaredNames, Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
	}

	[TestMethod]
	public void ModifierConstants_MatchLspModifierNames()
	{
		// Derived from the public const surface so a renamed or added constant cannot escape the check.
		string[] declaredNames = StaticMemberTestHelpers.GetStringConstantValues(typeof(SemanticTokenModifiers));

		string[] expectedNames =
		[
			"declaration",
			"definition",
			"readonly",
			"static",
			"deprecated",
			"abstract",
			"async",
			"modification",
			"documentation",
			"defaultLibrary",
		];

		Assert.AreSequenceEqual(expectedNames, declaredNames, Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
	}

	[TestMethod]
	public void Equality_SameComponents_AreValueEqual()
	{
		var first = new SemanticToken(new TextRange(6, 5), SemanticTokenTypes.Variable, ["global", "readonly"]);
		var second = new SemanticToken(new TextRange(6, 5), SemanticTokenTypes.Variable, ["global", "readonly"]);

		Assert.AreNotSame(first, second);
		Assert.AreEqual(first, second);
		Assert.IsTrue(first == second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equality_DifferentModifierSequence_AreNotEqual()
	{
		var forward = new SemanticToken(new TextRange(6, 5), SemanticTokenTypes.Variable, ["global", "readonly"]);
		var reordered = new SemanticToken(new TextRange(6, 5), SemanticTokenTypes.Variable, ["readonly", "global"]);
		var shorter = new SemanticToken(new TextRange(6, 5), SemanticTokenTypes.Variable, ["global"]);

		Assert.AreNotEqual(forward, reordered);
		Assert.IsTrue(forward != reordered);
		Assert.AreNotEqual(forward, shorter);
	}

	[TestMethod]
	public void Equality_ModifiersMutationAfterConstruction_KeepsEqualityAndHashStable()
	{
		// The constructor owns a snapshot of the modifier sequence; mutating the caller's list must
		// neither change the token's equality nor its hash code.
		var modifiers = new List<string> { "global", "readonly" };
		var token = new SemanticToken(new TextRange(6, 5), SemanticTokenTypes.Variable, modifiers);
		var expected = new SemanticToken(new TextRange(6, 5), SemanticTokenTypes.Variable, ["global", "readonly"]);

		int hashBeforeMutation = token.GetHashCode();

		modifiers.Clear();

		Assert.AreEqual(expected, token);
		Assert.AreEqual(hashBeforeMutation, token.GetHashCode());
	}

	[TestMethod]
	public void Equality_DifferentRangeTypeOrNull_AreNotEqual()
	{
		var token = new SemanticToken(new TextRange(6, 5), SemanticTokenTypes.Variable);

		Assert.AreNotEqual(token, new SemanticToken(new TextRange(7, 5), SemanticTokenTypes.Variable));
		Assert.AreNotEqual(token, new SemanticToken(new TextRange(6, 5), SemanticTokenTypes.Property));
		Assert.IsFalse(token.Equals(null));
		Assert.IsFalse(token == null);
		Assert.IsTrue(null != token);
	}

	[TestMethod]
	public void Constructor_PaddedType_IsTrimmed()
	{
		var token = new SemanticToken(new TextRange(0, 1), "  variable  ");
		Assert.AreEqual("variable", token.Type);
	}

	[TestMethod]
	public void Equality_IsOrdinalCaseSensitive()
	{
		var token = new SemanticToken(new TextRange(6, 5), "Variable");
		Assert.AreNotEqual(token, new SemanticToken(new TextRange(6, 5), "variable"));
	}
}
