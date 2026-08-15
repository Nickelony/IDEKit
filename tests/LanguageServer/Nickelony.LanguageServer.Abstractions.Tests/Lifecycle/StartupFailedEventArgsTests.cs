namespace Nickelony.LanguageServer.Abstractions.Tests;

[TestClass]
public sealed class StartupFailedEventArgsTests
{
	[TestMethod]
	public void Constructor_StoresFailure()
	{
		var failure = new LanguageServerStartupFailure("message", isPersistent: false);
		var eventArgs = new StartupFailedEventArgs(failure);

		Assert.AreEqual(failure, eventArgs.Failure);
	}

	[TestMethod]
	public void Constructor_NullFailure_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new StartupFailedEventArgs(null!));
	}
}
