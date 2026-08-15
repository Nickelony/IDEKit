using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.SemanticTokens;
using Nickelony.IDEKit.IntelliSense.Tests.TestSupport;

namespace Nickelony.IDEKit.IntelliSense.Tests.SemanticTokens;

[TestClass]
public sealed class TextSemanticTokenTests
{
	[TestMethod]
	public void Constructor_StoresRangeAndType()
	{
		var token = new TextSemanticToken(new TextRange(6, 5), TextSemanticTokenTypes.Variable);

		Assert.AreEqual(6, token.Range.Offset);
		Assert.AreEqual(5, token.Range.Length);
		Assert.AreEqual("variable", token.Type);
		Assert.AreEqual(0, token.Modifiers.Count);
	}

	[TestMethod]
	public void Constructor_BlankType_Throws()
	{
		// A token without a category cannot be mapped by a host, so blank types are rejected.
		Assert.ThrowsExactly<ArgumentException>(() => new TextSemanticToken(new TextRange(0, 4), string.Empty));
		Assert.ThrowsExactly<ArgumentException>(() => new TextSemanticToken(new TextRange(0, 4), "   "));
	}

	[TestMethod]
	public void Constructor_NullType_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextSemanticToken(new TextRange(0, 4), null!));
	}

	[TestMethod]
	public void Constructor_Modifiers_AreOwnedSnapshot()
	{
		var modifiers = new List<string> { TextSemanticTokenModifiers.Declaration, TextSemanticTokenModifiers.Static };
		var token = new TextSemanticToken(new TextRange(0, 4), TextSemanticTokenTypes.Method, modifiers);

		modifiers.Clear();

		Assert.AreEqual(2, token.Modifiers.Count);
		Assert.IsTrue(token.HasModifier(TextSemanticTokenModifiers.Declaration));
		Assert.IsTrue(token.HasModifier(TextSemanticTokenModifiers.Static));
	}

	[TestMethod]
	public void Constructor_DuplicateModifiers_AreDeduplicated()
	{
		var token = new TextSemanticToken(new TextRange(0, 4), TextSemanticTokenTypes.Variable, ["global", "global"]);

		Assert.AreEqual(1, token.Modifiers.Count);
		Assert.AreEqual("global", token.Modifiers[0]);
		Assert.IsTrue(token.HasModifier("global"));
	}

	[TestMethod]
	public void Constructor_PaddedModifiers_AreTrimmed()
	{
		var token = new TextSemanticToken(new TextRange(0, 4), TextSemanticTokenTypes.Variable, ["  global  "]);

		Assert.AreEqual("global", token.Modifiers[0]);
		Assert.IsTrue(token.HasModifier(" global "));
	}

	[TestMethod]
	public void Constructor_NullModifierElement_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new TextSemanticToken(new TextRange(0, 4), TextSemanticTokenTypes.Variable, ["global", null!]));

	[TestMethod]
	public void Constructor_BlankModifierElement_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new TextSemanticToken(new TextRange(0, 4), TextSemanticTokenTypes.Variable, ["  "]));

	[TestMethod]
	public void Constructor_NullOrEmptyModifiers_CollapseToNoModifiers()
	{
		var nullModifiers = new TextSemanticToken(new TextRange(0, 4), TextSemanticTokenTypes.Variable);
		var emptyModifiers = new TextSemanticToken(new TextRange(0, 4), TextSemanticTokenTypes.Variable, []);

		Assert.AreEqual(0, nullModifiers.Modifiers.Count);
		Assert.AreEqual(0, emptyModifiers.Modifiers.Count);
		Assert.AreEqual(nullModifiers, emptyModifiers);
	}

	[TestMethod]
	public void HasModifier_Null_ReturnsFalse()
	{
		var token = new TextSemanticToken(new TextRange(0, 4), TextSemanticTokenTypes.Variable, ["global"]);

		Assert.IsFalse(token.HasModifier(null));
	}

	[TestMethod]
	public void HasModifier_UnknownModifier_ReturnsFalse()
	{
		var token = new TextSemanticToken(new TextRange(0, 4), TextSemanticTokenTypes.Variable, ["global"]);

		Assert.IsFalse(token.HasModifier(TextSemanticTokenModifiers.Deprecated));
		Assert.IsFalse(token.HasModifier(string.Empty));
		Assert.IsFalse(token.HasModifier("   "));
	}

	[TestMethod]
	public void HasModifier_PaddedModifierName_IsTrimmed()
	{
		var token = new TextSemanticToken(new TextRange(0, 4), TextSemanticTokenTypes.Variable, ["global"]);

		Assert.IsTrue(token.HasModifier("  global  "));
	}

	[TestMethod]
	public void HasModifier_NoModifiers_ReturnsFalse()
	{
		var token = new TextSemanticToken(new TextRange(0, 4), TextSemanticTokenTypes.Number);

		Assert.IsFalse(token.HasModifier(TextSemanticTokenModifiers.Declaration));
	}

	[TestMethod]
	public void HasModifier_IsOrdinalCaseSensitive()
	{
		var token = new TextSemanticToken(new TextRange(0, 4), TextSemanticTokenTypes.Variable, ["global"]);

		Assert.IsFalse(token.HasModifier("Global"));
	}

	[TestMethod]
	public void TypeConstants_MatchLspTokenTypeNames()
	{
		// Derived from the public const surface so a renamed or added constant cannot escape the check.
		string[] declaredNames = StaticMemberReader.GetStringConstantValues(typeof(TextSemanticTokenTypes));

		string[] expectedNames =
		[
			"namespace", "type", "class", "enum", "interface", "struct", "typeParameter",
			"parameter", "variable", "property", "enumMember", "event", "function", "method",
			"macro", "keyword", "modifier", "comment", "string", "number", "regexp", "operator",
			"decorator", "label"
		];

		CollectionAssert.AreEquivalent(expectedNames, declaredNames);
	}

	[TestMethod]
	public void ModifierConstants_MatchLspModifierNames()
	{
		// Derived from the public const surface so a renamed or added constant cannot escape the check.
		string[] declaredNames = StaticMemberReader.GetStringConstantValues(typeof(TextSemanticTokenModifiers));

		string[] expectedNames =
		[
			"declaration", "definition", "readonly", "static", "deprecated", "abstract",
			"async", "modification", "documentation", "defaultLibrary"
		];

		CollectionAssert.AreEquivalent(expectedNames, declaredNames);
	}

	[TestMethod]
	public void Equality_SameComponents_AreValueEqual()
	{
		var first = new TextSemanticToken(new TextRange(6, 5), TextSemanticTokenTypes.Variable, ["global", "readonly"]);
		var second = new TextSemanticToken(new TextRange(6, 5), TextSemanticTokenTypes.Variable, ["global", "readonly"]);

		Assert.AreNotSame(first, second);
		Assert.AreEqual(first, second);
		Assert.IsTrue(first == second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equality_DifferentModifierSequence_AreNotEqual()
	{
		var forward = new TextSemanticToken(new TextRange(6, 5), TextSemanticTokenTypes.Variable, ["global", "readonly"]);
		var reordered = new TextSemanticToken(new TextRange(6, 5), TextSemanticTokenTypes.Variable, ["readonly", "global"]);
		var shorter = new TextSemanticToken(new TextRange(6, 5), TextSemanticTokenTypes.Variable, ["global"]);

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
		var token = new TextSemanticToken(new TextRange(6, 5), TextSemanticTokenTypes.Variable, modifiers);
		var expected = new TextSemanticToken(new TextRange(6, 5), TextSemanticTokenTypes.Variable, ["global", "readonly"]);
		int hashBeforeMutation = token.GetHashCode();

		modifiers.Clear();

		Assert.AreEqual(expected, token);
		Assert.AreEqual(hashBeforeMutation, token.GetHashCode());
	}

	[TestMethod]
	public void Equality_DifferentRangeTypeOrNull_AreNotEqual()
	{
		var token = new TextSemanticToken(new TextRange(6, 5), TextSemanticTokenTypes.Variable);

		Assert.AreNotEqual(token, new TextSemanticToken(new TextRange(7, 5), TextSemanticTokenTypes.Variable));
		Assert.AreNotEqual(token, new TextSemanticToken(new TextRange(6, 5), TextSemanticTokenTypes.Property));
		Assert.IsFalse(token.Equals(null));
		Assert.IsFalse(token == null);
		Assert.IsTrue(null != token);
	}

	[TestMethod]
	public void Constructor_PaddedType_IsTrimmed()
	{
		var token = new TextSemanticToken(new TextRange(0, 1), "  variable  ");

		Assert.AreEqual("variable", token.Type);
	}

	[TestMethod]
	public void Equality_IsOrdinalCaseSensitive()
	{
		var token = new TextSemanticToken(new TextRange(6, 5), "Variable");

		Assert.AreNotEqual(token, new TextSemanticToken(new TextRange(6, 5), "variable"));
	}
}
