namespace Nickelony.LanguageServer.Abstractions.Tests;

[TestClass]
public sealed class LanguageServerStartupFailureTests
{
	[TestMethod]
	public void Constructor_StoresMessageAndPersistence()
	{
		var failure = new LanguageServerStartupFailure("message", isPersistent: true);

		Assert.AreEqual("message", failure.Message);
		Assert.IsTrue(failure.IsPersistent);
	}

	[TestMethod]
	public void Constructor_NullMessage_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new LanguageServerStartupFailure(null!, isPersistent: false));
	}

	[TestMethod]
	public void Equals_SameValues_AreEqual()
	{
		var first = new LanguageServerStartupFailure("message", isPersistent: true);
		var second = new LanguageServerStartupFailure("message", isPersistent: true);

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equals_DifferentPersistence_AreNotEqual()
	{
		var persistent = new LanguageServerStartupFailure("message", isPersistent: true);
		var transient = new LanguageServerStartupFailure("message", isPersistent: false);

		Assert.AreNotEqual(persistent, transient);
	}
}
