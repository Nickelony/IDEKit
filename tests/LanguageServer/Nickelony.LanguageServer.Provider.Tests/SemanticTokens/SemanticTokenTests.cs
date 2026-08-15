namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class SemanticTokenTests
{
	[TestMethod]
	public void Constructor_StoresOwnedModifierSnapshot()
	{
		var modifiers = new List<string> { "readonly" };
		var token = new SemanticToken(0, 0, 1, "variable", modifiers);

		modifiers[0] = "changed";
		modifiers.Add("static");

		Assert.AreEqual(1, token.Modifiers.Count);
		Assert.AreEqual("readonly", token.Modifiers[0]);
		Assert.IsTrue(token.HasModifier("readonly"));
		Assert.IsFalse(token.HasModifier("changed"));
	}
}
