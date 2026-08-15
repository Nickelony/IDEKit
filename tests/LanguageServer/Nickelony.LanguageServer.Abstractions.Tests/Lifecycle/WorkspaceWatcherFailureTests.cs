namespace Nickelony.LanguageServer.Abstractions.Tests;

[TestClass]
public sealed class WorkspaceWatcherFailureTests
{
	[TestMethod]
	public void Constructor_StoresMessage()
	{
		var failure = new WorkspaceWatcherFailure("message");

		Assert.AreEqual("message", failure.Message);
	}

	[TestMethod]
	public void Constructor_NullMessage_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceWatcherFailure(null!));
	}

	[TestMethod]
	public void Equals_SameValues_AreEqual()
	{
		var first = new WorkspaceWatcherFailure("message");
		var second = new WorkspaceWatcherFailure("message");

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Constructor_StoresWorkspaceRootDirectoryPath()
	{
		var failure = new WorkspaceWatcherFailure("message", @"C:\Workspace\scripts");

		Assert.AreEqual(@"C:\Workspace\scripts", failure.WorkspaceRootDirectoryPath);
	}

	[TestMethod]
	public void Constructor_DefaultWorkspaceRootDirectoryPath_IsNull()
	{
		var failure = new WorkspaceWatcherFailure("message");

		Assert.IsNull(failure.WorkspaceRootDirectoryPath);
	}
}
