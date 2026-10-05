using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>
/// Covers the rebasing of document ids: which ids lie below a directory, the slice that remains, and the roots that end in a separator.
/// </summary>
[TestClass]
public sealed class WorkspaceDocumentPathTests
{
	private static readonly string s_directory = Path.Combine(Path.GetTempPath(), "workspace-document-path", "source");

	[TestMethod]
	public void GetRelativePathUnderDirectory_ADescendant_YieldsThePathBelowTheDirectory()
	{
		string documentId = Path.Combine(s_directory, "sub", "a.txt");

		Assert.AreEqual(
			Path.Combine("sub", "a.txt"),
			WorkspaceDocumentPath.GetRelativePathUnderDirectory(documentId, s_directory, LocalPathComparisonPolicy.CaseSensitive)
		);
	}

	[TestMethod]
	public void GetRelativePathUnderDirectory_TheDirectoryItself_YieldsAnEmptyPath()
	{
		Assert.AreEqual(
			string.Empty,
			WorkspaceDocumentPath.GetRelativePathUnderDirectory(s_directory, s_directory, LocalPathComparisonPolicy.CaseSensitive)
		);
	}

	[TestMethod]
	public void GetRelativePathUnderDirectory_ASiblingThatSharesTheNamePrefix_Throws()
	{
		string sibling = Path.Combine(s_directory + "-copy", "a.txt");

		Assert.ThrowsExactly<InvalidOperationException>(() =>
			WorkspaceDocumentPath.GetRelativePathUnderDirectory(sibling, s_directory, LocalPathComparisonPolicy.CaseSensitive)
		);
	}

	[TestMethod]
	public void GetRelativePathUnderDirectory_ADirectoryThatEndsInASeparator_KeepsTheFirstCharacterOfTheSlice()
	{
		string driveRoot = Path.GetPathRoot(Path.GetTempPath())!;
		string documentId = Path.Combine(driveRoot, "workspace-document-path", "a.txt");

		Assert.AreEqual(
			Path.Combine("workspace-document-path", "a.txt"),
			WorkspaceDocumentPath.GetRelativePathUnderDirectory(documentId, driveRoot, LocalPathComparisonPolicy.CaseSensitive)
		);
	}

	[TestMethod]
	[DataRow(true, true)]
	[DataRow(false, false)]
	public void GetRelativePathUnderDirectory_ADirectorySpelledInAnotherCase_FollowsThePolicy(bool ignoreCase, bool isBelow)
	{
		string documentId = Path.Combine(s_directory.ToUpperInvariant(), "A.txt");
		var policy = new LocalPathComparisonPolicy(ignoreCase);

		if (isBelow)
		{
			Assert.AreEqual("A.txt", WorkspaceDocumentPath.GetRelativePathUnderDirectory(documentId, s_directory, policy));
		}
		else
		{
			Assert.ThrowsExactly<InvalidOperationException>(() =>
				WorkspaceDocumentPath.GetRelativePathUnderDirectory(documentId, s_directory, policy)
			);
		}
	}

	[TestMethod]
	public void RebasePath_ADescendant_MovesItBelowTheDestination()
	{
		string destination = Path.Combine(Path.GetTempPath(), "workspace-document-path", "destination");
		string documentId = Path.Combine(s_directory, "sub", "a.txt");

		Assert.AreEqual(
			Path.Combine(destination, "sub", "a.txt"),

			WorkspaceDocumentPath.RebasePath(
				documentId,
				sourceDirectoryId: s_directory,
				destinationDirectoryId: destination,
				pathComparison: LocalPathComparisonPolicy.CaseSensitive
			)
		);
	}
}
