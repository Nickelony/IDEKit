namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class LanguageServerClientCapabilitiesOptionsTests
{
	[TestMethod]
	public void Default_HoldsNoKindsNoModifiersAndNoPullSupport()
	{
		LanguageServerClientCapabilitiesOptions options = LanguageServerClientCapabilitiesOptions.Default;

		Assert.IsEmpty(options.CodeActionKinds);
		Assert.IsEmpty(options.ExtraSemanticTokenModifiers);
		Assert.IsFalse(options.SupportsPullDiagnostics);
	}

	[TestMethod]
	public void CodeActionKinds_StoresACopyOfTheSuppliedList()
	{
		List<string> kinds = ["quickfix"];
		var options = new LanguageServerClientCapabilitiesOptions { CodeActionKinds = kinds };

		kinds.Add("refactor");

		Assert.AreSequenceEqual(["quickfix"], [.. options.CodeActionKinds]);
	}

	[TestMethod]
	public void CodeActionKinds_EmptyString_IsTheEmptyKindAndStaysValid()
	{
		var options = new LanguageServerClientCapabilitiesOptions { CodeActionKinds = ["", "quickfix"] };
		Assert.AreSequenceEqual(["", "quickfix"], [.. options.CodeActionKinds]);
	}

	[TestMethod]
	public void CodeActionKinds_Null_Throws() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new LanguageServerClientCapabilitiesOptions { CodeActionKinds = null! });

	[TestMethod]
	public void CodeActionKinds_NullEntry_Throws() =>
		Assert.ThrowsExactly<ArgumentException>(() => _ = new LanguageServerClientCapabilitiesOptions { CodeActionKinds = ["quickfix", null!] });

	[TestMethod]
	public void ExtraSemanticTokenModifiers_StoresACopyOfTheSuppliedList()
	{
		List<string> modifiers = ["global"];
		var options = new LanguageServerClientCapabilitiesOptions { ExtraSemanticTokenModifiers = modifiers };

		modifiers.Add("other");

		Assert.AreSequenceEqual(["global"], [.. options.ExtraSemanticTokenModifiers]);
	}

	[TestMethod]
	public void ExtraSemanticTokenModifiers_Null_Throws() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new LanguageServerClientCapabilitiesOptions { ExtraSemanticTokenModifiers = null! });

	[TestMethod]
	[DataRow("")]
	[DataRow("  ")]
	[DataRow(null)]
	public void ExtraSemanticTokenModifiers_BlankEntry_Throws(string? modifier)
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			_ = new LanguageServerClientCapabilitiesOptions { ExtraSemanticTokenModifiers = ["global", modifier!] }
		);
	}

	[TestMethod]
	public void WithExpression_KeepsTheValuesItDoesNotOverride()
	{
		LanguageServerClientCapabilitiesOptions options = new LanguageServerClientCapabilitiesOptions { CodeActionKinds = ["quickfix"] } with
		{
			SupportsPullDiagnostics = true,
		};

		Assert.IsTrue(options.SupportsPullDiagnostics);
		Assert.AreSequenceEqual(["quickfix"], [.. options.CodeActionKinds]);
	}
}
