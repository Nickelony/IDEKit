namespace Nickelony.LanguageServer.Provider.Abstractions.Tests;

[TestClass]
public sealed class WorkspaceFileWatchFailedEventArgsTests
{
	[TestMethod]
	public void Constructor_StoresFailure()
	{
		var failure = new WorkspaceFileWatchFailure(ProviderFailureCodes.FileWatchStartFailed, "message");
		var eventArgs = new WorkspaceFileWatchFailedEventArgs(failure);

		Assert.AreEqual(failure, eventArgs.Failure);
	}

	[TestMethod]
	public void Constructor_NullFailure_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceFileWatchFailedEventArgs(null!));
}
