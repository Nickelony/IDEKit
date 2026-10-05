using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// The folders of a package with one namespace depend on each other in one direction, so a package can be read
/// from the bottom up. The namespace shows no such direction, so the folder graph is the only structure to keep. The package root holds the facade
/// and the types that several folders share, so it is no node and an edge into or out of it is not checked.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed class FolderCycleGuardTests
{
	private const string Guard = nameof(FolderCycleGuardTests);

	// The root of a package is two nodes: the types that folders may name, and the composition that builds the folders' collaborators.
	private const string RootTypes = "(root)";

	private const string RootComposition = "(composition)";

	// The last word of the type of a root file that composes the package: the facade, the controller, the provider, the composition root and the teardown that shuts the composed collaborators down.
	private static readonly string[] s_compositionSuffixes = ["Client", "Provider", "Composition", "Teardown", "Controller"];

	private const string ProviderProject = RepositoryModel.ProviderPackage;

	private const string ClientProject = "Nickelony.LanguageServer.Client";

	// A package with folders has at least this many folder nodes, so a graph that found none fails instead of passing empty.
	private const int MinimumFolders = 1;

	// A package with at least this many folders has at least one edge between them.
	private const int FoldersThatImplyAnEdge = 5;

	// A folder whose direct subfolders are nodes of their own, keyed "{project}/{folder}". The files that sit directly in it form one more node.
	private static readonly string[] s_splitFolders = [ProviderProject + "/Watching", ClientProject + "/Protocol"];

	// A group of folders that depend on each other, keyed "{project below Nickelony}: {folders in alphabetical order}".
	private static readonly AllowList s_knownCycles = new("folder cycles");

	// A package whose source sits at the package root, so it has no folder graph, keyed by the project name.
	private static readonly AllowList s_packagesWithoutFolders = new(
		"packages without folders",
		("Nickelony.KeyBindings.Avalonia", "The adapter holds its source at the package root."),
		("Nickelony.KeyBindings.Wpf", "The adapter holds its source at the package root."),
		("Nickelony.LanguageServer.Lua", "The language package holds its source at the package root."),
		("Nickelony.Matching", "The leaf holds its source at the package root."),
		("Nickelony.QuickPick.Avalonia", "The adapter holds its source at the package root."),
		("Nickelony.QuickPick.Wpf", "The adapter holds its source at the package root."),
		("Nickelony.LanguageServer.Roslyn", "The language package holds its source at the package root.")
	);

	private static readonly ConcurrentDictionary<string, FolderGraph> s_graphs = new(StringComparer.Ordinal);

	public TestContext TestContext { get; set; } = null!;

	// The libraries with one flat namespace per package, which read from the bottom up by folder.
	private static IEnumerable<string> FlatPackages => RepositoryModel.FlatNamespacePackages.Select(project => project.Name);

	[TestMethod]
	public void NoCycleBetweenFoldersOfOnePackage()
	{
		List<string> violations = [];

		foreach (string name in FlatPackages)
		{
			FolderGraph folders = Folders(name);

			TestContext.WriteLine(
				$"{Short(name)}: {folders.Nodes.Count} folders, {folders.IgnoredNames.Count} names declared in several places are ignored."
			);

			int folderCount = folders.Nodes.Count(node => !IsRoot(node));
			bool vacuous = folderCount < MinimumFolders || (folderCount >= FoldersThatImplyAnEdge && folders.Graph.EdgeCount == 0);

			if (vacuous && !s_packagesWithoutFolders.Allows(name))
			{
				violations.Add(
					s_packagesWithoutFolders.Rejection(
						$"{Short(name)} has {folderCount} folders and {folders.Graph.EdgeCount} edges, so its folder graph proves nothing.",
						Guard
					)
				);
			}

			IReadOnlyList<IReadOnlyList<string>> cycles = [.. folders.Graph.Cycles()];

			if (cycles.Count == 0 && folders.Nodes.Count > 1)
			{
				string layers = string.Join(" < ", folders.Graph.Layers(folders.Nodes).Select(layer => string.Join(", ", layer)));
				TestContext.WriteLine($"  order, lowest first: {layers}");
			}

			foreach (IReadOnlyList<string> group in cycles)
			{
				string key = $"{Short(name)}: {string.Join(", ", group)}";
				string evidence = folders.Graph.Describe(group);

				TestContext.WriteLine($"  cycle {key} ({evidence})");

				if (!s_knownCycles.Allows(key))
					violations.Add(s_knownCycles.Rejection($"{key} depend on each other ({evidence}).", Guard));
			}
		}

		violations.AddRange(s_knownCycles.UnmatchedMessages(Guard));
		violations.AddRange(s_packagesWithoutFolders.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Folder cycles:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void AFolderIsOneNodeUnlessItIsSplit()
	{
		List<string> violations = [];

		foreach (string name in FlatPackages)
		{
			foreach (string node in Folders(name).Nodes.Where(node => node.Contains('/', StringComparison.Ordinal)))
			{
				string folder = node[..node.IndexOf('/', StringComparison.Ordinal)];

				if (!s_splitFolders.Contains($"{name}/{folder}", StringComparer.Ordinal))
					violations.Add($"{Short(name)}: '{node}' is a subfolder node, yet '{folder}' is not a split folder.");
			}
		}

		foreach (string split in s_splitFolders)
		{
			int slash = split.IndexOf('/', StringComparison.Ordinal);
			string folder = split[(slash + 1)..];

			if (!Folders(split[..slash]).Nodes.Any(node => node.StartsWith(folder + "/", StringComparison.Ordinal)))
				violations.Add($"The split folder '{split}' holds no subfolder. Remove it from the split folders of {Guard}.");
		}

		Assert.IsEmpty(violations, "Folder nodes:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void TheInternalsGuideListsTheFolderOrderOfTheProvider()
	{
		FolderGraph folders = Folders(ProviderProject);

		// A cycle that the known list holds closes no layer of the order, so the order is read over the edges that remain.

		string order = string.Join("\n", folders.Graph.Layers(folders.Nodes).Select(layer => string.Join(", ", layer)));
		string guide = File.ReadAllText(RepositoryPaths.Below("docs", "LanguageServerInternals.md")).ReplaceLineEndings("\n");

		Assert.IsTrue(
			guide.Contains(order, StringComparison.Ordinal),
			$"docs/LanguageServerInternals.md lists the provider folders, one layer per line, lowest first. It holds no block that reads:{Environment.NewLine}{order}"
		);
	}

	private static string Short(string project) => project["Nickelony.".Length..];

	private static FolderGraph Folders(string project) => s_graphs.GetOrAdd(project, name => Build(RepositoryModel.Project(name)));

	/// <summary>
	/// The node of a file, given its path below the project: its top-level folder, the subfolder of a split folder, or one of the two nodes
	/// of the package root, the composition for a root file whose type builds the package and the types for every other root file.
	/// </summary>
	private static string NodeOf(string project, string relative)
	{
		string[] segments = relative.Split('/');

		if (segments.Length == 1)
		{
			string type = segments[0].Split('.')[0];
			bool isInterface = type.Length > 1 && type[0] == 'I' && char.IsUpper(type[1]);

			return !isInterface && s_compositionSuffixes.Any(suffix => type.EndsWith(suffix, StringComparison.Ordinal)) ? RootComposition : RootTypes;
		}

		bool isSplit = segments.Length > 2 && s_splitFolders.Contains($"{project}/{segments[0]}", StringComparer.Ordinal);

		return isSplit ? $"{segments[0]}/{segments[1]}" : segments[0];
	}

	private static bool IsRoot(string node) => node is RootTypes or RootComposition;

	/// <summary>
	/// The folder graph of one package. An edge runs from a folder to another when a file of the first names a type that only the second declares.
	/// A name that two places declare is ignored, because the match is by identifier and cannot tell which type a file means.
	/// </summary>
	private static FolderGraph Build(ProjectFile project)
	{
		string directory = project.RelativeDirectory + "/";

		List<(string Relative, string Node, SyntaxNode Root)> files = [];
		Dictionary<string, HashSet<string>> declaredIn = new(StringComparer.Ordinal);

		foreach (string path in project.Sources.Where(path => path.StartsWith(directory, StringComparison.Ordinal)))
		{
			string relative = path[directory.Length..];
			string node = NodeOf(project.Name, relative);

			SyntaxNode root = RepositoryModel.Source(path).Tree([.. project.DefineConstants]).GetRoot();

			files.Add((relative, node, root));

			foreach (string type in TopLevelTypeNames(root))
			{
				if (!declaredIn.TryGetValue(type, out HashSet<string>? places))
				{
					places = new HashSet<string>(StringComparer.Ordinal);
					declaredIn[type] = places;
				}

				places.Add(node);
			}
		}

		DependencyGraph graph = new();
		SortedSet<string> nodes = new(StringComparer.Ordinal);

		foreach ((string relative, string node, SyntaxNode root) in files)
		{
			nodes.Add(node);

			foreach (string identifier in Identifiers(root).Order(StringComparer.Ordinal))
			{
				if (!declaredIn.TryGetValue(identifier, out HashSet<string>? places) || places.Count != 1)
					continue;

				string target = places.Single();

				if (target != node)
					graph.AddEdge(node, target, $"{relative} names {identifier} of {target}");
			}
		}

		return new FolderGraph(
			graph,
			[.. nodes],
			[.. declaredIn.Where(pair => pair.Value.Count > 1).Select(pair => pair.Key).Order(StringComparer.Ordinal)]
		);
	}

	/// <summary>The names of the types a file declares directly in a namespace or at the top of the file.</summary>
	private static IEnumerable<string> TopLevelTypeNames(SyntaxNode root)
	{
		foreach (SyntaxNode member in root.DescendantNodes().Where(node => node.Parent is BaseNamespaceDeclarationSyntax or CompilationUnitSyntax))
		{
			if (member is BaseTypeDeclarationSyntax type)
				yield return type.Identifier.ValueText;
			else if (member is DelegateDeclarationSyntax @delegate)
				yield return @delegate.Identifier.ValueText;
		}
	}

	/// <summary>The identifiers of a file, with those of its doc comment references, because a reference names a type as code does.</summary>
	private static HashSet<string> Identifiers(SyntaxNode root)
	{
		IEnumerable<string> tokens = root.DescendantTokens(descendIntoTrivia: true)
			.Where(token => token.IsKind(SyntaxKind.IdentifierToken))
			.Select(token => token.ValueText);

		return new HashSet<string>(tokens, StringComparer.Ordinal);
	}

	/// <summary>The graph of the folders of one package, its nodes, and the names that were ignored for being declared in several places.</summary>
	private sealed record FolderGraph(DependencyGraph Graph, IReadOnlyList<string> Nodes, IReadOnlyList<string> IgnoredNames);
}
