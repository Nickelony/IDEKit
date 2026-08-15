using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

[TestClass]
public sealed class TextCompletionTriggerTests
{
	[TestMethod]
	public void Invoked_IsTheWellKnownReservedTrigger()
	{
		Assert.AreEqual("Invoked", TextCompletionTrigger.Invoked.Identifier);
		Assert.AreEqual("Invoked", TextCompletionTrigger.Invoked.ToString());
	}

	[TestMethod]
	public void CreateCustom_TrimsAndKeepsExactCasing()
	{
		Assert.AreEqual("EmptyLine", TextCompletionTrigger.CreateCustom("  EmptyLine  ").Identifier);
		Assert.AreEqual("contextual", TextCompletionTrigger.CreateCustom("contextual").Identifier);
		Assert.AreEqual("host-trigger", TextCompletionTrigger.CreateCustom("host-trigger").Identifier);
		Assert.AreEqual("ümlaut", TextCompletionTrigger.CreateCustom("ümlaut").Identifier);
	}

	[TestMethod]
	public void CreateCustom_NullIdentifier_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextCompletionTrigger.CreateCustom(null!));
	}

	[TestMethod]
	public void CreateCustom_BlankIdentifier_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() => TextCompletionTrigger.CreateCustom(string.Empty));
		Assert.ThrowsExactly<ArgumentException>(() => TextCompletionTrigger.CreateCustom(" \t\r\n "));
	}

	[TestMethod]
	[DataRow("invoked")]
	[DataRow("INVOKED")]
	[DataRow("Invoked")]
	[DataRow(" Invoked ")]
	public void CreateCustom_ReservedIdentifierRegardlessOfCasingOrPadding_Throws(string identifier)
	{
		Assert.ThrowsExactly<ArgumentException>(() => TextCompletionTrigger.CreateCustom(identifier));
	}

	[TestMethod]
	public void CreateCustom_RepeatedCalls_ProduceEqualButDistinctInstances()
	{
		TextCompletionTrigger first = TextCompletionTrigger.CreateCustom("EmptyLine");
		TextCompletionTrigger second = TextCompletionTrigger.CreateCustom("EmptyLine");

		Assert.AreNotSame(first, second);
		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equality_IsOrdinalCaseSensitive()
	{
		TextCompletionTrigger upper = TextCompletionTrigger.CreateCustom("Word");
		TextCompletionTrigger lower = TextCompletionTrigger.CreateCustom("word");

		Assert.AreNotEqual(upper, lower);
		Assert.IsTrue(upper != lower);
		Assert.IsFalse(upper.Equals(null));
	}

	[TestMethod]
	public void Operators_TreatNullsAsEqualOnlyToEachOther()
	{
		TextCompletionTrigger? left = null;
		TextCompletionTrigger? right = null;

		Assert.IsTrue(left == right);
		Assert.IsFalse(left != right);
		Assert.IsFalse(left == TextCompletionTrigger.Invoked);
		Assert.IsTrue(left != TextCompletionTrigger.Invoked);
		Assert.IsTrue(TextCompletionTrigger.Invoked == TextCompletionTrigger.Invoked);
	}

	[TestMethod]
	public void ToString_ReturnsTheNormalizedIdentifier()
	{
		Assert.AreEqual("EmptyLine", TextCompletionTrigger.CreateCustom("  EmptyLine ").ToString());
	}
}
