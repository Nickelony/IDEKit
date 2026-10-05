using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// A library references the projects whose namespaces it names and no others, so a reference list shows what a package builds on and a package
/// that stops using a dependency drops it.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed class ReferenceUsageGuardTests
{
	private const string Guard = nameof(ReferenceUsageGuardTests);

	// A reference that a library declares and its source does not use, or a project whose namespace it names without a reference, keyed
	// "{project} references {project}" or "{project} names {project}", both below the Nickelony. prefix.
	private static readonly AllowList s_knownMismatches = new("reference mismatches");

	[TestMethod]
	public void ALibraryReferencesEachProjectWhoseNamespaceItNamesAndNoOther()
	{
		IReadOnlyList<ProjectFile> libraries = [.. RepositoryModel.SourceProjects];
		Dictionary<string, HashSet<string>> owners = new(StringComparer.Ordinal);

		foreach (ProjectFile library in libraries)
		{
			foreach (string declared in RepositoryModel.DeclaredNamespaces(library))
			{
				if (!owners.TryGetValue(declared, out HashSet<string>? projects))
				{
					projects = new HashSet<string>(StringComparer.Ordinal);
					owners[declared] = projects;
				}

				projects.Add(library.Name);
			}
		}

		List<string> violations = [];

		foreach (ProjectFile library in libraries)
		{
			// A namespace the library declares itself needs no reference, and a name found only there does not count as use.
			HashSet<string> named = [.. NamedNamespaces(library, owners.Keys).Where(name => !owners[name].Contains(library.Name))];

			// A type of an enclosing namespace needs no using, so an enclosing namespace is a use that requires nothing.
			HashSet<string> used =
			[
				.. EnclosingNamespaces(library, owners.Keys).Where(name => !owners[name].Contains(library.Name)).SelectMany(name => owners[name]),
			];

			foreach (string name in named.Order(StringComparer.Ordinal))
			{
				string[] declaring = [.. owners[name].Order(StringComparer.Ordinal)];
				used.UnionWith(declaring);

				if (declaring.Any(project => library.ProjectReferences.Contains(project, StringComparer.Ordinal)))
					continue;

				string key = $"{Short(library.Name)} names {string.Join(" or ", declaring.Select(Short))}";

				if (!s_knownMismatches.Allows(key))
				{
					violations.Add(
						s_knownMismatches.Rejection(
							$"{library.RelativePath}: names the namespace '{name}' of {string.Join(" or ", declaring)} without referencing it.",
							Guard
						)
					);
				}
			}

			foreach (string reference in library.ProjectReferences.Where(reference => !used.Contains(reference)))
			{
				string key = $"{Short(library.Name)} references {Short(reference)}";

				if (!s_knownMismatches.Allows(key))
				{
					violations.Add(
						s_knownMismatches.Rejection($"{library.RelativePath}: references '{reference}' and names none of its namespaces.", Guard)
					);
				}
			}
		}

		violations.AddRange(s_knownMismatches.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Declared and used references differ:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	private static string Short(string project) => project["Nickelony.".Length..];

	/// <summary>
	/// The namespaces that enclose the ones a library declares. A type of an enclosing namespace needs no <c>using</c>, so a library that declares
	/// <c>A.B.C</c> uses a project that declares <c>A.B</c> by name.
	/// </summary>
	private static IEnumerable<string> EnclosingNamespaces(ProjectFile library, ICollection<string> declaredNamespaces)
	{
		foreach (string declared in RepositoryModel.DeclaredNamespaces(library))
		{
			for (int dot = declared.LastIndexOf('.'); dot > 0; dot = declared.LastIndexOf('.', dot - 1))
			{
				if (declaredNamespaces.Contains(declared[..dot]))
					yield return declared[..dot];
			}
		}
	}

	/// <summary>
	/// The declared namespaces of other projects that a library names, in a <c>using</c> directive, a project <c>Using</c> item, a qualified name
	/// or a documentation reference, found at the longest prefix that some project declares.
	/// </summary>
	private static IEnumerable<string> NamedNamespaces(ProjectFile library, IEnumerable<string> declaredNamespaces)
	{
		HashSet<string> declared = [.. declaredNamespaces];
		HashSet<string> named = new(StringComparer.Ordinal);

		foreach (string chain in QualifiedChains(library))
		{
			for (
				string prefix = chain;
				prefix.Length > 0;
				prefix = prefix.Contains('.', StringComparison.Ordinal) ? prefix[..prefix.LastIndexOf('.')] : string.Empty
			)
			{
				if (declared.Contains(prefix))
				{
					named.Add(prefix);
					break;
				}
			}
		}

		return named;
	}

	private static IEnumerable<string> QualifiedChains(ProjectFile library)
	{
		string[] symbols = [.. library.DefineConstants];

		foreach (string path in library.Sources)
		{
			SyntaxNode root = RepositoryModel.Source(path).Tree(symbols).GetRoot();

			foreach (SyntaxNode node in root.DescendantNodes(descendIntoTrivia: true))
			{
				bool isChain = node is QualifiedNameSyntax or MemberAccessExpressionSyntax;

				bool isInner =
					(node.Parent is QualifiedNameSyntax outer && outer.Left == node)
					|| (node.Parent is MemberAccessExpressionSyntax access && access.Expression == node);

				if (!isChain || isInner)
					continue;

				string text = node.ToString().Replace(" ", string.Empty, StringComparison.Ordinal);

				if (text.StartsWith("Nickelony.", StringComparison.Ordinal))
					yield return text;
			}
		}

		var project = XDocument.Load(RepositoryPaths.Below(library.RelativePath.Split('/')));

		foreach (string include in project.Descendants("Using").Select(item => (string?)item.Attribute("Include")).OfType<string>())
			yield return include;
	}
}
