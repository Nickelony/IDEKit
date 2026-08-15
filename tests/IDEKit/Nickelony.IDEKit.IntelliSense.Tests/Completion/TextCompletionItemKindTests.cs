using Nickelony.IDEKit.IntelliSense.Completion;
using Nickelony.IDEKit.IntelliSense.Tests.TestSupport;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

[TestClass]
public sealed class TextCompletionItemKindTests
{
	[TestMethod]
	public void WellKnownKinds_HaveUniqueIdentifiersAndAreRecognized()
	{
		// Derived from the public static surface so a newly added well-known kind cannot escape the check.
		TextCompletionItemKind[] kinds = StaticMemberReader.GetPropertyValues<TextCompletionItemKind>(typeof(TextCompletionItemKind));

		Assert.IsTrue(kinds.Length > 0);

		string[] expectedIdentifiers =
		[
			"Generic", "Text", "Property", "Array", "Section", "Directive", "Constant", "Keyword",
			"Method", "Function", "Constructor", "Event", "Operator", "Variable", "Value",
			"Reference", "Field", "Class", "Interface", "Enum", "EnumMember", "Struct", "TypeParameter",
			"Parameter", "Namespace", "Module", "Unit", "File", "Folder", "Snippet", "Color"
		];

		CollectionAssert.AreEquivalent(expectedIdentifiers, kinds.Select(kind => kind.Identifier).ToArray());
		Assert.AreEqual(kinds.Length, kinds.Select(kind => kind.Identifier).Distinct(StringComparer.Ordinal).Count());

		// Canonical protocol spellings (for example LSP's lowercase "method") must resolve to the
		// well-known instance instead of creating a phantom custom category.
		foreach (TextCompletionItemKind kind in kinds)
		{
			Assert.AreSame(
				kind,
				TextCompletionItemKind.FromIdentifier(kind.Identifier.ToLowerInvariant()),
				$"The lowercase identifier of '{kind.Identifier}' should resolve to the well-known kind.");
		}
	}

	[TestMethod]
	[DataRow("Generic")]
	[DataRow("generic")]
	[DataRow("METHOD")]
	[DataRow(" Method ")]
	public void CreateCustom_ReservedIdentifiersRegardlessOfCasingOrPadding_Throw(string identifier)
	{
		Assert.ThrowsExactly<ArgumentException>(() => TextCompletionItemKind.CreateCustom(identifier));
	}

	[TestMethod]
	public void ToStringAndOperators_UseTheNormalizedIdentifier()
	{
		TextCompletionItemKind kind = TextCompletionItemKind.Method;

		Assert.AreEqual("Method", kind.ToString());
		Assert.IsTrue(kind == TextCompletionItemKind.Method);
		Assert.IsFalse(kind == TextCompletionItemKind.Function);
		Assert.IsTrue(kind != TextCompletionItemKind.Function);
		Assert.IsTrue(kind == TextCompletionItemKind.FromIdentifier("method"));
	}

	[TestMethod]
	public void EqualityOperators_NullOperands_CompareByIdentity()
	{
		TextCompletionItemKind? missing = null;

		Assert.IsTrue(missing == null);
		Assert.IsFalse(missing != null);
		Assert.IsFalse(TextCompletionItemKind.Method == null);
		Assert.IsTrue(TextCompletionItemKind.Method != null);
		Assert.IsFalse(TextCompletionItemKind.Method.Equals((TextCompletionItemKind?)null));
	}

	[TestMethod]
	public void CreateCustom_ReturnsEqualInstancesPerIdentifier()
	{
		TextCompletionItemKind created = TextCompletionItemKind.CreateCustom("host.custom");
		TextCompletionItemKind parsed = TextCompletionItemKind.FromIdentifier("host.custom");

		Assert.AreEqual(created, parsed, "Repeated lookups of the same custom identifier should compare equal.");
		Assert.AreEqual(created.GetHashCode(), parsed.GetHashCode());
	}

	[TestMethod]
	public void CreateCustom_TrimsAndComparesByIdentifier()
	{
		TextCompletionItemKind customKind = TextCompletionItemKind.CreateCustom("  host.custom-type  ");
		TextCompletionItemKind roundTrippedKind = TextCompletionItemKind.FromIdentifier(customKind.Identifier);

		Assert.AreEqual("host.custom-type", customKind.Identifier);
		Assert.AreEqual(customKind, roundTrippedKind);
		Assert.AreEqual(customKind.GetHashCode(), roundTrippedKind.GetHashCode());
	}

	[TestMethod]
	public void CreateCustomAndFromIdentifier_WhitespaceOnlyIdentifier_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() => TextCompletionItemKind.CreateCustom(" \t\r\n "));
		Assert.ThrowsExactly<ArgumentException>(() => TextCompletionItemKind.FromIdentifier(" \t\r\n "));
	}

	[TestMethod]
	public void CreateCustom_ArbitraryNonBlankIdentifiers_KeepExactCasing()
	{
		Assert.AreEqual("host-kind", TextCompletionItemKind.CreateCustom("host-kind").Identifier);
		Assert.AreEqual("lua:type", TextCompletionItemKind.CreateCustom("lua:type").Identifier);
		Assert.AreEqual("_private", TextCompletionItemKind.CreateCustom("_private").Identifier);
		Assert.AreEqual("lua.type", TextCompletionItemKind.CreateCustom("lua.type").Identifier);
		Assert.AreEqual("OldCommand", TextCompletionItemKind.CreateCustom("OldCommand").Identifier);
		Assert.AreEqual("ümlaut", TextCompletionItemKind.CreateCustom("ümlaut").Identifier);
	}
}
