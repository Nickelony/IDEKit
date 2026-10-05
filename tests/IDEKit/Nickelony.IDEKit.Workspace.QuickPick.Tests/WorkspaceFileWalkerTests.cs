using System.Diagnostics;

namespace Nickelony.IDEKit.Workspace.QuickPick.Tests;

/// <summary>Covers the walk of a root: which files it returns, what it skips, and how it ends on a missing folder or a canceled token.</summary>
[TestClass]
public sealed class WorkspaceFileWalkerTests
{
	private static string[] Walk(WorkspaceFileTestHost host, params string[] excludeGlobs)
	{
		return
		[
			.. WorkspaceFileWalker
				.List(
					directory: host.Root,
					root: host.Root,
					filter: new WorkspaceFileFilter(excludeGlobs, WorkspaceFileTestHost.Comparison),
					cancellationToken: CancellationToken.None
				)
				.Order(StringComparer.Ordinal),
		];
	}

	[TestMethod]
	public void List_ReturnsEveryFileBelowTheFolder()
	{
		using var host = new WorkspaceFileTestHost();

		string[] expected = [host.Write("a.txt"), host.Write("sub/b.txt"), host.Write("sub/deep/c.txt")];

		Assert.AreSequenceEqual(expected.Order(StringComparer.Ordinal), Walk(host));
	}

	[TestMethod]
	public void List_IncludesHiddenFiles()
	{
		using var host = new WorkspaceFileTestHost();
		string path = host.Write(".hidden");

		File.SetAttributes(path, FileAttributes.Hidden);

		Assert.AreSequenceEqual([path], Walk(host));
	}

	[TestMethod]
	public void List_ANameGlob_SkipsTheFolderAndEverythingBelowIt()
	{
		using var host = new WorkspaceFileTestHost();

		host.Write("bin/a.dll");
		host.Write("src/bin/deep/b.dll");

		string kept = host.Write("src/c.cs");

		Assert.AreSequenceEqual([kept], Walk(host, "bin"));
	}

	[TestMethod]
	public void List_AFileGlob_SkipsMatchingFilesOnly()
	{
		using var host = new WorkspaceFileTestHost();

		host.Write("a.tmp");
		host.Write("sub/b.tmp");

		string[] kept = [host.Write("a.cs"), host.Write("sub/b.cs")];

		Assert.AreSequenceEqual(kept.Order(StringComparer.Ordinal), Walk(host, "*.tmp"));
	}

	[TestMethod]
	public void List_APathGlob_SkipsThatFolderOnly()
	{
		using var host = new WorkspaceFileTestHost();

		host.Write("docs/build/a.html");
		string[] kept = [host.Write("build/b.html"), host.Write("docs/c.md")];

		Assert.AreSequenceEqual(kept.Order(StringComparer.Ordinal), Walk(host, "docs/build"));
	}

	[TestMethod]
	[DataRow("**/bin/**", "keep.cs|obj/d.dll|src/docs/build/e.html|src/obj/c.dll|docs/build/f.html")]
	[DataRow("src/**/obj", "keep.cs|bin/a.dll|obj/d.dll|src/bin/b.dll|src/docs/build/e.html|docs/build/f.html")]
	[DataRow("**/docs/build", "keep.cs|bin/a.dll|obj/d.dll|src/bin/b.dll|src/obj/c.dll")]
	[DataRow("bin", "keep.cs|obj/d.dll|src/docs/build/e.html|src/obj/c.dll|docs/build/f.html")]
	public void List_AGlobWithTheAnyDepthSegment_SkipsTheFoldersItNames(string glob, string expectedList)
	{
		using var host = new WorkspaceFileTestHost();
		string[] files = ["bin/a.dll", "src/bin/b.dll", "src/obj/c.dll", "obj/d.dll", "src/docs/build/e.html", "docs/build/f.html", "keep.cs"];

		foreach (string file in files)
			host.Write(file);

		string[] expected = [.. expectedList.Split('|').Select(host.PathOf).Order(StringComparer.Ordinal)];

		Assert.AreSequenceEqual(expected, Walk(host, glob));
	}

	[TestMethod]
	public void List_AFolderBelowTheRoot_ListsOnlyItsFiles()
	{
		using var host = new WorkspaceFileTestHost();

		host.Write("a.txt");
		string inner = host.Write("sub/b.txt");

		string[] files =
		[
			.. WorkspaceFileWalker.List(
				directory: host.PathOf("sub"),
				root: host.Root,
				filter: new WorkspaceFileFilter([], WorkspaceFileTestHost.Comparison),
				cancellationToken: CancellationToken.None
			),
		];

		Assert.AreSequenceEqual([inner], files);
	}

	[TestMethod]
	public void List_AFolderThatDoesNotExist_ListsNothing()
	{
		using var host = new WorkspaceFileTestHost();

		Assert.IsEmpty(
			WorkspaceFileWalker.List(
				directory: host.PathOf("missing"),
				root: host.Root,
				filter: new WorkspaceFileFilter([], WorkspaceFileTestHost.Comparison),
				cancellationToken: CancellationToken.None
			)
		);
	}

	[TestMethod]
	public void List_ACanceledToken_Throws()
	{
		using var host = new WorkspaceFileTestHost();
		using var source = new CancellationTokenSource();

		host.Write("a.txt");
		source.Cancel();

		Assert.ThrowsExactly<OperationCanceledException>(() =>
			WorkspaceFileWalker
				.List(
					directory: host.Root,
					root: host.Root,
					filter: new WorkspaceFileFilter([], WorkspaceFileTestHost.Comparison),
					cancellationToken: source.Token
				)
				.ToList()
		);
	}

	[TestMethod]
	public void List_ALinkToAFolder_IsNotEntered()
	{
		using var host = new WorkspaceFileTestHost();
		string target = host.Write("real/a.txt");

		if (!TryCreateFolderLink(host.PathOf("link"), Path.GetDirectoryName(target)!))
			Assert.Inconclusive("The session may create neither a symbolic link nor a junction.");

		Assert.AreSequenceEqual([target], Walk(host));
	}

	// A symbolic link needs a privilege on Windows, whereas a junction does not.
	private static bool TryCreateFolderLink(string link, string target)
	{
		try
		{
			Directory.CreateSymbolicLink(link, target);
			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
		{
			if (!OperatingSystem.IsWindows())
				return false;
		}

		using var process = Process.Start(
			new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
			{
				CreateNoWindow = true,
				UseShellExecute = false,
				RedirectStandardOutput = true,
			}
		);

		process?.WaitForExit();

		return process?.ExitCode == 0 && Directory.Exists(link);
	}
}
