using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nickelony.Repository.Tests;

/// <summary>Reads the repository's project and source files once per run and answers structural questions about them.</summary>
internal static class RepositoryModel
{
	private static readonly string[] s_skippedFolders = [".git", ".vs", "bin", "obj", "node_modules"];

	private static readonly Lazy<IReadOnlyList<ProjectFile>> s_projects = new(LoadProjects);

	private static readonly Dictionary<string, SourceFile> s_sources = new(StringComparer.Ordinal);

	/// <summary>The provider framework, which every language package builds on.</summary>
	internal const string ProviderPackage = "Nickelony.LanguageServer.Provider";

	/// <summary>The one flat namespace that every file of an internal-helper folder declares.</summary>
	internal const string FlatInfrastructureNamespace = "Nickelony.Infrastructure";

	/// <summary>The folder below which each subfolder is one shared test tree of the editor bindings.</summary>
	internal const string SharedTreesFolder = "tests/IDEKit/Shared";

	/// <summary>The folder below which each subfolder is one shared test tree of the quick-pick toolkit packages.</summary>
	internal const string QuickPickSharedTreesFolder = "tests/QuickPick/Shared";

	/// <summary>The folder below which each subfolder is one shared test tree of the sample heads.</summary>
	internal const string SamplesSharedTreesFolder = "tests/Samples/Shared";

	/// <summary>Every folder below which each subfolder is one shared test tree.</summary>
	internal static IReadOnlyList<string> SharedTreeFolders { get; } = [SharedTreesFolder, QuickPickSharedTreesFolder, SamplesSharedTreesFolder];

	/// <summary>
	/// Every folder of source that more than one project compiles: the subfolders of <c>shared/</c> except <c>docs</c>, the shared test trees,
	/// and the <c>TestSupport</c> folders. The guards and the reports read this one list.
	/// </summary>
	internal static IReadOnlyList<SharedHome> SharedHomes { get; } =
	[
		new(Folder: "shared/Editor", Kind: SharedHomeKind.Editor, Suffix: "", FolderDerived: true),
		new(Folder: "shared/Editor.LanguageFeatures", Kind: SharedHomeKind.Editor, Suffix: ".LanguageFeatures", FolderDerived: true),
		new(Folder: "shared/Editor.Markdown", Kind: SharedHomeKind.Editor, Suffix: ".Markdown", FolderDerived: true),
		new(Folder: "shared/Editor.TextMate", Kind: SharedHomeKind.Editor, Suffix: ".TextMate", FolderDerived: true),

		new(
			Folder: "shared/Infrastructure",
			Kind: SharedHomeKind.Neutral,
			Suffix: FlatInfrastructureNamespace,
			FolderDerived: false,
			InternalHelpers: true
		),

		new(Folder: "shared/KeyBindings", Kind: SharedHomeKind.KeyBindings, Suffix: "", FolderDerived: true),
		new(Folder: "shared/Primitives", Kind: SharedHomeKind.Primitive, Suffix: "", FolderDerived: true, SelectsVisibility: true),
		new(Folder: "shared/QuickPick", Kind: SharedHomeKind.QuickPick, Suffix: "", FolderDerived: true),
		new(Folder: "shared/Samples", Kind: SharedHomeKind.Neutral, Suffix: "Nickelony.Samples", FolderDerived: false),
		new(Folder: $"{SharedTreesFolder}/Editor.Tests", Kind: SharedHomeKind.Editor, Suffix: ".Tests", FolderDerived: false),

		new(
			Folder: $"{SharedTreesFolder}/Editor.LanguageFeatures.Tests",
			Kind: SharedHomeKind.Editor,
			Suffix: ".LanguageFeatures.Tests",
			FolderDerived: false
		),

		new(Folder: $"{SharedTreesFolder}/Editor.Markdown.Tests", Kind: SharedHomeKind.Editor, Suffix: ".Markdown.Tests", FolderDerived: false),
		new(Folder: $"{SharedTreesFolder}/Editor.TextMate.Tests", Kind: SharedHomeKind.Editor, Suffix: ".TextMate.Tests", FolderDerived: false),
		new(Folder: $"{QuickPickSharedTreesFolder}/Presenter.Tests", Kind: SharedHomeKind.QuickPick, Suffix: ".Tests", FolderDerived: false),
		new(Folder: $"{SamplesSharedTreesFolder}/Samples.Tests", Kind: SharedHomeKind.Sample, Suffix: ".Tests", FolderDerived: false),
		new(Folder: "tests/IDEKit/TestSupport", Kind: SharedHomeKind.Neutral, Suffix: "Nickelony.IDEKit.Testing", FolderDerived: false),
		new(Folder: "tests/KeyBindings/TestSupport", Kind: SharedHomeKind.Neutral, Suffix: "Nickelony.KeyBindings.Testing", FolderDerived: false),
		new(Folder: "tests/QuickPick/TestSupport", Kind: SharedHomeKind.Neutral, Suffix: "Nickelony.QuickPick.Testing", FolderDerived: false),

		new(
			Folder: "tests/LanguageServer/TestSupport",
			Kind: SharedHomeKind.Neutral,
			Suffix: "Nickelony.LanguageServer.Testing",
			FolderDerived: false
		),

		new(Folder: "tests/TestSupport", Kind: SharedHomeKind.Neutral, Suffix: "Nickelony.Testing", FolderDerived: false),
	];

	/// <summary>
	/// The folders of <c>shared/</c> that hold internal helpers: source that several assemblies compile and no host sees, so each file declares
	/// internal types only. Every file in these folders declares the flat <c>Nickelony.Infrastructure</c> namespace.
	/// </summary>
	internal static IEnumerable<string> InternalHelperFolders => SharedHomes.Where(home => home.InternalHelpers).Select(home => home.Folder);

	/// <summary>
	/// The IDEKit folders and the folders of a shared editor home, as repository paths, whose files declare internal types only and so keep
	/// the namespace of their parent folder. A folder joins this list in the change that makes all its types internal.
	/// </summary>
	internal static IReadOnlyList<string> InternalOnlyFolders { get; } =
	[
		"shared/Editor.LanguageFeatures/Presentation",
		"src/IDEKit/AvalonEdit/Nickelony.IDEKit.AvalonEdit.LanguageFeatures/Presentation",
		"src/IDEKit/AvaloniaEdit/Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures/Presentation",
		"src/IDEKit/Nickelony.IDEKit.Core/Bookmarks/Sidecar",
		"src/IDEKit/Nickelony.IDEKit.Workspace/Documents/Store",
		"src/IDEKit/Nickelony.IDEKit.Workspace/FileSystem/Local",
		"src/IDEKit/Nickelony.IDEKit.Workspace.Views/Collaborators",
		"src/IDEKit/Nickelony.IDEKit.Workspace.Views/Coordination/Collaborators",
	];

	/// <summary>The shared home that holds the file, or <see langword="null"/> when the file sits under no declared home.</summary>
	internal static SharedHome? SharedHomeOf(string relativePath) =>
		SharedHomes.FirstOrDefault(home => relativePath.StartsWith(home.Folder + "/", StringComparison.Ordinal));

	/// <summary>Every project file below <c>src</c>, <c>samples</c> and <c>tests</c>.</summary>
	internal static IReadOnlyList<ProjectFile> Projects => s_projects.Value;

	/// <summary>The library projects below <c>src</c>.</summary>
	internal static IEnumerable<ProjectFile> SourceProjects => Projects.Where(project => !project.IsTestProject && !project.IsSampleProject);

	/// <summary>
	/// The sample applications below <c>samples</c>, which are no library and no test project, so the tier and package guards never see them.
	/// </summary>
	internal static IEnumerable<ProjectFile> SampleProjects => Projects.Where(project => project.IsSampleProject);

	/// <summary>
	/// The language packages: the libraries that reference the provider framework, so a new language needs no edit of a guard.
	/// The provider itself is not one.
	/// </summary>
	internal static IEnumerable<ProjectFile> LanguagePackages =>
		SourceProjects.Where(project => project.ProjectReferences.Contains(ProviderPackage, StringComparer.Ordinal));

	/// <summary>
	/// The libraries that declare one flat namespace per package: the LanguageServer, KeyBindings, QuickPick and Matching families.
	/// </summary>
	internal static IEnumerable<ProjectFile> FlatNamespacePackages => SourceProjects.Where(project => IsFlatNamespacePath(project.RelativePath));

	/// <summary>Reports whether a repository path lies in a family whose packages declare one flat namespace each.</summary>
	internal static bool IsFlatNamespacePath(string relativePath)
	{
		return relativePath.StartsWith("src/LanguageServer/", StringComparison.Ordinal)
			|| relativePath.StartsWith("src/KeyBindings/", StringComparison.Ordinal)
			|| relativePath.StartsWith("src/QuickPick/", StringComparison.Ordinal)
			|| relativePath.StartsWith("src/Matching/", StringComparison.Ordinal);
	}

	/// <summary>The namespaces that the sources of a project declare, under the project's own symbols.</summary>
	internal static IReadOnlyList<string> DeclaredNamespaces(ProjectFile project)
	{
		HashSet<string> declared = new(StringComparer.Ordinal);

		foreach (string path in project.Sources)
		{
			foreach (string name in Source(path).Namespaces([.. project.DefineConstants]))
				declared.Add(name);
		}

		return [.. declared.Order(StringComparer.Ordinal)];
	}

	internal static ProjectFile Project(string name)
	{
		return Projects.FirstOrDefault(project => project.Name == name)
			?? throw new InvalidOperationException($"The repository holds no project named '{name}'.");
	}

	internal static bool HasProject(string name) => Projects.Any(project => project.Name == name);

	/// <summary>Reads a source file once and returns the cached instance afterwards.</summary>
	internal static SourceFile Source(string relativePath)
	{
		lock (s_sources)
		{
			if (!s_sources.TryGetValue(relativePath, out SourceFile? source))
			{
				source = new SourceFile(relativePath, File.ReadAllText(Path.Combine(RepositoryPaths.Root(), relativePath)));
				s_sources[relativePath] = source;
			}

			return source;
		}
	}

	/// <summary>The C# files below the given repository folders, sorted, as paths below the repository root.</summary>
	internal static IReadOnlyList<string> SourcePathsUnder(params string[] folders)
	{
		string root = RepositoryPaths.Root();

		return
		[
			.. folders
				.Where(folder => Directory.Exists(Path.Combine(root, folder)))
				.SelectMany(folder => EnumerateFiles(Path.Combine(root, folder), "*.cs"))
				.Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
				.OrderBy(path => path, StringComparer.Ordinal),
		];
	}

	/// <summary>The direct subfolders of a repository folder, as paths below the repository root, or none when the folder is absent.</summary>
	internal static IEnumerable<string> SubfoldersOf(string folder)
	{
		string directory = RepositoryPaths.Below(folder.Split('/'));

		if (!Directory.Exists(directory))
			return [];

		return Directory.EnumerateDirectories(directory).Select(subfolder => folder + "/" + Path.GetFileName(subfolder));
	}

	/// <summary>Enumerates files below a folder, leaving out build output, version control and scratch folders.</summary>
	internal static IEnumerable<string> EnumerateFiles(string directory, string pattern)
	{
		Stack<string> pending = new();
		pending.Push(directory);

		while (pending.Count > 0)
		{
			string current = pending.Pop();

			foreach (string child in Directory.EnumerateDirectories(current))
			{
				if (!s_skippedFolders.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
					pending.Push(child);
			}

			foreach (string file in Directory.EnumerateFiles(current, pattern))
				yield return file;
		}
	}

	private static IReadOnlyList<ProjectFile> LoadProjects()
	{
		string root = RepositoryPaths.Root();

		return
		[
			.. new[] { "src", "samples", "tests" }
				.Where(folder => Directory.Exists(Path.Combine(root, folder)))
				.SelectMany(folder => EnumerateFiles(Path.Combine(root, folder), "*.csproj"))
				.OrderBy(path => path, StringComparer.Ordinal)
				.Select(ProjectFile.Load),
		];
	}
}
