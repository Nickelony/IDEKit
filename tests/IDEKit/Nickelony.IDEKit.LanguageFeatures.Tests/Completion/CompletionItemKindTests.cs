using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class CompletionItemKindTests
{
	[TestMethod]
	public void WellKnownKinds_HaveUniqueIdentifiersAndAreRecognized()
	{
		// Derived from the public static surface so a newly added well-known kind cannot escape the check.
		CompletionItemKind[] kinds = StaticMemberTestHelpers.GetPropertyValues<CompletionItemKind>(typeof(CompletionItemKind));

		Assert.IsNotEmpty(kinds);

		string[] expectedIdentifiers =
		[
			"Generic",
			"Text",
			"Property",
			"Array",
			"Section",
			"Directive",
			"Constant",
			"Keyword",
			"Method",
			"Function",
			"Constructor",
			"Event",
			"Operator",
			"Variable",
			"Value",
			"Reference",
			"Field",
			"Class",
			"Interface",
			"Enum",
			"EnumMember",
			"Struct",
			"TypeParameter",
			"Parameter",
			"Namespace",
			"Module",
			"Unit",
			"File",
			"Folder",
			"Snippet",
			"Color",
		];

		Assert.AreSequenceEqual(
			expectedIdentifiers,
			[.. kinds.Select(kind => kind.Identifier)],
			Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder
		);

		Assert.HasCount(kinds.Length, kinds.Select(kind => kind.Identifier).Distinct(StringComparer.Ordinal));

		// Canonical protocol spellings (for example LSP's lowercase "method") must resolve to the
		// well-known instance instead of creating a phantom custom category.
		foreach (CompletionItemKind kind in kinds)
		{
			Assert.AreSame(
				kind,
				CompletionItemKind.FromIdentifier(kind.Identifier.ToLowerInvariant()),
				$"The lowercase identifier of '{kind.Identifier}' should resolve to the well-known kind."
			);
		}
	}

	[TestMethod]
	[DataRow("Generic")]
	[DataRow("generic")]
	[DataRow("METHOD")]
	[DataRow(" Method ")]
	public void CreateCustom_ReservedIdentifiersRegardlessOfCasingOrPadding_Throw(string identifier) =>
		Assert.ThrowsExactly<ArgumentException>(() => CompletionItemKind.CreateCustom(identifier));

	[TestMethod]
	public void ToStringAndOperators_UseTheNormalizedIdentifier()
	{
		CompletionItemKind kind = CompletionItemKind.Method;

		Assert.AreEqual("Method", kind.ToString());
		Assert.IsTrue(kind == CompletionItemKind.Method);
		Assert.IsFalse(kind == CompletionItemKind.Function);
		Assert.IsTrue(kind != CompletionItemKind.Function);
		Assert.IsTrue(kind == CompletionItemKind.FromIdentifier("method"));
	}

	[TestMethod]
	public void EqualityOperators_NullOperands_CompareByIdentity()
	{
		CompletionItemKind? missing = null;

		Assert.IsTrue(missing == null);
		Assert.IsFalse(missing != null);
		Assert.IsFalse(CompletionItemKind.Method == null);
		Assert.IsTrue(CompletionItemKind.Method != null);
		Assert.IsFalse(CompletionItemKind.Method.Equals((CompletionItemKind?)null));
	}

	[TestMethod]
	public void CreateCustom_ReturnsEqualInstancesPerIdentifier()
	{
		var created = CompletionItemKind.CreateCustom("host.custom");
		var parsed = CompletionItemKind.FromIdentifier("host.custom");

		Assert.AreEqual(created, parsed, "Repeated lookups of the same custom identifier should compare equal.");
		Assert.AreEqual(created.GetHashCode(), parsed.GetHashCode());
	}

	[TestMethod]
	public void CreateCustom_TrimsAndComparesByIdentifier()
	{
		var customKind = CompletionItemKind.CreateCustom("  host.custom-type  ");
		var roundTrippedKind = CompletionItemKind.FromIdentifier(customKind.Identifier);

		Assert.AreEqual("host.custom-type", customKind.Identifier);
		Assert.AreEqual(customKind, roundTrippedKind);
		Assert.AreEqual(customKind.GetHashCode(), roundTrippedKind.GetHashCode());
	}

	[TestMethod]
	public void CreateCustomAndFromIdentifier_WhitespaceOnlyIdentifier_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() => CompletionItemKind.CreateCustom(" \t\r\n "));
		Assert.ThrowsExactly<ArgumentException>(() => CompletionItemKind.FromIdentifier(" \t\r\n "));
	}

	[TestMethod]
	public void CreateCustom_ArbitraryNonBlankIdentifiers_KeepExactCasing()
	{
		Assert.AreEqual("host-kind", CompletionItemKind.CreateCustom("host-kind").Identifier);
		Assert.AreEqual("lua:type", CompletionItemKind.CreateCustom("lua:type").Identifier);
		Assert.AreEqual("_private", CompletionItemKind.CreateCustom("_private").Identifier);
		Assert.AreEqual("lua.type", CompletionItemKind.CreateCustom("lua.type").Identifier);
		Assert.AreEqual("OldCommand", CompletionItemKind.CreateCustom("OldCommand").Identifier);
		Assert.AreEqual("ümlaut", CompletionItemKind.CreateCustom("ümlaut").Identifier);
	}
}
