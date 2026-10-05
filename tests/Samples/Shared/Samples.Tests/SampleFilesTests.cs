using System.IO;

#if AVALONIAEDIT
namespace Nickelony.Samples.Avalonia.Tests;

#else
namespace Nickelony.Samples.Wpf.Tests;

#endif

/// <summary>Covers the starter files that the sample writes into a new folder.</summary>
[TestClass]
public sealed class SampleFilesTests
{
	[TestMethod]
	public void Constructor_WritesTheProjectThatMakesTheCSharpFileACompilation()
	{
		using var directory = new TemporaryDirectory("sample-");

		var files = new SampleFiles(directory.Path);
		string project = File.ReadAllText(Path.Combine(files.Root, SampleFiles.CSharpProjectFileName));

		Assert.IsTrue(File.Exists(files.CSharpPath));
		Assert.Contains("<Project Sdk=\"Microsoft.NET.Sdk\">", project);
		Assert.Contains("<TargetFramework>net" + Environment.Version.Major + ".0</TargetFramework>", project);
	}

	[TestMethod]
	public void Constructor_ForAFolderOfTheUsersOwn_WritesNoProject()
	{
		using var directory = new TemporaryDirectory("sample-");

		var files = new SampleFiles(directory.Path, writeStarterFiles: false);

		Assert.IsFalse(File.Exists(Path.Combine(files.Root, SampleFiles.CSharpProjectFileName)));
	}

	[TestMethod]
	public void Constructor_KeepsAProjectThatTheFolderAlreadyHas()
	{
		using var directory = new TemporaryDirectory("sample-");
		string projectPath = Path.Combine(directory.Path, SampleFiles.CSharpProjectFileName);

		File.WriteAllText(projectPath, "<Project />");

		_ = new SampleFiles(directory.Path);

		Assert.AreEqual("<Project />", File.ReadAllText(projectPath));
	}
}
