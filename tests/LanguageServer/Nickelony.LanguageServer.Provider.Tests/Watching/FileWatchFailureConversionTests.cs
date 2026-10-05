namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers how a watcher failure of the engine becomes the failure payload that a host receives.</summary>
[TestClass]
public sealed class FileWatchFailureConversionTests
{
	[TestMethod]
	public void ToWorkspaceFileWatchFailure_ForAFailedStart_NamesTheProviderAndTheRootWithTheStartCode()
	{
		var exception = new IOException("The watcher could not start.");

		var failure = FileWatchFailureConversion.ToWorkspaceFileWatchFailure(
			new FileWatchFailure(FileWatchFailureCodes.StartFailed, TestPaths.Root, exception),
			"Test"
		);

		Assert.AreEqual(ProviderFailureCodes.FileWatchStartFailed, failure.Code);
		Assert.AreEqual(TestPaths.Root, failure.WorkspaceRootDirectoryPath);
		Assert.AreSame(exception, failure.Exception);
		Assert.Contains($"The Test workspace file watcher could not be started for the workspace root '{TestPaths.Root}'.", failure.Message);
		Assert.Contains("Test language features will continue to work", failure.Message);
		Assert.Contains("external workspace changes may not be forwarded", failure.Message);
	}

	[TestMethod]
	public void ToWorkspaceFileWatchFailure_ForAFailedRecovery_NamesTheProviderAndTheRootWithTheRecoveryCode()
	{
		var failure = FileWatchFailureConversion.ToWorkspaceFileWatchFailure(
			new FileWatchFailure(FileWatchFailureCodes.RecoveryFailed, TestPaths.Root, Exception: null),
			"Test"
		);

		Assert.AreEqual(ProviderFailureCodes.FileWatchRecoveryFailed, failure.Code);
		Assert.AreEqual(TestPaths.Root, failure.WorkspaceRootDirectoryPath);
		Assert.IsNull(failure.Exception);

		Assert.Contains(
			$"The Test workspace file watcher encountered an internal error and automatic recovery failed for the workspace root '{TestPaths.Root}'.",
			failure.Message
		);
	}

	[TestMethod]
	public void ToWorkspaceFileWatchFailure_ForAnUnknownCode_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			FileWatchFailureConversion.ToWorkspaceFileWatchFailure(new FileWatchFailure("Unknown", TestPaths.Root, null), "Test")
		);
	}
}
