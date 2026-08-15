namespace Nickelony.LanguageServer.Abstractions.Tests;

[TestClass]
public sealed class WorkspaceWatcherFailedEventArgsTests
{
	[TestMethod]
	public void Constructor_StoresFailure()
	{
		var failure = new WorkspaceWatcherFailure("message");
		var eventArgs = new WorkspaceWatcherFailedEventArgs(failure);

		Assert.AreEqual(failure, eventArgs.Failure);
	}

	[TestMethod]
	public void Constructor_NullFailure_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceWatcherFailedEventArgs(null!));
	}
}
