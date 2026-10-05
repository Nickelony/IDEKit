using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class CompletionContextTests
{
	[TestMethod]
	public void Invoked_IsTheWellKnownReservedTrigger()
	{
		Assert.AreEqual("Invoked", CompletionContext.Invoked.Identifier);
		Assert.AreEqual("Invoked", CompletionContext.Invoked.ToString());
	}

	[TestMethod]
	public void CreateCustom_TrimsAndKeepsExactCasing()
	{
		Assert.AreEqual("EmptyLine", CompletionContext.CreateCustom("  EmptyLine  ").Identifier);
		Assert.AreEqual("contextual", CompletionContext.CreateCustom("contextual").Identifier);
		Assert.AreEqual("host-trigger", CompletionContext.CreateCustom("host-trigger").Identifier);
		Assert.AreEqual("ümlaut", CompletionContext.CreateCustom("ümlaut").Identifier);
	}

	[TestMethod]
	public void CreateCustom_NullIdentifier_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => CompletionContext.CreateCustom(null!));

	[TestMethod]
	public void CreateCustom_BlankIdentifier_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() => CompletionContext.CreateCustom(string.Empty));
		Assert.ThrowsExactly<ArgumentException>(() => CompletionContext.CreateCustom(" \t\r\n "));
	}

	[TestMethod]
	[DataRow("invoked")]
	[DataRow("INVOKED")]
	[DataRow("Invoked")]
	[DataRow(" Invoked ")]
	public void CreateCustom_ReservedIdentifierRegardlessOfCasingOrPadding_Throws(string identifier) =>
		Assert.ThrowsExactly<ArgumentException>(() => CompletionContext.CreateCustom(identifier));

	[TestMethod]
	public void CreateCustom_RepeatedCalls_ProduceEqualButDistinctInstances()
	{
		var first = CompletionContext.CreateCustom("EmptyLine");
		var second = CompletionContext.CreateCustom("EmptyLine");

		Assert.AreNotSame(first, second);
		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equality_IsOrdinalCaseSensitive()
	{
		var upper = CompletionContext.CreateCustom("Word");
		var lower = CompletionContext.CreateCustom("word");

		Assert.AreNotEqual(upper, lower);
		Assert.IsTrue(upper != lower);
		Assert.IsFalse(upper.Equals(null));
	}

	[TestMethod]
	public void Operators_TreatNullsAsEqualOnlyToEachOther()
	{
		CompletionContext? left = null;
		CompletionContext? right = null;

		Assert.IsTrue(left == right);
		Assert.IsFalse(left != right);
		Assert.IsFalse(left == CompletionContext.Invoked);
		Assert.IsTrue(left != CompletionContext.Invoked);
		Assert.IsTrue(CompletionContext.Invoked == CompletionContext.Invoked);
	}

	[TestMethod]
	public void ToString_ReturnsTheNormalizedIdentifier() => Assert.AreEqual("EmptyLine", CompletionContext.CreateCustom("  EmptyLine ").ToString());

	[TestMethod]
	public void ForCharacter_CarriesTheCharacterUnderTheReservedIdentifier()
	{
		var trigger = CompletionContext.ForCharacter('.');

		Assert.AreEqual("TriggerCharacter", trigger.Identifier);
		Assert.AreEqual('.', trigger.TriggerCharacter);
		Assert.IsNull(CompletionContext.Invoked.TriggerCharacter);
	}

	[TestMethod]
	public void ForCharacter_WhitespaceCharacter_IsAValidTrigger() => Assert.AreEqual(' ', CompletionContext.ForCharacter(' ').TriggerCharacter);

	[TestMethod]
	public void ForCharacter_Equality_ComparesTheCharacter()
	{
		Assert.AreEqual(CompletionContext.ForCharacter('.'), CompletionContext.ForCharacter('.'));
		Assert.AreEqual(CompletionContext.ForCharacter('.').GetHashCode(), CompletionContext.ForCharacter('.').GetHashCode());
		Assert.AreNotEqual(CompletionContext.ForCharacter('.'), CompletionContext.ForCharacter(':'));
		Assert.AreNotEqual(CompletionContext.ForCharacter('.'), CompletionContext.Invoked);
	}

	[TestMethod]
	public void CreateCustom_TriggerCharacterIdentifier_IsReserved() =>
		Assert.ThrowsExactly<ArgumentException>(() => CompletionContext.CreateCustom(" triggercharacter "));
}
