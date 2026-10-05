using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>The namespaces of one IDEKit package depend on each other in one direction, so a package can be read from the bottom up.</summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed class NamespaceCycleGuardTests
{
	private const string PackagePrefix = "Nickelony.IDEKit.";

	// A package has at least this many namespaces, so a graph that found none fails instead of passing empty.
	private const int MinimumNamespaces = 1;

	// A package with at least this many namespaces has at least one edge between them. The language-feature bindings declare eight namespaces
	// that name no one another, so the threshold sits above eight.
	private const int NamespacesThatImplyAnEdge = 9;

	[TestMethod]
	public void NoCycleBetweenNamespacesOfOnePackage()
	{
		List<string> violations = [];

		foreach (
			ProjectFile project in RepositoryModel.SourceProjects.Where(candidate =>
				candidate.RelativePath.StartsWith("src/IDEKit/", StringComparison.Ordinal)
			)
		)
		{
			(DependencyGraph graph, int namespaces) = NamespaceGraph.Build(project);

			if (namespaces < MinimumNamespaces || (namespaces >= NamespacesThatImplyAnEdge && graph.EdgeCount == 0))
				violations.Add($"{project.Name} has {namespaces} namespaces and {graph.EdgeCount} edges, so its namespace graph proves nothing.");

			foreach (IReadOnlyList<string> cycle in graph.Cycles())
			{
				violations.Add(
					$"{Key(project, cycle)} ({graph.Describe(cycle)}). Move a type so the namespaces depend on each other in one direction."
				);
			}
		}

		Assert.IsEmpty(violations, "Namespace cycles:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	// Names a cycle by its package and its namespaces below the package root in alphabetical order.
	private static string Key(ProjectFile project, IReadOnlyList<string> cycle)
	{
		string package = project.Name[PackagePrefix.Length..];
		IEnumerable<string> relative = cycle.Select(name => name == project.Name ? "(root)" : name[(project.Name.Length + 1)..]);

		return package + ": " + string.Join(" <-> ", relative.Order(StringComparer.Ordinal));
	}

	/// <summary>
	/// The namespace graph of one package. An edge runs from a namespace to each namespace of the package it imports, and to each ancestor namespace
	/// whose types it names, because a nested namespace sees its ancestors without an import.
	/// </summary>
	private static class NamespaceGraph
	{
		internal static (DependencyGraph Graph, int Namespaces) Build(ProjectFile project)
		{
			DependencyGraph graph = new();
			List<(SourceFile Source, SyntaxNode Root, string Namespace)> files = [];
			Dictionary<string, HashSet<string>> typesByNamespace = new(StringComparer.Ordinal);

			foreach (string path in project.Sources)
			{
				SourceFile source = RepositoryModel.Source(path);
				SyntaxNode root = source.Tree([.. project.DefineConstants]).GetRoot();

				string? declared = root.DescendantNodes()
					.OfType<BaseNamespaceDeclarationSyntax>()
					.Select(node => node.Name.ToString())
					.FirstOrDefault();

				if (declared is null)
					continue;

				files.Add((source, root, declared));
				CollectTypes(root, declared, typesByNamespace);
			}

			foreach ((SourceFile source, SyntaxNode root, string current) in files)
			{
				AddImportEdges(graph, source, root, current, typesByNamespace);
				AddAncestorEdges(graph, source, root, current, typesByNamespace);
			}

			return (graph, typesByNamespace.Count);
		}

		private static void CollectTypes(SyntaxNode root, string declared, Dictionary<string, HashSet<string>> typesByNamespace)
		{
			if (!typesByNamespace.TryGetValue(declared, out HashSet<string>? types))
			{
				types = new HashSet<string>(StringComparer.Ordinal);
				typesByNamespace[declared] = types;
			}

			foreach (
				SyntaxNode member in root.DescendantNodes().Where(node => node.Parent is BaseNamespaceDeclarationSyntax or CompilationUnitSyntax)
			)
			{
				if (member is BaseTypeDeclarationSyntax type)
					types.Add(type.Identifier.ValueText);
				else if (member is DelegateDeclarationSyntax @delegate)
					types.Add(@delegate.Identifier.ValueText);
			}
		}

		private static void AddImportEdges(
			DependencyGraph graph,
			SourceFile source,
			SyntaxNode root,
			string current,
			Dictionary<string, HashSet<string>> typesByNamespace
		)
		{
			foreach (UsingDirectiveSyntax directive in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
			{
				if (directive.Alias is not null || !directive.StaticKeyword.IsKind(SyntaxKind.None) || directive.Name is null)
					continue;

				string imported = directive.Name.ToString();

				if (imported != current && typesByNamespace.ContainsKey(imported))
					graph.AddEdge(current, imported, $"{source.RelativePath} imports {imported}");
			}
		}

		private static void AddAncestorEdges(
			DependencyGraph graph,
			SourceFile source,
			SyntaxNode root,
			string current,
			Dictionary<string, HashSet<string>> typesByNamespace
		)
		{
			IEnumerable<string> tokens = root.DescendantTokens(descendIntoTrivia: true)
				.Where(token => token.IsKind(SyntaxKind.IdentifierToken))
				.Select(token => token.ValueText);

			HashSet<string> identifiers = new(tokens, StringComparer.Ordinal);
			HashSet<string> shadowed = new(typesByNamespace[current], StringComparer.Ordinal);

			for (string ancestor = Parent(current); ancestor.Length > 0; ancestor = Parent(ancestor))
			{
				if (!typesByNamespace.TryGetValue(ancestor, out HashSet<string>? ancestorTypes))
					continue;

				if (ancestorTypes.Any(name => !shadowed.Contains(name) && identifiers.Contains(name)))
					graph.AddEdge(current, ancestor, $"{source.RelativePath} names a type of {ancestor}");

				shadowed.UnionWith(ancestorTypes);
			}
		}

		private static string Parent(string name)
		{
			int dot = name.LastIndexOf('.');
			return dot < 0 ? string.Empty : name[..dot];
		}
	}
}
