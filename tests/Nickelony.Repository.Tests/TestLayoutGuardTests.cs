using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// Test source that several projects compile lives under <c>tests/</c>, in its family's folder, never in the folder of one project.
/// Every helper a test project links sits in a virtual <c>TestSupport\</c> folder, so a reader finds each helper by one name and <c>shared/</c> says
/// that its files ship.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed class TestLayoutGuardTests
{
	[TestMethod]
	public void SharedHoldsNoTestSource()
	{
		List<string> violations = [];

		foreach (string path in RepositoryModel.SourcePathsUnder("shared"))
		{
			string[] folders = path.Split('/')[..^1];
			string text = RepositoryModel.Source(path).Text;

			if (folders.Any(folder => folder.Contains("Test", StringComparison.OrdinalIgnoreCase)))
			{
				violations.Add(
					$"{path} sits in a test folder under shared/. Product source only lives there: move it below {string.Join(" or ", RepositoryModel.SharedTreeFolders)} or into a TestSupport folder."
				);
			}
			else if (
				text.Contains("[TestMethod", StringComparison.Ordinal) || text.Contains("Microsoft.VisualStudio.TestTools", StringComparison.Ordinal)
			)
			{
				violations.Add($"{path} uses the test framework. Test source lives under tests/, in its family's folder.");
			}
		}

		Assert.IsEmpty(violations, "Test source under shared/:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void NoTestFolderIsNamedTestHelpersOrSupport()
	{
		string root = RepositoryPaths.Root();

		List<string> violations =
		[
			.. Directory
				.EnumerateDirectories(RepositoryPaths.Below("tests"), "*", SearchOption.AllDirectories)
				.Select(directory => Path.GetRelativePath(root, directory).Replace('\\', '/'))
				.Where(directory => !directory.Split('/').Any(segment => segment is "bin" or "obj"))
				.Where(directory => directory.Split('/')[^1] is "TestHelpers" or "Support")
				.Order(StringComparer.Ordinal)
				.Select(directory => $"{directory} is a helper folder with a second spelling. Name it TestSupport."),
		];

		Assert.IsEmpty(violations, "Helper folders:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void EveryTestFileLinkedFromOutsideItsProjectSitsInAVirtualTestSupportFolder()
	{
		string root = RepositoryPaths.Root();
		List<string> violations = [];

		foreach (ProjectFile project in RepositoryModel.Projects.Where(project => project.IsTestProject))
		{
			string directory = RepositoryPaths.Below(project.RelativeDirectory.Split('/'));
			var document = XDocument.Load(RepositoryPaths.Below(project.RelativePath.Split('/')));

			foreach (XElement item in document.Descendants("Compile"))
			{
				string? include = (string?)item.Attribute("Include");

				if (include is null)
					continue;

				string source = Path.GetRelativePath(root, Path.GetFullPath(Path.Combine(directory, include))).Replace('\\', '/');

				if (source.StartsWith(project.RelativeDirectory + "/", StringComparison.OrdinalIgnoreCase))
					continue;

				bool inSharedTree = RepositoryModel.SharedTreeFolders.Any(folder =>
					source.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)
				);

				bool inSupportFolder = source.Split('/')[..^1].Contains("TestSupport", StringComparer.OrdinalIgnoreCase);

				ProjectFile? owner = RepositoryModel.Projects.FirstOrDefault(other =>
					source.StartsWith(other.RelativeDirectory + "/", StringComparison.OrdinalIgnoreCase)
				);

				if (owner is not null)
				{
					violations.Add(
						$"{project.RelativePath}: links {source}, which sits in the folder of {owner.Name}. Move it to the nearest TestSupport folder that every compiling project shares."
					);

					continue;
				}

				if (!inSharedTree && !inSupportFolder)
				{
					violations.Add(
						$"{project.RelativePath}: links {source}, which sits in neither a TestSupport folder nor {string.Join(" or ", RepositoryModel.SharedTreeFolders)}. Move it to the nearest TestSupport folder that every compiling project shares."
					);

					continue;
				}

				if (inSharedTree)
				{
					if ((string?)item.Attribute("LinkBase") != ".")
					{
						violations.Add(
							$"{project.RelativePath}: links the shared test tree {source} without LinkBase=\".\". Link the tree with LinkBase=\".\" so each file sits beside the engine files of its folder."
						);
					}

					continue;
				}

				string? link = ((string?)item.Attribute("Link"))?.Replace('/', '\\');

				if (link is null || !link.StartsWith(@"TestSupport\", StringComparison.Ordinal))
				{
					violations.Add(
						$"{project.RelativePath}: links {source} as '{link ?? "no link"}'. Link it into the virtual TestSupport\\ folder."
					);
				}
			}
		}

		Assert.IsEmpty(violations, "Test links:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void EverySharedTestTreeIsIncludedByWildcardInItsProjects()
	{
		List<string> violations = [];

		(string Root, string Tree)[] trees =
		[
			.. RepositoryModel.SharedTreeFolders.SelectMany(root =>
				RepositoryModel.SubfoldersOf(root).Select(path => (Root: root, Tree: path[(root.Length + 1)..]))
			),
		];

		Assert.IsNotEmpty(trees, $"{string.Join(" and ", RepositoryModel.SharedTreeFolders)} hold no shared test tree, so the guard checks nothing.");

		foreach ((string root, string tree) in trees)
		{
			string treeDirectory = RepositoryPaths.Below([.. root.Split('/'), tree]);

			IReadOnlyList<string> treeFiles = RepositoryModel.SourcePathsUnder($"{root}/{tree}");

			if (!TryGetProjectNames(root, tree, out string[] projectNames))
			{
				violations.Add(
					$"{root}/{tree}: is no Editor{{.Package}}.Tests tree of the editor bindings, Presenter.Tests tree of the quick-pick toolkits or Samples.Tests tree of the sample heads."
				);

				continue;
			}

			foreach (string name in projectNames)
			{
				if (!RepositoryModel.HasProject(name))
				{
					violations.Add($"{root}/{tree}: no test project '{name}' compiles it. Add the project, or remove the tree.");
					continue;
				}

				ProjectFile project = RepositoryModel.Project(name);
				string projectDirectory = RepositoryPaths.Below(project.RelativeDirectory.Split('/'));

				var document = XDocument.Load(RepositoryPaths.Below(project.RelativePath.Split('/')));

				bool includesTree = document
					.Descendants("Compile")
					.Select(item => (string?)item.Attribute("Include"))
					.OfType<string>()
					.Any(include => IsWildcardIncludeOf(include, projectDirectory, treeDirectory));

				if (!includesTree)
				{
					violations.Add(
						$"{project.RelativePath}: does not include {root}/{tree} with one wildcard (Include=\"...\\{tree}\\**\\*.cs\"), so a dropped include would remove its tests silently."
					);
				}

				foreach (string file in treeFiles.Except(project.Sources, StringComparer.Ordinal))
				{
					violations.Add(
						$"{project.RelativePath}: does not compile {file}. A Compile Remove or an Include that skips the file drops a shared test."
					);
				}
			}
		}

		Assert.IsEmpty(violations, "Shared test trees:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	/// <summary>
	/// Resolves the test projects that compile a shared test tree: the engine pair of an editor tree, the toolkit pair of the quick-pick tree,
	/// or the head pair of the sample tree.
	/// </summary>
	private static bool TryGetProjectNames(string root, string tree, out string[] projectNames)
	{
		if (root == RepositoryModel.QuickPickSharedTreesFolder)
		{
			projectNames = tree == "Presenter.Tests" ? ["Nickelony.QuickPick.Wpf.Tests", "Nickelony.QuickPick.Avalonia.Tests"] : [];
			return projectNames.Length > 0;
		}

		if (root == RepositoryModel.SamplesSharedTreesFolder)
		{
			projectNames = tree == "Samples.Tests" ? ["Nickelony.Samples.Wpf.Tests", "Nickelony.Samples.Avalonia.Tests"] : [];
			return projectNames.Length > 0;
		}

		if (!tree.StartsWith("Editor", StringComparison.Ordinal) || !tree.EndsWith(".Tests", StringComparison.Ordinal))
		{
			projectNames = [];
			return false;
		}

		// Each binding's test project of the package is named Nickelony.IDEKit.{Engine}{.Package}.Tests.
		string package = tree["Editor".Length..^".Tests".Length];
		projectNames = [$"Nickelony.IDEKit.AvalonEdit{package}.Tests", $"Nickelony.IDEKit.AvaloniaEdit{package}.Tests"];

		return true;
	}

	/// <summary>
	/// Reports whether a <c>Compile</c> include is a recursive wildcard over the given tree, as <c>..\..\Shared\{Tree}\**\*.cs</c> is.
	/// </summary>
	private static bool IsWildcardIncludeOf(string include, string projectDirectory, string treeDirectory)
	{
		const string Suffix = "/**/*.cs";

		string normalized = include.Replace('\\', '/');

		if (!normalized.EndsWith(Suffix, StringComparison.Ordinal))
			return false;

		string folder = Path.GetFullPath(Path.Combine(projectDirectory, normalized[..^Suffix.Length]));

		return string.Equals(folder.TrimEnd('\\', '/'), treeDirectory.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
	}

	[TestMethod]
	public void AFileOfAProductSharedHomeIsCompiledByTwoProjects()
	{
		List<string> violations = [];
		string root = RepositoryPaths.Root();

		foreach (string full in Directory.EnumerateFiles(RepositoryPaths.Below("shared"), "*.cs", SearchOption.AllDirectories))
		{
			string path = Path.GetRelativePath(root, full).Replace('\\', '/');

			if (path.Contains("/obj/", StringComparison.Ordinal) || path.Contains("/bin/", StringComparison.Ordinal))
				continue;

			int compiling = RepositoryModel.Projects.Count(project =>
				!project.IsTestProject && project.Sources.Contains(path, StringComparer.Ordinal)
			);

			if (compiling < 2)
				violations.Add($"{path} is compiled by {compiling} projects, and a file of shared/ holds source that two compile.");
		}

		Assert.IsEmpty(
			violations,
			"Shared product files:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal))
		);
	}

	[TestMethod]
	public void TheSharedReadmeNamesEachHomeTheProjectsThatCompileItAndItsDefine()
	{
		List<string> violations = [];
		HashSet<string> named = new(StringComparer.Ordinal);

		string readme = RepositoryPaths.Below("shared", "README.md");

		foreach (string line in File.ReadLines(readme).Where(line => line.StartsWith("| `", StringComparison.Ordinal)))
		{
			string[] cells = line.Split('|', StringSplitOptions.TrimEntries);

			string folder = "shared/" + cells[1].Trim('`').TrimEnd('/');
			string define = cells[3].Trim('`');

			string[] namedProjects =
			[
				.. cells[2]
					.Split(',', StringSplitOptions.TrimEntries)
					.Select(cell => cell.Trim('`'))
					.Where(cell => cell.StartsWith("Nickelony.", StringComparison.Ordinal))
					.Order(StringComparer.Ordinal),
			];

			string[] compiling =
			[
				.. RepositoryModel
					.Projects.Where(project =>
						!project.IsTestProject && project.Sources.Any(source => source.StartsWith(folder + "/", StringComparison.Ordinal))
					)
					.Select(project => project.Name)
					.Order(StringComparer.Ordinal),
			];

			named.Add(folder);

			if (!namedProjects.SequenceEqual(compiling, StringComparer.Ordinal))
			{
				violations.Add(
					$"{folder}: the readme names [{string.Join(", ", namedProjects)}] as its compilers, and [{string.Join(", ", compiling)}] compile it."
				);
			}

			bool opensDefine = RepositoryModel
				.SourcePathsUnder(folder)
				.Any(path => RepositoryModel.Source(path).Text.Contains($"#if {define}", StringComparison.Ordinal));

			if (define != "none" && !opensDefine)
				violations.Add($"{folder}: no file of the home opens an #if {define} block, which the readme names as its define.");
		}

		foreach (string folder in RepositoryModel.SubfoldersOf("shared").Where(folder => !named.Contains(folder)))
			violations.Add($"{folder}: the readme has no row for this home.");

		foreach (string folder in named.Where(folder => !Directory.Exists(RepositoryPaths.Below(folder.Split('/')))))
			violations.Add($"{folder}: the readme names a home that does not exist.");

		Assert.IsEmpty(
			violations,

			"The shared readme differs from the homes:"
				+ Environment.NewLine
				+ string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal))
		);
	}
}
