using Nickelony.QuickPick;

namespace Nickelony.IDEKit.Workspace.QuickPick.Tests;

/// <summary>Covers the options of the files mode: the defaults, and the validation of each option when it is assigned.</summary>
[TestClass]
public sealed class WorkspaceFileQuickPickOptionsTests
{
	[TestMethod]
	public void Default_ExcludesNothing() => Assert.IsEmpty(WorkspaceFileQuickPickOptions.Default.ExcludeGlobs);

	[TestMethod]
	public void Default_HoldsFiftyThousandFilesAndTheUnsavedText()
	{
		Assert.AreEqual(50_000, WorkspaceFileQuickPickOptions.Default.MaxFileCount);
		Assert.AreEqual("unsaved", WorkspaceFileQuickPickOptions.Default.UnsavedText);
	}

	[TestMethod]
	public void Default_TextsAreEnglish()
	{
		WorkspaceFileQuickPickOptions options = WorkspaceFileQuickPickOptions.Default;

		Assert.AreEqual("Files", options.Name);
		Assert.AreEqual("Type the name of a file to open", options.Placeholder);
		Assert.AreEqual("No matching files", options.EmptyText);
		Assert.AreEqual("recently opened", options.RecentGroupLabel);
		Assert.AreEqual("file results", options.FileGroupLabel);
	}

	[TestMethod]
	public void Texts_ANullTextThrowsAndANullGroupLabelDrawsNoSeparator()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceFileQuickPickOptions { Name = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceFileQuickPickOptions { Placeholder = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceFileQuickPickOptions { EmptyText = null! });
		Assert.IsNull(new WorkspaceFileQuickPickOptions { RecentGroupLabel = null }.RecentGroupLabel);
		Assert.IsNull(new WorkspaceFileQuickPickOptions { FileGroupLabel = null }.FileGroupLabel);
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(-1)]
	[DataRow(int.MinValue)]
	public void MaxFileCount_ZeroOrNegative_Throws(int value) =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new WorkspaceFileQuickPickOptions { MaxFileCount = value });

	[TestMethod]
	public void MaxFileCount_APositiveValue_IsKept()
	{
		Assert.AreEqual(1, new WorkspaceFileQuickPickOptions { MaxFileCount = 1 }.MaxFileCount);
		Assert.AreEqual(int.MaxValue, new WorkspaceFileQuickPickOptions { MaxFileCount = int.MaxValue }.MaxFileCount);
	}

	[TestMethod]
	public void UnsavedText_NullIsAllowedAndKept() => Assert.IsNull(new WorkspaceFileQuickPickOptions { UnsavedText = null }.UnsavedText);

	[TestMethod]
	public void ExcludeGlobs_AreCopiedWhenAssigned()
	{
		List<string> globs = ["bin"];
		WorkspaceFileQuickPickOptions options = new() { ExcludeGlobs = globs };

		globs.Add("obj");

		Assert.AreSequenceEqual(["bin"], options.ExcludeGlobs);
	}

	[TestMethod]
	public void ExcludeGlobs_NullListOrNullEntry_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceFileQuickPickOptions { ExcludeGlobs = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceFileQuickPickOptions { ExcludeGlobs = [null!] });
	}

	[TestMethod]
	[DataRow("")]
	[DataRow("  ")]
	public void ExcludeGlobs_BlankEntry_Throws(string glob) =>
		Assert.ThrowsExactly<ArgumentException>(() => new WorkspaceFileQuickPickOptions { ExcludeGlobs = [glob] });
}
