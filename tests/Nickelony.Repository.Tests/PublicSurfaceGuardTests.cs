using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// A public type of a library ships with a consumer: another library of the repository or a sample host names it in code, or a row gives the adopter,
/// the extension call, the wire record or the host scenario that needs it, so the public surface holds nothing that nothing produces or reads.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed partial class PublicSurfaceGuardTests
{
	private const string Guard = nameof(PublicSurfaceGuardTests);

	private const string AdopterKey = "adopter:";
	private const string ExtensionKey = "extension:";
	private const string WireKey = "wire:";
	private const string ScenarioKey = "scenario:";

	// The key that opens the reason of an unconsumed type, and what it claims:
	// adopter - a host outside the repository names the type in code, which a grep of that host confirms;
	// extension - the type holds extension methods that no library or sample of the repository calls;
	// wire - the type is a record of the language-server wire model that a host reads through a Client event or a request result;
	// scenario - a host that does the thing the reason names needs the type.
	private static readonly string[] s_reasonKeys = [AdopterKey, ExtensionKey, WireKey, ScenarioKey];

	[TestMethod]
	public void EveryPublicTypeHasAConsumerOrARow()
	{
		List<string> violations = [];
		IReadOnlyList<ProjectFile> libraries = [.. RepositoryModel.SourceProjects];

		// A sample host consumes the public surface of the libraries it references the way another library does.
		IReadOnlyList<ProjectFile> candidates = [.. libraries, .. RepositoryModel.SampleProjects];

		var reaches = candidates.ToDictionary(candidate => candidate.Name, Reach, StringComparer.Ordinal);
		var uses = candidates.ToDictionary(candidate => candidate.Name, ReadUse, StringComparer.Ordinal);

		foreach (ProjectFile library in libraries)
		{
			ProjectFile[] consumers = [.. candidates.Where(other => other.Name != library.Name && reaches[other.Name].Contains(library.Name))];

			foreach (PublicType type in PublicTypes(library))
			{
				bool used = consumers.Any(consumer => uses[consumer.Name].Names(type));

				if (!used && !s_unconsumedTypes.Allows($"{Short(library.Name)}: {type.Name}"))
				{
					violations.Add(
						s_unconsumedTypes.Rejection(
							$"{Short(library.Name)}: {type.Name} is public, and no library or sample that references {library.Name} names it in code. Give it a consumer, make it internal, or add a row whose reason starts with a key.",
							Guard
						)
					);
				}
			}
		}

		violations.AddRange(s_unconsumedTypes.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Public types without a consumer:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void EveryReasonOfAnUnconsumedTypeStartsWithAKeyThatTheTypeSupports()
	{
		List<string> violations = [];

		Dictionary<string, (PublicType Type, string Project)> declared = new(StringComparer.Ordinal);

		foreach (ProjectFile library in RepositoryModel.SourceProjects)
		{
			foreach (PublicType type in PublicTypes(library))
				declared[$"{Short(library.Name)}: {type.Name}"] = (type, library.Name);
		}

		foreach ((string entry, string reason) in s_unconsumedTypes.Reasons.OrderBy(row => row.Key, StringComparer.Ordinal))
		{
			string? key = s_reasonKeys.FirstOrDefault(candidate => reason.StartsWith(candidate, StringComparison.Ordinal));

			if (key is null)
			{
				violations.Add(
					$"The reason of '{entry}' starts with none of {string.Join(", ", s_reasonKeys)}. Start it with the key that says why the type stays public."
				);

				continue;
			}

			if (!declared.TryGetValue(entry, out (PublicType Type, string Project) found))
				continue;

			if (key == ExtensionKey && found.Type.ExtensionMethods.Count == 0)
				violations.Add($"The reason of '{entry}' starts with {ExtensionKey}, and the type declares no extension method.");

			if (key == WireKey && found.Project != "Nickelony.LanguageServer.Client")
				violations.Add($"The reason of '{entry}' starts with {WireKey}, and the type is not in the language-server client package.");
		}

		Assert.IsEmpty(violations, "Reasons without a supported key:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	private static string Short(string project) => project["Nickelony.".Length..];

	/// <summary>The projects that a project reaches through its project references, directly or through other projects.</summary>
	private static HashSet<string> Reach(ProjectFile project)
	{
		HashSet<string> reached = new(StringComparer.Ordinal);
		Stack<string> pending = new(project.ProjectReferences);

		while (pending.Count > 0)
		{
			string current = pending.Pop();

			if (!reached.Add(current) || !RepositoryModel.HasProject(current))
				continue;

			foreach (string next in RepositoryModel.Project(current).ProjectReferences)
				pending.Push(next);
		}

		return reached;
	}

	/// <summary>
	/// The public types a project declares at the top level of a file under its own symbols, once each, with the extension methods
	/// of all their parts. A block of another engine's symbol declares no type of this project.
	/// </summary>
	private static IReadOnlyList<PublicType> PublicTypes(ProjectFile project)
	{
		Dictionary<string, PublicType> types = new(StringComparer.Ordinal);

		foreach (string path in project.Sources)
		{
			SyntaxTree tree = RepositoryModel.Source(path).Tree([.. project.DefineConstants]);

			foreach (BaseTypeDeclarationSyntax type in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
			{
				if (type.Parent is not BaseNamespaceDeclarationSyntax @namespace || !type.Modifiers.Any(SyntaxKind.PublicKeyword))
					continue;

				string name = type.Identifier.ValueText;
				string key = $"{@namespace.Name}.{name}";

				if (types.TryGetValue(key, out PublicType? existing))
					existing.ExtensionMethods.UnionWith(ExtensionMethodNames(type));
				else
					types.Add(key, new PublicType(name, @namespace.Name.ToString(), ExtensionMethodNames(type)));
			}
		}

		return [.. types.Values];
	}

	/// <summary>The names of the extension methods that a static class declares, or none for any other type.</summary>
	private static HashSet<string> ExtensionMethodNames(BaseTypeDeclarationSyntax type)
	{
		if (type is not ClassDeclarationSyntax declaration || !declaration.Modifiers.Any(SyntaxKind.StaticKeyword))
			return [];

		return new HashSet<string>(
			declaration
				.Members.OfType<MethodDeclarationSyntax>()
				.Where(method => method.ParameterList.Parameters.FirstOrDefault()?.Modifiers.Any(SyntaxKind.ThisKeyword) == true)
				.Select(method => method.Identifier.ValueText),

			StringComparer.Ordinal
		);
	}

	/// <summary>What the files of a project name in code, and which namespaces each of them sees.</summary>
	private static ProjectUse ReadUse(ProjectFile project)
	{
		// A global using, or a Using item of the project file, applies to every file of the project.
		HashSet<string> projectImports = new(StringComparer.Ordinal);
		List<FileUse> files = [];

		foreach (string path in project.Sources)
		{
			SourceFile source = RepositoryModel.Source(path);

			foreach (SyntaxTree tree in source.TreesOfEveryBranch(project.DefineConstants))
			{
				SyntaxNode root = tree.GetRoot();

				HashSet<string> tokens = new(
					root.DescendantTokens().Where(token => token.IsKind(SyntaxKind.IdentifierToken)).Select(token => token.ValueText),
					StringComparer.Ordinal
				);

				HashSet<string> declared = new(
					root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Select(type => type.Identifier.ValueText),
					StringComparer.Ordinal
				);

				// An extension method is called as a member of its first argument, so the call never names the static class that holds it.
				HashSet<string> calledMembers = new(
					root.DescendantNodes()
						.OfType<InvocationExpressionSyntax>()
						.Select(call =>
							call.Expression switch
							{
								MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
								MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
								_ => null,
							}
						)
						.OfType<string>(),

					StringComparer.Ordinal
				);

				HashSet<string> visible = new(StringComparer.Ordinal);

				foreach (UsingDirectiveSyntax directive in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
				{
					string? name = directive.Name?.ToString();

					if (name is null || !directive.StaticKeyword.IsKind(SyntaxKind.None))
						continue;

					visible.Add(name);

					if (!directive.GlobalKeyword.IsKind(SyntaxKind.None))
						projectImports.Add(name);
				}

				foreach (BaseNamespaceDeclarationSyntax @namespace in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
				{
					// A file inside A.B.C sees the types of A.B.C, A.B and A without a using.
					for (
						string name = @namespace.Name.ToString();
						name.Length > 0;
						name = name.Contains('.', StringComparison.Ordinal) ? name[..name.LastIndexOf('.')] : string.Empty
					)
					{
						visible.Add(name);
					}
				}

				files.Add(new FileUse(Text: source.Text, tokens, declared, calledMembers, visible));
			}
		}

		var document = System.Xml.Linq.XDocument.Load(RepositoryPaths.Below(project.RelativePath.Split('/')));

		foreach (string include in document.Descendants("Using").Select(item => (string?)item.Attribute("Include")).OfType<string>())
			projectImports.Add(include);

		return new ProjectUse(files, projectImports);
	}

	private sealed record PublicType(string Name, string Namespace, HashSet<string> ExtensionMethods);

	private sealed record FileUse(
		string Text,
		HashSet<string> Tokens,
		HashSet<string> Declared,
		HashSet<string> CalledMembers,
		HashSet<string> Visible
	);

	private sealed record ProjectUse(List<FileUse> Files, HashSet<string> Imports)
	{
		/// <summary>
		/// Reports whether a file of the project, without declaring the type, names it through a namespace that it sees,
		/// or calls one of the extension methods that it holds as a member of an expression.
		/// </summary>
		/// <remarks>
		/// The extension match is by method name, so a call of another method of the same name counts as a use.
		/// </remarks>
		internal bool Names(PublicType type)
		{
			return Files.Any(file =>
			{
				if (file.Declared.Contains(type.Name))
					return false;

				bool sees = file.Visible.Contains(type.Namespace) || Imports.Contains(type.Namespace);

				bool namesClass =
					file.Tokens.Contains(type.Name) && (sees || file.Text.Contains($"{type.Namespace}.{type.Name}", StringComparison.Ordinal));

				return namesClass || (sees && file.CalledMembers.Overlaps(type.ExtensionMethods));
			});
		}
	}
}
