namespace Nickelony.LanguageServer.Provider.Abstractions.Tests;

[TestClass]
public sealed class StartupFailureTests
{
	[TestMethod]
	public void Constructor_StoresCodeMessageAndPersistence()
	{
		var failure = new StartupFailure(ProviderFailureCodes.StartFailed, "message", isPersistent: true);

		Assert.AreEqual(ProviderFailureCodes.StartFailed, failure.Code);
		Assert.AreEqual("message", failure.Message);
		Assert.IsTrue(failure.IsPersistent);
	}

	[TestMethod]
	public void Constructor_DefaultException_IsNull() =>
		Assert.IsNull(new StartupFailure(ProviderFailureCodes.StartFailed, "message", isPersistent: false).Exception);

	[TestMethod]
	public void Constructor_StoresTheException()
	{
		var exception = new InvalidOperationException("The server did not start.");

		var failure = new StartupFailure(code: ProviderFailureCodes.StartFailed, message: "message", isPersistent: false, exception);

		Assert.AreSame(exception, failure.Exception);
	}

	[TestMethod]
	public void Constructor_NullCode_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => new StartupFailure(null!, "message", isPersistent: false));

	[TestMethod]
	[DataRow("", DisplayName = "Empty code is rejected")]
	[DataRow("   ", DisplayName = "Blank code is rejected")]
	public void Constructor_BlankCode_ThrowsArgumentException(string code) =>
		Assert.ThrowsExactly<ArgumentException>(() => new StartupFailure(code, "message", isPersistent: false));

	[TestMethod]
	public void Constructor_NullMessage_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => new StartupFailure(ProviderFailureCodes.StartFailed, null!, isPersistent: false));

	[TestMethod]
	[DataRow("", DisplayName = "Empty message is rejected")]
	[DataRow("   ", DisplayName = "Blank message is rejected")]
	public void Constructor_BlankMessage_ThrowsArgumentException(string message) =>
		Assert.ThrowsExactly<ArgumentException>(() => new StartupFailure(ProviderFailureCodes.StartFailed, message, isPersistent: false));

	[TestMethod]
	public void Equals_SameValues_AreEqual()
	{
		var first = new StartupFailure(ProviderFailureCodes.StartFailed, "message", isPersistent: true);
		var second = new StartupFailure(ProviderFailureCodes.StartFailed, "message", isPersistent: true);

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equals_DifferentPersistence_AreNotEqual()
	{
		var persistent = new StartupFailure(ProviderFailureCodes.StartFailed, "message", isPersistent: true);
		var transient = new StartupFailure(ProviderFailureCodes.StartFailed, "message", isPersistent: false);

		Assert.AreNotEqual(persistent, transient);
	}

	[TestMethod]
	public void Equals_DifferentCodes_AreNotEqual()
	{
		var started = new StartupFailure(ProviderFailureCodes.StartFailed, "message", isPersistent: true);
		var unavailable = new StartupFailure(ProviderFailureCodes.ClientUnavailable, "message", isPersistent: true);

		Assert.AreNotEqual(started, unavailable);
	}
}
