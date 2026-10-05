namespace Nickelony.LanguageServer.Provider.Abstractions.Tests;

[TestClass]
public sealed class WorkspaceFileWatchFailureTests
{
	[TestMethod]
	public void Constructor_StoresCodeAndMessage()
	{
		var failure = new WorkspaceFileWatchFailure(ProviderFailureCodes.FileWatchStartFailed, "message");

		Assert.AreEqual(ProviderFailureCodes.FileWatchStartFailed, failure.Code);
		Assert.AreEqual("message", failure.Message);
	}

	[TestMethod]
	public void Constructor_NullCode_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceFileWatchFailure(null!, "message"));

	[TestMethod]
	[DataRow("", DisplayName = "Empty code is rejected")]
	[DataRow("   ", DisplayName = "Blank code is rejected")]
	public void Constructor_BlankCode_ThrowsArgumentException(string code) =>
		Assert.ThrowsExactly<ArgumentException>(() => new WorkspaceFileWatchFailure(code, "message"));

	[TestMethod]
	public void Constructor_NullMessage_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceFileWatchFailure(ProviderFailureCodes.FileWatchStartFailed, null!));

	[TestMethod]
	[DataRow("", DisplayName = "Empty message is rejected")]
	[DataRow("   ", DisplayName = "Blank message is rejected")]
	public void Constructor_BlankMessage_ThrowsArgumentException(string message) =>
		Assert.ThrowsExactly<ArgumentException>(() => new WorkspaceFileWatchFailure(ProviderFailureCodes.FileWatchStartFailed, message));

	[TestMethod]
	public void Equals_SameValues_AreEqual()
	{
		var first = new WorkspaceFileWatchFailure(ProviderFailureCodes.FileWatchStartFailed, "message");
		var second = new WorkspaceFileWatchFailure(ProviderFailureCodes.FileWatchStartFailed, "message");

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equals_DifferentCodes_AreNotEqual()
	{
		var started = new WorkspaceFileWatchFailure(ProviderFailureCodes.FileWatchStartFailed, "message");
		var recovered = new WorkspaceFileWatchFailure(ProviderFailureCodes.FileWatchRecoveryFailed, "message");

		Assert.AreNotEqual(started, recovered);
	}

	[TestMethod]
	public void Constructor_StoresWorkspaceRootDirectoryPath()
	{
		var failure = new WorkspaceFileWatchFailure(ProviderFailureCodes.FileWatchStartFailed, "message", @"C:\Workspace\scripts");
		Assert.AreEqual(@"C:\Workspace\scripts", failure.WorkspaceRootDirectoryPath);
	}

	[TestMethod]
	public void Constructor_DefaultWorkspaceRootDirectoryPath_IsNull()
	{
		var failure = new WorkspaceFileWatchFailure(ProviderFailureCodes.FileWatchStartFailed, "message");
		Assert.IsNull(failure.WorkspaceRootDirectoryPath);
	}

	[TestMethod]
	public void Constructor_DefaultException_IsNull() =>
		Assert.IsNull(new WorkspaceFileWatchFailure(ProviderFailureCodes.FileWatchStartFailed, "message").Exception);

	[TestMethod]
	public void Constructor_StoresTheException()
	{
		var exception = new IOException("The watcher could not be created.");

		var failure = new WorkspaceFileWatchFailure(
			code: ProviderFailureCodes.FileWatchRecoveryFailed,
			message: "message",
			workspaceRootDirectoryPath: @"C:\Workspace",
			exception
		);

		Assert.AreSame(exception, failure.Exception);
	}
}
