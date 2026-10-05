namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class DiagnosticMappingPolicyTests
{
	[TestMethod]
	public void Default_UsesTheNeutralFallbackMessageAndTheIdentifierWordRule()
	{
		DiagnosticMappingPolicy policy = DiagnosticMappingPolicy.Default;

		Assert.AreEqual("Unknown diagnostic.", policy.UnknownMessage);
		Assert.AreSame(IdentifierCharacterPolicy.Default, policy.WordCharacters);
		Assert.IsTrue(policy.WordCharacters.IsPartCharacter('a'));
		Assert.IsFalse(policy.WordCharacters.IsPartCharacter('.'));
	}

	[TestMethod]
	[DataRow('a', true)]
	[DataRow('Z', true)]
	[DataRow('7', true)]
	[DataRow('_', true)]
	[DataRow('.', false)]
	[DataRow(':', false)]
	[DataRow('-', false)]
	[DataRow(' ', false)]
	public void Default_WordCharacters_AcceptLettersDigitsAndUnderscoreOnly(char character, bool expected) =>
		Assert.AreEqual(expected, DiagnosticMappingPolicy.Default.WordCharacters.IsPartCharacter(character));

	[TestMethod]
	public void WithExpression_OverridesOneValueAndKeepsTheOther()
	{
		DiagnosticMappingPolicy policy = DiagnosticMappingPolicy.Default with { UnknownMessage = "Unknown script diagnostic." };

		Assert.AreEqual("Unknown script diagnostic.", policy.UnknownMessage);
		Assert.AreSame(DiagnosticMappingPolicy.Default.WordCharacters, policy.WordCharacters);
		Assert.AreEqual("Unknown diagnostic.", DiagnosticMappingPolicy.Default.UnknownMessage);
	}

	[TestMethod]
	public void WordCharacters_CustomPolicy_ReplacesTheDefaultRule()
	{
		var policy = new DiagnosticMappingPolicy { WordCharacters = IdentifierCharacterPolicy.Create(static character => character == '.') };

		Assert.IsTrue(policy.WordCharacters.IsPartCharacter('.'));
		Assert.IsFalse(policy.WordCharacters.IsPartCharacter('a'));
	}

	[TestMethod]
	public void UnknownMessage_Null_Throws() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new DiagnosticMappingPolicy { UnknownMessage = null! });

	[TestMethod]
	[DataRow("")]
	[DataRow(" ")]
	[DataRow("\t\r\n")]
	public void UnknownMessage_EmptyOrWhitespace_Throws(string message) =>
		Assert.ThrowsExactly<ArgumentException>(() => _ = new DiagnosticMappingPolicy { UnknownMessage = message });

	[TestMethod]
	public void WordCharacters_Null_Throws() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new DiagnosticMappingPolicy { WordCharacters = null! });
}
