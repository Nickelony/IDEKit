using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// An <c>InternalsVisibleTo</c> grant in a library project names only the test assembly of its own project, so a package reaches another package's
/// public or protected surface and nothing else, and a shared folder of internal helpers declares no public type, because every assembly
/// that compiles it would then export its own copy. The grant guard reads the project items, the <c>AssemblyAttribute</c> items,
/// the assembly attributes of every file a project compiles (every branch of its conditional blocks), and the MSBuild files that apply
/// to every project.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed class InternalsVisibleToGuardTests
{
	private const string Guard = nameof(InternalsVisibleToGuardTests);

	private const string EveryProject = "(every project)";

	// A grant to the test assembly of another project, keyed "{granting project} -> {test assembly}".
	private static readonly AllowList s_foreignGrants = new("foreign grants");

	[TestMethod]
	public void AGrantInALibraryProjectNamesATestAssembly()
	{
		HashSet<string> testAssemblies =
		[
			.. RepositoryModel
				.Projects.Where(project => project.IsTestProject && project.Name.EndsWith(".Tests", StringComparison.Ordinal))
				.Select(project => project.Name),
		];

		List<string> violations = [];

		foreach (Grant grant in Grants())
			AddViolationWhenNotATestAssembly(grantee: grant.Grantee, origin: grant.Origin, testAssemblies, violations);

		string message =
			"InternalsVisibleTo grants name an assembly outside the repository's test projects:"
			+ Environment.NewLine
			+ string.Join(Environment.NewLine, violations);

		Assert.IsEmpty(
			violations,

			message
				+ Environment.NewLine
				+ "A package that extends another one uses its public or protected surface, so make that surface public instead of granting the package its internals."
		);
	}

	[TestMethod]
	public void AGrantNamesTheTestAssemblyOfItsOwnProject()
	{
		List<string> violations = [];

		foreach (Grant grant in Grants())
		{
			// A grant in a file that applies to every project can only be right when it names the project through its property.
			string expected = grant.Project is null ? "$(MSBuildProjectName).Tests" : grant.Project + ".Tests";

			if (grant.Grantee != expected && !s_foreignGrants.Allows(grant.Key))
			{
				violations.Add(
					s_foreignGrants.Rejection(
						$"{grant.Origin}: grants internals to '{grant.Grantee}', which is not the test project of {grant.Project ?? EveryProject}.",
						Guard
					)
				);
			}
		}

		violations.AddRange(s_foreignGrants.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Grants to the tests of another project:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void ATestAssemblyReceivesGrantsFromOneAssemblyOnly()
	{
		List<string> violations =
		[
			.. Grants()
				.Where(grant => !s_foreignGrants.Entries.Contains(grant.Key, StringComparer.Ordinal))
				.GroupBy(grant => grant.Grantee, StringComparer.Ordinal)
				.Select(group =>
					(
						Grantee: group.Key,
						Projects: group
							.Select(grant => grant.Project ?? EveryProject)
							.Distinct(StringComparer.Ordinal)
							.Order(StringComparer.Ordinal)
							.ToList()
					)
				)
				.Where(entry => entry.Projects.Count > 1)
				.Select(entry =>
					$"'{entry.Grantee}' receives grants from {string.Join(", ", entry.Projects)}. A test assembly sees the internals of one assembly, the one it tests."
				)
				.Order(StringComparer.Ordinal),
		];

		Assert.IsEmpty(violations, "Test assemblies with several grantors:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void AnInternalHelperFolderOfSharedDeclaresOnlyInternalTypes()
	{
		HashSet<string> violations = new(StringComparer.Ordinal);

		foreach (string folder in RepositoryModel.InternalHelperFolders)
		{
			IReadOnlyList<string> files = RepositoryModel.SourcePathsUnder(folder);

			if (files.Count == 0)
			{
				violations.Add(
					$"The internal-helper folder '{folder}' holds no source file. Remove it from {nameof(RepositoryModel.InternalHelperFolders)}."
				);
			}

			foreach (string path in files)
			{
				foreach (SyntaxTree tree in RepositoryModel.Source(path).TreesOfEveryBranch([]))
				{
					foreach (MemberDeclarationSyntax member in tree.GetRoot().DescendantNodes().OfType<MemberDeclarationSyntax>().Where(IsTopLevel))
					{
						if (IsPublicType(member))
						{
							violations.Add(
								$"{path}: declares a public type. Several assemblies compile this folder, so make the type internal, or move it to the package that owns it."
							);
						}
					}
				}
			}
		}

		Assert.IsEmpty(
			violations,

			"Public types in an internal-helper folder:"
				+ Environment.NewLine
				+ string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal))
		);
	}

	private static bool IsTopLevel(MemberDeclarationSyntax member) => member.Parent is BaseNamespaceDeclarationSyntax or CompilationUnitSyntax;

	private static bool IsPublicType(MemberDeclarationSyntax member)
	{
		return member switch
		{
			BaseTypeDeclarationSyntax type => type.Modifiers.Any(SyntaxKind.PublicKeyword),
			DelegateDeclarationSyntax @delegate => @delegate.Modifiers.Any(SyntaxKind.PublicKeyword),
			_ => false,
		};
	}

	/// <summary>
	/// Every grant the library projects declare, with the project that makes it. A grant in a file that applies to every project has no project.
	/// </summary>
	private static IEnumerable<Grant> Grants()
	{
		foreach (ProjectFile project in RepositoryModel.SourceProjects)
		{
			foreach ((string grantee, string origin) in ReadItemGrants(project.RelativePath).Concat(ReadAttributeGrants(project)))
				yield return new Grant(project.Name, grantee, origin);
		}

		foreach (string buildFile in SharedBuildFiles())
		{
			foreach ((string grantee, string origin) in ReadItemGrants(buildFile))
				yield return new Grant(null, grantee, origin);
		}
	}

	private static void AddViolationWhenNotATestAssembly(string grantee, string origin, HashSet<string> testAssemblies, List<string> violations)
	{
		if (!testAssemblies.Contains(grantee))
			violations.Add($"{origin}: grants internals to '{grantee}', which is not a test project of this repository.");
	}

	/// <summary>
	/// The MSBuild files that apply to every library project: the props and targets files at the root and every one below <c>src</c>.
	/// </summary>
	private static IEnumerable<string> SharedBuildFiles()
	{
		string root = RepositoryPaths.Root();
		string source = Path.Combine(root, "src");

		IEnumerable<string> atRoot = Directory.EnumerateFiles(root, "*.props").Concat(Directory.EnumerateFiles(root, "*.targets"));

		IEnumerable<string> belowSource = RepositoryModel
			.EnumerateFiles(source, "*.props")
			.Concat(RepositoryModel.EnumerateFiles(source, "*.targets"));

		return atRoot.Concat(belowSource).Select(file => Path.GetRelativePath(root, file).Replace('\\', '/')).Order(StringComparer.Ordinal);
	}

	/// <summary>
	/// Reads the <c>InternalsVisibleTo</c> items and the <c>AssemblyAttribute</c> items that name the attribute, as the assembly name each one names
	/// and where it is declared.
	/// </summary>
	private static IEnumerable<(string Grantee, string Origin)> ReadItemGrants(string relativePath)
	{
		var document = XDocument.Load(RepositoryPaths.Below(relativePath.Split('/')));

		foreach (XElement item in document.Descendants("InternalsVisibleTo"))
		{
			string? include = (string?)item.Attribute("Include");

			if (!string.IsNullOrWhiteSpace(include))
				yield return (AssemblyName(include), relativePath);
		}

		foreach (XElement item in document.Descendants("AssemblyAttribute"))
		{
			string? include = (string?)item.Attribute("Include");

			if (include is null || !IsInternalsVisibleToName(include))
				continue;

			string? argument = (string?)item.Element("_Parameter1") ?? (string?)item.Attribute("_Parameter1");

			yield return (string.IsNullOrWhiteSpace(argument) ? "<no _Parameter1>" : AssemblyName(argument), relativePath);
		}
	}

	/// <summary>Reads the <c>[assembly: InternalsVisibleTo]</c> attributes of the files a project compiles.</summary>
	private static IEnumerable<(string Grantee, string Origin)> ReadAttributeGrants(ProjectFile project)
	{
		HashSet<(string Grantee, string Origin)> grants = [];

		foreach (string path in project.Sources)
		{
			SourceFile source = RepositoryModel.Source(path);

			// A file that never says the word cannot declare the attribute, so most files skip the parse.
			if (!source.Text.Contains("InternalsVisibleTo", StringComparison.Ordinal))
				continue;

			// A grant under a conditional symbol is read whether or not the project defines the symbol.
			foreach (SyntaxTree tree in source.TreesOfEveryBranch(project.DefineConstants))
			{
				foreach (AttributeSyntax attribute in tree.GetRoot().DescendantNodes().OfType<AttributeSyntax>())
				{
					if (
						attribute.Parent is not AttributeListSyntax { Target.Identifier.ValueText: "assembly" }
						|| !IsInternalsVisibleToName(attribute.Name.ToString())
					)
					{
						continue;
					}

					string? grantee = (attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression as LiteralExpressionSyntax)?.Token.ValueText;

					grants.Add((grantee is null ? "<not a string literal>" : AssemblyName(grantee), path));
				}
			}
		}

		return grants.Order();
	}

	private static bool IsInternalsVisibleToName(string name)
	{
		string simpleName = name[(name.LastIndexOf('.') + 1)..];
		return simpleName is "InternalsVisibleTo" or "InternalsVisibleToAttribute";
	}

	/// <summary>Takes the assembly name from a grant, which can carry a public key after a comma.</summary>
	private static string AssemblyName(string grant)
	{
		int comma = grant.IndexOf(',', StringComparison.Ordinal);
		return (comma < 0 ? grant : grant[..comma]).Trim();
	}

	/// <summary>
	/// A grant of internals to the assembly <paramref name="Grantee"/>, made by <paramref name="Project"/> in <paramref name="Origin"/>.
	/// </summary>
	private sealed record Grant(string? Project, string Grantee, string Origin)
	{
		/// <summary>The grant as the known-failure list names it: <c>{granting project} -> {test assembly}</c>.</summary>
		internal string Key => $"{Project ?? EveryProject} -> {Grantee}";
	}
}
