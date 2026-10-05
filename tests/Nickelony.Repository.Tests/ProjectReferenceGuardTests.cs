using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// A project reference points down the tiers, so a lower package never takes a dependency on a higher one, and the package lists
/// of <c>Directory.Build.targets</c> and the names a library sets agree with the project set that the reference rules read.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed partial class ProjectReferenceGuardTests
{
	private const string WorkspacePackage = "Nickelony.IDEKit.Workspace";
	private const string ViewsPackage = "Nickelony.IDEKit.Workspace.Views";

	private enum Engine
	{
		None,
		Wpf,
		Avalonia,
	}

	private const string Guard = nameof(ProjectReferenceGuardTests);
	private const string Prefix = "Nickelony.";
	private const string EnginePlaceholder = "{Engine}";

	// A namespace that holds a public type and that no row of the import map names, because no host imports it.
	private static readonly AllowList s_unimportedNamespaces = new("unimported namespaces");

	private static readonly string[] s_engines = ["AvalonEdit", "AvaloniaEdit"];

	// The one table of allowed references: each library, with the libraries it may reference. A name is written below the Nickelony. prefix,
	// and {Engine} stands for each editor engine in turn. A reference outside a library's row fails, so a new reference is a decision made here.
	private static readonly (string Library, string[] MayReference)[] s_allowedReferences =
	[
		("IDEKit.Core", []),
		("IDEKit.LanguageFeatures", ["IDEKit.Core"]),
		("IDEKit.LanguageFeatures.Controllers", ["IDEKit.Core", "IDEKit.LanguageFeatures", "Matching", "QuickPick"]),
		("IDEKit.Markdown", ["IDEKit.Core"]),
		("IDEKit.TextMate", ["IDEKit.Core"]),
		("IDEKit.Workspace", ["IDEKit.Core"]),
		("IDEKit.Workspace.QuickPick", ["IDEKit.Core", "IDEKit.FileWatching", "QuickPick"]),
		("IDEKit.Workspace.Views", ["IDEKit.Core", "IDEKit.LanguageFeatures", "IDEKit.Workspace"]),
		("IDEKit.Processes", []),
		("IDEKit.FileWatching", ["IDEKit.Core"]),
		("IDEKit.JsonSchema", []),
		("IDEKit.{Engine}", ["IDEKit.Core"]),
		("IDEKit.{Engine}.LanguageFeatures", ["IDEKit.Core", "IDEKit.{Engine}", "IDEKit.LanguageFeatures", "IDEKit.LanguageFeatures.Controllers"]),
		("IDEKit.{Engine}.Markdown", ["IDEKit.Core", "IDEKit.{Engine}", "IDEKit.Markdown"]),
		("IDEKit.{Engine}.TextMate", ["IDEKit.Core", "IDEKit.{Engine}", "IDEKit.TextMate"]),
		("KeyBindings", []),
		("KeyBindings.Wpf", ["KeyBindings"]),
		("KeyBindings.Avalonia", ["KeyBindings"]),
		("Matching", []),
		("QuickPick", ["Matching"]),
		("QuickPick.Wpf", ["QuickPick", "Matching"]),
		("QuickPick.Avalonia", ["QuickPick", "Matching"]),
		("LanguageServer.Provider.Abstractions", ["IDEKit.Core", "IDEKit.LanguageFeatures"]),
		("LanguageServer.Client", []),

		(
			"LanguageServer.Provider",
			["LanguageServer.Provider.Abstractions", "LanguageServer.Client", "IDEKit.Core", "IDEKit.FileWatching", "IDEKit.LanguageFeatures"]
		),
	];

	// A language package is a library that references the provider framework, so it has no row of its own.
	private static readonly string[] s_languagePackageMayReference =
	[
		"LanguageServer.Provider.Abstractions",
		"LanguageServer.Client",
		"LanguageServer.Provider",
		"IDEKit.Core",
		"IDEKit.FileWatching",
		"IDEKit.LanguageFeatures",
	];

	// Two families of projects that never reference each other, each with the project-name prefix that selects its members.
	private static readonly (string First, string Second, string Reason)[] s_unrelatedEngineFamilies =
	[
		("Nickelony.IDEKit.AvalonEdit", "Nickelony.IDEKit.AvaloniaEdit", "an editor binding takes one engine, never both"),
		("Nickelony.KeyBindings.Wpf", "Nickelony.KeyBindings.Avalonia", "a key-binding adapter takes one toolkit, never both"),
		("Nickelony.QuickPick.Wpf", "Nickelony.QuickPick.Avalonia", "a quick-pick window takes one toolkit, never both"),
	];

	// The property of Directory.Build.targets that lists the libraries of each engine.
	private static readonly (string Property, Engine Engine)[] s_packageLists =
	[
		("HostAgnosticPackageNames", Engine.None),
		("WpfTypedPackageNames", Engine.Wpf),
		("AvaloniaTypedPackageNames", Engine.Avalonia),
	];

	[TestMethod]
	public void EveryReferenceIsInTheAllowedTable()
	{
		List<string> violations = [];

		foreach ((string library, string[] mayReference) in ExpandedRows())
		{
			foreach (string name in mayReference.Prepend(library).Where(name => !RepositoryModel.HasProject(name)).Distinct(StringComparer.Ordinal))
				violations.Add($"The allowed-reference row of '{library}' names '{name}', which is no project. Fix or delete the row.");
		}

		foreach (ProjectFile project in RepositoryModel.SourceProjects)
		{
			string[]? allowed = AllowedReferences(project);

			if (allowed is null)
			{
				violations.Add(
					$"{project.RelativePath}: this library has no row in the allowed references and references no provider framework. Add its row to {nameof(ProjectReferenceGuardTests)}."
				);

				continue;
			}

			foreach (string reference in project.ProjectReferences.Except(allowed, StringComparer.Ordinal))
			{
				violations.Add(
					$"{project.RelativePath}: references '{reference}', which its row does not allow. It may reference: {(allowed.Length == 0 ? "nothing" : string.Join(", ", allowed))}."
				);
			}
		}

		string message = "Project references leave the allowed table:" + Environment.NewLine + string.Join(Environment.NewLine, violations);

		Assert.IsEmpty(
			violations,
			message + Environment.NewLine + "A reference points only down; remove it or move the type it reaches into the lower package."
		);
	}

	[TestMethod]
	public void TheCommandsOnlyHostReferencesOnlyTheQuickPickAndKeyBindingsFamilies()
	{
		ProjectFile host = RepositoryModel.Project("Nickelony.QuickPick.CommandsOnly.Tests");

		string[] outsiders =
		[
			.. host.ProjectReferences.Where(reference =>
				reference != "Nickelony.Matching"
				&& !reference.StartsWith("Nickelony.QuickPick", StringComparison.Ordinal)
				&& !reference.StartsWith("Nickelony.KeyBindings", StringComparison.Ordinal)
			),
		];

		Assert.IsNotEmpty(host.ProjectReferences, "The commands-only host references no project, so it exercises nothing.");

		Assert.Contains(
			reference => reference == "Nickelony.KeyBindings",
			host.ProjectReferences,
			$"{host.RelativePath} proves the command palette recipe of the guides, which builds the items from a command catalog, so it references the key bindings."
		);

		Assert.IsEmpty(
			outsiders,

			$"{host.RelativePath} proves that a host takes the quick pick and the key bindings without IDEKit, so it may reference only the quick-pick family, the key-binding family and the matcher, and it references: "
				+ string.Join(", ", outsiders)
		);
	}

	[TestMethod]
	public void TheReadmeDiagramListsTheReferencesOfEveryLibrary()
	{
		string[] lines = ReadFence(File.ReadAllText(RepositoryPaths.Below("README.md")), "### Dependency diagram");
		Dictionary<string, string[]> diagram = new(StringComparer.Ordinal);

		foreach (string line in lines)
		{
			Match row = DiagramRow().Match(line);

			if (!row.Success)
				continue;

			string text = row.Groups["rest"].Value.Trim();

			string[] tokens = text.StartsWith("->", StringComparison.Ordinal)
				? [.. text[2..].Split(',', StringSplitOptions.TrimEntries).Select(item => DiagramToken().Match(item).Value)]
				: [];

			foreach (string engine in row.Groups["name"].Value.Contains("<Engine>", StringComparison.Ordinal) ? s_engines : [string.Empty])
			{
				string name = row.Groups["name"].Value.Replace("<Engine>", engine, StringComparison.Ordinal);
				diagram[name] = [.. tokens.Select(token => token.Replace("<Engine>", engine, StringComparison.Ordinal))];
			}
		}

		List<string> violations = [];

		foreach (ProjectFile project in RepositoryModel.SourceProjects)
		{
			string name = project.Name[Prefix.Length..];

			if (name.StartsWith("LanguageServer.", StringComparison.Ordinal))
				name = name["LanguageServer.".Length..];

			if (!diagram.TryGetValue(name, out string[]? listed))
			{
				violations.Add($"The README diagram has no line for '{project.Name}'.");
				continue;
			}

			HashSet<string> declared = [.. listed.Select(ResolveDiagramName).OfType<string>()];
			HashSet<string> actual = new(project.ProjectReferences, StringComparer.Ordinal);

			if (!declared.SetEquals(actual))
			{
				violations.Add(
					$"{project.Name}: the README diagram lists [{string.Join(", ", declared.Order(StringComparer.Ordinal))}], and the project file references [{string.Join(", ", actual.Order(StringComparer.Ordinal))}]."
				);
			}
		}

		Assert.IsEmpty(
			violations,
			"The README dependency diagram differs from the project files:" + Environment.NewLine + string.Join(Environment.NewLine, violations)
		);
	}

	[TestMethod]
	public void TheImportMapNamesRealPackagesAndNamespaces()
	{
		string text = File.ReadAllText(RepositoryPaths.Below("docs", "ConsumerIntegration.md")).ReplaceLineEndings("\n");
		List<string> violations = [];

		string[] rows =
		[
			.. text[text.IndexOf("## Packages and imports by feature", StringComparison.Ordinal)..]
				.Split('\n')
				.Skip(1)
				.TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal))
				.Where(line => line.StartsWith("| ", StringComparison.Ordinal) && !line.StartsWith("| ---", StringComparison.Ordinal))
				.Skip(1),
		];

		Assert.IsNotEmpty(rows, "docs/ConsumerIntegration.md holds no import map, so this guard checks nothing.");

		HashSet<string> declaredNamespaces = [.. RepositoryModel.SourceProjects.SelectMany(RepositoryModel.DeclaredNamespaces)];
		HashSet<string> mapped = new(StringComparer.Ordinal);

		foreach (string row in rows)
		{
			string[] cells = row.Split('|', StringSplitOptions.TrimEntries);

			foreach (string package in ExpandEngines(BacktickTokens(cells[2])))
			{
				mapped.Add(package);

				if (!RepositoryModel.HasProject(package))
					violations.Add($"The import map names the package '{package}', which is no project.");
			}

			foreach (string name in ExpandEngines(BacktickTokens(cells[3])).Where(name => !declaredNamespaces.Contains(name)))
				violations.Add($"The import map names the namespace '{name}', which no library declares.");
		}

		foreach (ProjectFile library in RepositoryModel.SourceProjects.Where(library => !mapped.Contains(library.Name)))
			violations.Add($"The import map lists no row for the library '{library.Name}'.");

		Assert.IsEmpty(
			violations,
			"docs/ConsumerIntegration.md differs from the repository:" + Environment.NewLine + string.Join(Environment.NewLine, violations)
		);
	}

	[TestMethod]
	public void TheImportMapNamesEveryNamespaceThatHoldsAPublicType()
	{
		string text = File.ReadAllText(RepositoryPaths.Below("docs", "ConsumerIntegration.md")).ReplaceLineEndings("\n");
		string map = text[text.IndexOf("## Packages and imports by feature", StringComparison.Ordinal)..];

		map = map[..map.IndexOf("\n## ", 1, StringComparison.Ordinal)];

		HashSet<string> mapped = new(
			map.Split('\n')
				.Where(line => line.StartsWith("| ", StringComparison.Ordinal))
				.SelectMany(line => ExpandEngines(BacktickTokens(line.Split('|', StringSplitOptions.TrimEntries)[3]))),

			StringComparer.Ordinal
		);

		List<string> violations = [];

		foreach (string ns in PublicTypeNamespaces().Where(ns => !mapped.Contains(ns)))
		{
			if (!s_unimportedNamespaces.Allows(ns))
			{
				violations.Add(
					s_unimportedNamespaces.Rejection(
						$"{ns} holds a public type, and no row of the import map in docs/ConsumerIntegration.md names it. Add it to the row of its feature, or list it with the reason no host imports it.",
						Guard
					)
				);
			}
		}

		violations.AddRange(s_unimportedNamespaces.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Namespaces missing from the import map:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void OnlyTheViewsPackageMayReferenceTheWorkspacePackage()
	{
		List<string> violations =
		[
			.. RepositoryModel
				.SourceProjects.Where(project =>
					project.Name != ViewsPackage && project.ProjectReferences.Contains(WorkspacePackage, StringComparer.Ordinal)
				)
				.Select(project => $"{project.RelativePath}: references {WorkspacePackage}, and only {ViewsPackage} may."),
		];

		Assert.IsEmpty(
			violations,

			"Workspace references:"
				+ Environment.NewLine
				+ string.Join(Environment.NewLine, violations)
				+ Environment.NewLine
				+ "An editor binding takes no document authority, so the code that connects an editor to the workspace store is host code."
		);
	}

	[TestMethod]
	public void TheBuildHoldsATargetThatFailsAWorkspaceReference()
	{
		// The target fails the build of a package that references the workspace package, so a reference the project files hide
		// from the guard above (a conditional item, a package reference) still stops the build.
		string targets = File.ReadAllText(RepositoryPaths.Below("Directory.Build.targets"));

		string message =
			"Directory.Build.targets defines no GuardNoWorkspacePackageReferences target, which keeps every package except Workspace.Views from referencing Workspace.";

		Assert.Contains("Name=\"GuardNoWorkspacePackageReferences\"", targets, message);
	}

	[TestMethod]
	public void NoProjectReferencesAProjectOfAnUnrelatedFamily()
	{
		List<string> violations = [];

		foreach (ProjectFile project in RepositoryModel.Projects)
		{
			foreach ((string first, string second, string reason) in UnrelatedFamilies())
			{
				foreach (string reference in project.ProjectReferences)
				{
					if (
						(IsFamily(project.Name, first) && IsFamily(reference, second))
						|| (IsFamily(project.Name, second) && IsFamily(reference, first))
					)
					{
						violations.Add(
							$"{project.RelativePath}: references '{reference}', yet {reason}. Share the code through a package both reference, or a linked source file."
						);
					}
				}
			}
		}

		Assert.IsEmpty(violations, "References between unrelated families:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void TheBuildPackageListsNameEveryLibraryOnTheListOfItsEngine()
	{
		var targets = XDocument.Load(RepositoryPaths.Below("Directory.Build.targets"));
		IReadOnlyList<ProjectFile> libraries = [.. RepositoryModel.SourceProjects];

		List<string> violations = [];

		foreach ((string property, Engine engine) in s_packageLists)
		{
			string[] listed = [.. ReadList(targets, property)];
			string[] expected = [.. libraries.Where(library => EngineOf(library) == engine).Select(library => library.Name)];

			foreach (string name in expected.Except(listed, StringComparer.Ordinal).Order(StringComparer.Ordinal))
			{
				violations.Add(
					$"Directory.Build.targets: {property} does not list the library '{name}'. Add it, because its engine puts it on this list."
				);
			}

			foreach (string name in listed.Except(expected, StringComparer.Ordinal).Order(StringComparer.Ordinal))
			{
				violations.Add(
					$"Directory.Build.targets: {property} lists '{name}', which is not a library of this engine under src. Remove the entry, or move it to the list of its engine."
				);
			}

			foreach (string name in listed.GroupBy(name => name, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key))
				violations.Add($"Directory.Build.targets: {property} lists '{name}' more than once.");
		}

		Assert.IsEmpty(
			violations,

			"The package lists of Directory.Build.targets differ from the libraries:"
				+ Environment.NewLine
				+ string.Join(Environment.NewLine, violations)
				+ Environment.NewLine
				+ "The host-agnostic guard of the build reads these lists, so a library that is on none of them is not guarded."
		);
	}

	[TestMethod]
	public void ALibraryNamesItsNamespaceAssemblyAndPackageAfterItsProjectFile()
	{
		List<string> violations = [];

		foreach (ProjectFile project in RepositoryModel.SourceProjects)
		{
			var document = XDocument.Load(RepositoryPaths.Below(project.RelativePath.Split('/')));

			foreach (string property in new[] { "RootNamespace", "AssemblyName", "PackageId" })
			{
				foreach (XElement element in document.Descendants(property))
				{
					string value = element.Value.Trim();

					if (value.Replace("$(MSBuildProjectName)", project.Name, StringComparison.Ordinal) != project.Name)
					{
						violations.Add(
							$"{project.RelativePath}: sets {property} to '{value}'. The namespace, internals and package-list guards read the project file name '{project.Name}', so the two must agree."
						);
					}
				}
			}
		}

		Assert.IsEmpty(violations, "Project names:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void AnEditorBindingProjectStatesItsEngineOnceAndTheNameAgrees()
	{
		List<string> violations = [];

		foreach (ProjectFile project in RepositoryModel.Projects)
		{
			string? expected =
				project.Name.Contains("AvaloniaEdit", StringComparison.Ordinal)
				|| project.Name.StartsWith("Nickelony.Samples.Avalonia", StringComparison.Ordinal)
					? "Avalonia"
				: project.Name.Contains("AvalonEdit", StringComparison.Ordinal)
				|| project.Name.StartsWith("Nickelony.Samples.Wpf", StringComparison.Ordinal)
					? "Wpf"
				: null;

			var document = XDocument.Load(RepositoryPaths.Below(project.RelativePath.Split('/')));
			XElement[] engineElements = [.. document.Descendants("EditorEngine")];

			if (
				engineElements.Length != (expected is null ? 0 : 1)
				|| engineElements.Any(element => element.Attribute("Condition") is not null || element.Parent?.Attribute("Condition") is not null)
			)
			{
				violations.Add(
					$"{project.RelativePath}: has {engineElements.Length} EditorEngine element(s), some conditional. An editor binding states EditorEngine exactly once and without a condition, and no other project states it."
				);
			}

			if (
				expected == "Avalonia"
				&& project.IsTestProject
				&& !document.Descendants("IsTestProject").Any(element => element.Value.Trim() == "true")
			)
			{
				violations.Add(
					$"{project.RelativePath}: is an Avalonia test project that does not set IsTestProject itself, so Directory.Build.targets cannot add the Avalonia test packages on a clean restore."
				);
			}

			if (project.EditorEngine != expected)
			{
				violations.Add(
					$"{project.RelativePath}: states EditorEngine '{project.EditorEngine ?? "none"}' where its name puts it on '{expected ?? "none"}'. State <EditorEngine>Wpf</EditorEngine> or <EditorEngine>Avalonia</EditorEngine> once, because Directory.Build.targets derives the define and the Avalonia test packages from it."
				);
			}

			if (document.Descendants("DefineConstants").Any(item => item.Value.Contains("AVALONIAEDIT", StringComparison.Ordinal)))
				violations.Add($"{project.RelativePath}: defines AVALONIAEDIT itself. State <EditorEngine>Avalonia</EditorEngine> instead.");
		}

		Assert.IsEmpty(violations, "Editor engines:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void TheBuildFileDerivesTheEditorEngineGroups()
	{
		string targets = File.ReadAllText(RepositoryPaths.Below("Directory.Build.targets"));

		foreach (string required in new[] { "'$(EditorEngine)' == 'Avalonia'", "AVALONIAEDIT", "Avalonia.Headless" })
		{
			Assert.Contains(
				required,
				targets,
				$"Directory.Build.targets no longer holds '{required}', which the project model of the guards mirrors."
			);
		}
	}

	/// <summary>Reports whether a project is the package that a prefix names or one of its sub-packages or test projects.</summary>
	private static bool IsFamily(string projectName, string prefix) =>
		projectName == prefix || projectName.StartsWith(prefix + ".", StringComparison.Ordinal);

	/// <summary>
	/// The engine of a library: an editor binding states it with <c>EditorEngine</c>, the key-binding adapters spell it in their names,
	/// and every other library is host-agnostic.
	/// </summary>
	private static Engine EngineOf(ProjectFile project)
	{
		switch (project.EditorEngine)
		{
			case "Avalonia":
				return Engine.Avalonia;

			case "Wpf":
				return Engine.Wpf;
		}

		if (project.Name.EndsWith(".Avalonia", StringComparison.Ordinal))
			return Engine.Avalonia;

		return project.Name.EndsWith(".Wpf", StringComparison.Ordinal) ? Engine.Wpf : Engine.None;
	}

	/// <summary>The package names of one list property, which holds them between semicolons.</summary>
	private static IEnumerable<string> ReadList(XDocument targets, string property)
	{
		XElement? element = targets.Descendants(property).FirstOrDefault();

		Assert.IsNotNull(element, $"Directory.Build.targets defines no {property} property, so the build guard that reads it protects no package.");

		return element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	}

	/// <summary>
	/// The families that never reference each other: the engine pairs, and every pair of language packages, derived from the references.
	/// </summary>
	private static List<(string First, string Second, string Reason)> UnrelatedFamilies()
	{
		List<(string First, string Second, string Reason)> families = [.. s_unrelatedEngineFamilies];
		ProjectFile[] languages = [.. RepositoryModel.LanguagePackages];

		for (int first = 0; first < languages.Length; first++)
		{
			for (int second = first + 1; second < languages.Length; second++)
			{
				families.Add(
					(
						languages[first].Name,
						languages[second].Name,
						"a language package builds on the provider framework, never on another language package"
					)
				);
			}
		}

		return families;
	}

	/// <summary>The rows of the allowed table with the engine placeholder expanded and the prefix restored.</summary>
	private static IEnumerable<(string Library, string[] MayReference)> ExpandedRows()
	{
		foreach ((string library, string[] mayReference) in s_allowedReferences)
		{
			foreach (string engine in library.Contains(EnginePlaceholder, StringComparison.Ordinal) ? s_engines : [string.Empty])
			{
				yield return (
					Prefix + library.Replace(EnginePlaceholder, engine, StringComparison.Ordinal),
					[.. mayReference.Select(name => Prefix + name.Replace(EnginePlaceholder, engine, StringComparison.Ordinal))]
				);
			}
		}
	}

	/// <summary>The libraries a project may reference, or <see langword="null"/> when it has no row and is no language package.</summary>
	private static string[]? AllowedReferences(ProjectFile project)
	{
		foreach ((string library, string[] mayReference) in ExpandedRows())
		{
			if (library == project.Name)
				return mayReference;
		}

		return RepositoryModel.LanguagePackages.Any(language => language.Name == project.Name)
			? [.. s_languagePackageMayReference.Select(name => Prefix + name)]
			: null;
	}

	/// <summary>The lines of the first fenced block after a heading.</summary>
	private static string[] ReadFence(string markdown, string heading)
	{
		string[] lines = markdown.ReplaceLineEndings("\n").Split('\n');
		int start = Array.FindIndex(lines, line => line == heading);

		Assert.IsGreaterThanOrEqualTo(0, start, $"The page holds no heading '{heading}'.");

		int open = Array.FindIndex(lines, start, line => line.StartsWith("```", StringComparison.Ordinal));
		int close = Array.FindIndex(lines, open + 1, line => line.StartsWith("```", StringComparison.Ordinal));

		Assert.IsGreaterThan(0, close, $"The section '{heading}' holds no fenced block.");

		return lines[(open + 1)..close];
	}

	/// <summary>The project a diagram name stands for, or <see langword="null"/> for a NuGet package or an engine placeholder.</summary>
	private static string? ResolveDiagramName(string token)
	{
		foreach (string candidate in new[] { Prefix + token, Prefix + "LanguageServer." + token })
		{
			if (RepositoryModel.HasProject(candidate))
				return candidate;
		}

		return null;
	}

	// The namespaces of the public types the baselines of the libraries declare; a nested type's line names its outer type, which is skipped.
	private static IEnumerable<string> PublicTypeNamespaces()
	{
		HashSet<string> types = new(StringComparer.Ordinal);

		foreach (
			string baseline in new[] { "PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt" }.SelectMany(name =>
				RepositoryModel.EnumerateFiles(RepositoryPaths.Below("src"), name)
			)
		)
		{
			// A type declaration is the one baseline line without a space: members carry a signature, a value or a modifier.
			foreach (
				string line in File.ReadLines(baseline)
					.Where(line => line.Length > 0 && line[0] != '#' && !line.Contains(' ', StringComparison.Ordinal))
			)
			{
				types.Add(line);
			}
		}

		return types
			.Where(type => type.Contains('.', StringComparison.Ordinal))
			.Select(type => type[..type.LastIndexOf('.')])
			.Where(ns => !types.Contains(ns))
			.Distinct(StringComparer.Ordinal)
			.Order(StringComparer.Ordinal);
	}

	private static IEnumerable<string> BacktickTokens(string cell) => BacktickToken().Matches(cell).Select(match => match.Groups["token"].Value);

	private static IEnumerable<string> ExpandEngines(IEnumerable<string> names)
	{
		return names.SelectMany(name =>
			name.Contains(EnginePlaceholder, StringComparison.Ordinal)
				? s_engines.Select(engine => name.Replace(EnginePlaceholder, engine, StringComparison.Ordinal))
				: [name]
		);
	}

	[GeneratedRegex(@"^  (?<name>[A-Za-z][A-Za-z.<>]*)\s{2,}(?<rest>\S.*)$", RegexOptions.CultureInvariant)]
	private static partial Regex DiagramRow();

	[GeneratedRegex(@"^[A-Za-z][A-Za-z0-9.<>]*", RegexOptions.CultureInvariant)]
	private static partial Regex DiagramToken();

	[GeneratedRegex("`(?<token>[^`]+)`", RegexOptions.CultureInvariant)]
	private static partial Regex BacktickToken();
}
