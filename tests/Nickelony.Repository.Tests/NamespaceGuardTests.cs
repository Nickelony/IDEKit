using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>Each file declares the namespace that the CONVENTIONS.md rules derive from its location.</summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed class NamespaceGuardTests
{
	// The folders of a flat package whose files declare a second namespace, which is the project name plus the suffix: the Client package's LSP
	// wire model, and the entries of the KeyBindings override document, which no host names.
	private static readonly (string Folder, string Suffix)[] s_secondNamespaceFolders =
	[
		("src/LanguageServer/Nickelony.LanguageServer.Client/Protocol/", ".Protocol"),
		("src/KeyBindings/Nickelony.KeyBindings/Overrides/Entries/", ".OverrideEntries"),
	];

	[TestMethod]
	public void EveryFolderOfSharedSourceIsADeclaredHome()
	{
		List<string> violations = [];

		foreach (
			string folder in RepositoryModel
				.SubfoldersOf("shared")
				.Where(path => path != "shared/docs")
				.Concat(RepositoryModel.SharedTreeFolders.SelectMany(RepositoryModel.SubfoldersOf))
		)
		{
			if (RepositoryModel.SharedHomes.All(home => home.Folder != folder))
			{
				violations.Add(
					$"{folder} is not a declared shared home. Declare it in {nameof(RepositoryModel)}.{nameof(RepositoryModel.SharedHomes)}."
				);
			}
		}

		Assert.IsEmpty(violations, "Shared folders:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void EveryFileDeclaresTheNamespaceItsLocationDerives()
	{
		List<string> violations = [];
		HashSet<string> usedHomes = new(StringComparer.Ordinal);

		foreach (
			string path in RepositoryModel.SourcePathsUnder(
				"src",
				"samples",
				"shared",
				"tests/IDEKit/Shared",
				"tests/QuickPick/Shared",
				"tests/Samples/Shared",
				"tests/IDEKit/TestSupport",
				"tests/KeyBindings/TestSupport",
				"tests/QuickPick/TestSupport",
				"tests/LanguageServer/TestSupport",
				"tests/TestSupport"
			)
		)
		{
			SourceFile source = RepositoryModel.Source(path);

			if (path.StartsWith("src/", StringComparison.Ordinal))
				CheckSourceFile(source, violations);
			else if (path.StartsWith("samples/", StringComparison.Ordinal))
				CheckSampleFile(source, violations);
			else
				CheckSharedFile(source, violations, usedHomes);
		}

		foreach (SharedHome home in RepositoryModel.SharedHomes.Where(home => !usedHomes.Contains(home.Folder)))
		{
			violations.Add(
				$"The shared home '{home.Folder}' holds no source file. Remove it from {nameof(RepositoryModel)}.{nameof(RepositoryModel.SharedHomes)}."
			);
		}

		string message = "Namespaces differ from the CONVENTIONS.md rules:" + Environment.NewLine + string.Join(Environment.NewLine, violations);

		Assert.IsEmpty(violations, message + Environment.NewLine + "Edit the namespace line, or move the file.");
	}

	[TestMethod]
	public void EveryTestFileDeclaresTheNameOfItsProject()
	{
		List<string> violations = [];

		// A shared test tree and a shared TestSupport home declare the flat namespace of their home, which the test above checks.
		string[] sharedTestHomes =
		[
			.. RepositoryModel.SharedHomes.Select(home => home.Folder).Where(folder => folder.StartsWith("tests/", StringComparison.Ordinal)),
		];

		foreach (string path in RepositoryModel.SourcePathsUnder("tests"))
		{
			if (sharedTestHomes.Any(home => path.StartsWith(home + "/", StringComparison.Ordinal)))
				continue;

			ProjectFile? owner = RepositoryModel
				.Projects.Where(project => project.IsTestProject && IsInside(path, project))
				.MaxBy(project => project.RelativeDirectory.Length);

			if (owner is null)
			{
				violations.Add($"{path}: belongs to no test project, so it declares no namespace that a project name could fix.");
				continue;
			}

			foreach (string actual in RepositoryModel.Source(path).Namespaces([.. owner.DefineConstants]))
			{
				if (actual != owner.Name)
					violations.Add($"{path}: declares '{actual}' where '{owner.Name}', the name of its project, is expected.");
			}
		}

		Assert.IsEmpty(
			violations,
			"Test namespaces differ from the CONVENTIONS.md rules:" + Environment.NewLine + string.Join(Environment.NewLine, violations)
		);
	}

	// A sample project declares one flat namespace, its project name, whatever its folders.
	private static void CheckSampleFile(SourceFile source, List<string> violations)
	{
		ProjectFile? owner = RepositoryModel
			.SampleProjects.Where(candidate => IsInside(source.RelativePath, candidate))
			.MaxBy(candidate => candidate.RelativeDirectory.Length);

		if (owner is null)
		{
			violations.Add($"{source.RelativePath}: belongs to no sample project, so it declares no namespace that a project name could fix.");
			return;
		}

		foreach (string actual in source.Namespaces([.. owner.DefineConstants]))
		{
			if (actual != owner.Name)
				violations.Add($"{source.RelativePath}: declares {actual} where {owner.Name}, the name of its project, is expected.");
		}
	}

	private static void CheckSourceFile(SourceFile source, List<string> violations)
	{
		string[] segments = source.RelativePath.Split('/');
		int projectIndex = Array.FindLastIndex(segments[..^1], segment => segment.StartsWith("Nickelony.", StringComparison.Ordinal));

		if (projectIndex < 0)
			return;

		bool flat = RepositoryModel.IsFlatNamespacePath(source.RelativePath);

		string? secondNamespaceSuffix = s_secondNamespaceFolders
			.FirstOrDefault(entry => source.RelativePath.StartsWith(entry.Folder, StringComparison.Ordinal))
			.Suffix;

		bool atProjectRoot = segments.Length - 2 <= projectIndex;

		string project = segments[projectIndex];

		// A folder that holds only internal types adds no namespace: its files declare the namespace of the folder around it.
		string folderPath = atProjectRoot
			? string.Empty
			: string.Join(
				'.',

				Enumerable
					.Range(projectIndex + 1, segments.Length - projectIndex - 2)
					.Where(index => !RepositoryModel.InternalOnlyFolders.Contains(string.Join('/', segments[..(index + 1)]), StringComparer.Ordinal))
					.Select(index => segments[index])
			);

		string expected =
			secondNamespaceSuffix is not null ? project + secondNamespaceSuffix
			: flat || atProjectRoot || folderPath.Length == 0 ? project
			: project + "." + folderPath;

		ProjectFile? owner = RepositoryModel
			.SourceProjects.Where(candidate => IsInside(source.RelativePath, candidate))
			.MaxBy(candidate => candidate.RelativeDirectory.Length);

		string[] symbols = owner is null ? [] : [.. owner.DefineConstants];

		foreach (string actual in source.Namespaces(symbols))
		{
			if (actual != expected)
				violations.Add($"{source.RelativePath}: declares '{actual}' where '{expected}' is expected.");
		}
	}

	private static void CheckSharedFile(SourceFile source, List<string> violations, HashSet<string> usedHomes)
	{
		if (RepositoryModel.SharedHomeOf(source.RelativePath) is not { } home)
		{
			violations.Add($"{source.RelativePath}: sits under no declared shared home, so no namespace rule reads it.");
			return;
		}

		(string folder, SharedHomeKind kind, string suffix, bool folderDerived) = (home.Folder, home.Kind, home.Suffix, home.FolderDerived);
		usedHomes.Add(folder);

		string[] subfolders = Path.GetDirectoryName(source.RelativePath)!
			.Replace('\\', '/')[folder.Length..]
			.Split('/', StringSplitOptions.RemoveEmptyEntries);

		// A folder that holds only internal types adds no namespace, as it does in a project.
		string subfolder = string.Join(
			'.',

			subfolders.Where(
				(_, index) =>
					!RepositoryModel.InternalOnlyFolders.Contains(folder + "/" + string.Join('/', subfolders[..(index + 1)]), StringComparer.Ordinal)
			)
		);

		// An internal helper below an editor home keeps the flat namespace, and no other shared file may declare it.
		bool isFlatHelper =
			kind != SharedHomeKind.Neutral
			&& RepositoryModel.InternalHelperFolders.Any(helper => source.RelativePath.StartsWith(helper + "/", StringComparison.Ordinal));

		(string? symbol, string defined, string undefined) = Variants(kind, suffix);

		foreach ((bool isDefined, string root) in new[] { (true, defined), (false, undefined) })
		{
			// The namespace of the quick-pick family is flat, so the folder of a primitive names a slice only in the Core variant.
			bool addsSubfolder = folderDerived && subfolder.Length > 0 && !(kind == SharedHomeKind.Primitive && isDefined);

			string expected =
				isFlatHelper ? RepositoryModel.FlatInfrastructureNamespace
				: addsSubfolder ? root + "." + subfolder
				: root;

			string[] symbols = isDefined && symbol is not null ? [symbol] : [];

			foreach (string actual in source.Namespaces(symbols))
			{
				if (actual != expected)
				{
					violations.Add(
						$"{source.RelativePath}: declares '{actual}' where '{expected}' is expected (symbol {symbol ?? "none"} {(isDefined ? "set" : "unset")})."
					);
				}
			}

			if (symbol is null)
				break;
		}
	}

	private static (string? Symbol, string Defined, string Undefined) Variants(SharedHomeKind kind, string suffix)
	{
		return kind switch
		{
			SharedHomeKind.Editor => ("AVALONIAEDIT", "Nickelony.IDEKit.AvaloniaEdit" + suffix, "Nickelony.IDEKit.AvalonEdit" + suffix),
			SharedHomeKind.KeyBindings => ("AVALONIAKEYBINDINGS", "Nickelony.KeyBindings.Avalonia" + suffix, "Nickelony.KeyBindings.Wpf" + suffix),
			SharedHomeKind.QuickPick => ("AVALONIAQUICKPICK", "Nickelony.QuickPick.Avalonia" + suffix, "Nickelony.QuickPick.Wpf" + suffix),
			SharedHomeKind.Sample => ("AVALONIAEDIT", "Nickelony.Samples.Avalonia" + suffix, "Nickelony.Samples.Wpf" + suffix),
			SharedHomeKind.Primitive => ("QUICKPICKFAMILY", "Nickelony.QuickPick" + suffix, "Nickelony.IDEKit.Core" + suffix),
			_ => (null, suffix, suffix),
		};
	}

	private static bool IsInside(string relativePath, ProjectFile project) =>
		relativePath.StartsWith(project.RelativeDirectory + "/", StringComparison.Ordinal);

	[TestMethod]
	public void TheGlobalUsingsOfAProductProjectNameNoNamespaceOfItsOwnPackage()
	{
		List<string> violations = [];

		foreach (ProjectFile project in RepositoryModel.SourceProjects)
		{
			foreach (string ns in GlobalUsingNamespaces(project))
			{
				// A project whose name extends this one owns the namespaces below its own name.
				bool belongsToLongerPackage = RepositoryModel.SourceProjects.Any(other =>
					other.Name.Length > project.Name.Length && (ns == other.Name || ns.StartsWith(other.Name + ".", StringComparison.Ordinal))
				);

				if (!belongsToLongerPackage && (ns == project.Name || ns.StartsWith(project.Name + ".", StringComparison.Ordinal)))
				{
					violations.Add(
						$"{project.RelativeDirectory}/GlobalUsings.cs imports {ns}, a namespace of its own package. Import it in the files that reach it."
					);
				}
			}
		}

		Assert.IsEmpty(
			violations,
			"Global usings:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal))
		);
	}

	[TestMethod]
	public void TheGlobalUsingsOfAProductProjectNameOnlyLoggingAndReferencedPackageNamespaces()
	{
		List<string> violations = [];
		int read = 0;

		foreach (ProjectFile project in RepositoryModel.SourceProjects)
		{
			HashSet<string> referenced = new(
				project
					.ProjectReferences.Where(RepositoryModel.HasProject)
					.SelectMany(name => RepositoryModel.DeclaredNamespaces(RepositoryModel.Project(name))),

				StringComparer.Ordinal
			);

			foreach (string ns in GlobalUsingNamespaces(project))
			{
				read++;

				bool logging = ns == "Microsoft.Extensions.Logging" || ns.StartsWith("Microsoft.Extensions.Logging.", StringComparison.Ordinal);

				if (!logging && !referenced.Contains(ns))
				{
					violations.Add(
						$"{project.RelativeDirectory}/GlobalUsings.cs imports {ns}, which is neither a logging namespace nor a namespace of a package the project references. Import it in the files that reach it."
					);
				}
			}
		}

		Assert.IsGreaterThan(0, read, "The guard read no global using, so it checks nothing.");

		Assert.IsEmpty(
			violations,
			"Global usings:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal))
		);
	}

	/// <summary>The namespaces that the <c>GlobalUsings.cs</c> of a project imports, leaving out a static import and an alias.</summary>
	private static IEnumerable<string> GlobalUsingNamespaces(ProjectFile project)
	{
		string file = Path.Combine(RepositoryPaths.Root(), project.RelativeDirectory.Replace('/', Path.DirectorySeparatorChar), "GlobalUsings.cs");

		if (!File.Exists(file))
			yield break;

		foreach (string line in File.ReadLines(file))
		{
			string trimmed = line.Trim();

			if (
				!trimmed.StartsWith("global using ", StringComparison.Ordinal)
				|| trimmed.StartsWith("global using static ", StringComparison.Ordinal)
				|| trimmed.Contains('=', StringComparison.Ordinal)
			)
			{
				continue;
			}

			yield return trimmed["global using ".Length..].TrimEnd(';').Trim();
		}
	}
}
