using Nickelony.Infrastructure;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class WorkspaceRootsTests
{
	[TestMethod]
	public void Create_NormalizesEachRootAndKeepsTheCallersOrder()
	{
		string rawPrimary = TestPaths.WithAlternateSeparators(TestPaths.Root);
		string rawSecondary = TestPaths.WithAlternateSeparators(TestPaths.SecondaryRoot);

		var roots = WorkspaceRoots.Create([rawPrimary, rawSecondary]);

		string primary = LocalPathRules.Normalize(rawPrimary);
		string secondary = LocalPathRules.Normalize(rawSecondary);

		Assert.AreSequenceEqual([primary, secondary], roots.Paths);
		Assert.HasCount(2, roots.Folders);
		Assert.AreEqual(new WorkspaceFolder(DocumentUriConversion.CreateFileUri(primary), "Workspace"), roots.Folders[0]);
		Assert.AreEqual(new WorkspaceFolder(DocumentUriConversion.CreateFileUri(secondary), "Secondary"), roots.Folders[1]);
		Assert.AreEqual(primary + ", " + secondary, roots.DisplayText);
	}

	[TestMethod]
	public void Create_WithoutRoots_ModelsAFolderlessSession()
	{
		var roots = WorkspaceRoots.Create([]);

		Assert.IsEmpty(roots.Paths);
		Assert.IsEmpty(roots.Folders);
		Assert.AreEqual(string.Empty, roots.DisplayText);
	}

	[TestMethod]
	public void Create_AcceptsNestedRoots()
	{
		var roots = WorkspaceRoots.Create([TestPaths.Root, Path.Combine(TestPaths.Root, "Nested")]);

		Assert.HasCount(2, roots.Folders);
		Assert.AreEqual("Nested", roots.Folders[1].Name);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public void Create_NamesAFilesystemRootByItself()
	{
		var roots = WorkspaceRoots.Create([@"C:\"]);

		Assert.HasCount(1, roots.Folders);
		Assert.AreEqual(@"C:\", roots.Folders[0].Name);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Linux | OperatingSystems.OSX)]
	public void Create_NamesTheRootDirectoryByItself()
	{
		var roots = WorkspaceRoots.Create(["/"]);

		Assert.HasCount(1, roots.Folders);
		Assert.AreEqual("/", roots.Folders[0].Name);
	}

	[TestMethod]
	public void Create_WithARelativeRoot_NamesTheParameter()
	{
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() => WorkspaceRoots.Create(["relative/root"]));
		Assert.AreEqual("workspaceRootDirectoryPaths", exception.ParamName);
	}

	[TestMethod]
	public void Create_WithRootsThatNormalizeToTheSamePath_Throws()
	{
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
			WorkspaceRoots.Create([TestPaths.Root, TestPaths.WithAlternateSeparators(TestPaths.Root)])
		);

		Assert.Contains("duplicate path", exception.Message);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Linux | OperatingSystems.OSX)]
	public void Create_WithAWindowsDrivePath_ThrowsBecauseThePathIsRelative() =>
		Assert.ThrowsExactly<ArgumentException>(() => WorkspaceRoots.Create([@"C:\Projects\Workspace"]));

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public void Create_WithARootedPathWithoutADrive_ThrowsBecauseThePathIsNotFullyQualified() =>
		Assert.ThrowsExactly<ArgumentException>(() => WorkspaceRoots.Create(["/home/user/workspace"]));
}
