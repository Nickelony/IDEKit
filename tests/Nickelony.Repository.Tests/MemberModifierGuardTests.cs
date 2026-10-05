using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// A member of an internal type is internal or private unless it implements an interface or overrides a member, and a member that only in-assembly
/// callers reach takes no <see langword="null"/> guard, because in-assembly callers are trusted.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed class MemberModifierGuardTests
{
	private const string Guard = nameof(MemberModifierGuardTests);

	// The members of the framework interfaces that the repository's types implement; an interface that the repository does not declare
	// contributes these names.
	private static readonly string[] s_frameworkInterfaceMembers =
	[
		"Dispose",
		"DisposeAsync",
		"GetEnumerator",
		"Equals",
		"CompareTo",
		"MoveNext",
		"Current",
		"Reset",
		"Read",
		"Write",
		"CanConvert",
		"ToString",
		"GetHashCode",
		"Count",
		"this[]",
		"Contains",
		"TryGetValue",
		"Keys",
		"Values",
		"ContainsKey",
		"Add",
		"Remove",
		"Clear",
		"CopyTo",
		"IsReadOnly",
		"Item",
		"Report",
		"Post",
		"Send",
		"Invoke",
		"Compare",
	];

	private static readonly Lazy<(List<Offender> PublicMembers, List<Offender> NullGuards)> s_scan = new(Scan);

	// A null guard in a non-public member that stays because it checks a value no public member can check first, keyed "{path}: {type}.{member}".
	private static readonly AllowList s_nullGuards = new(
		"null guards in non-public members",

		(
			"shared/Infrastructure/Paths/LocalPathRules.cs: LocalPathRules.Normalize",
			"Public members forward their argument to this member, which names the caller's argument expression."
		),

		(
			"shared/Infrastructure/Paths/LocalPathRules.cs: LocalPathRules.NormalizeRoots",
			"Public members forward their arguments to this member, which names the caller's argument expression."
		),

		(
			"shared/Primitives/Requests/LatestRequestGate.cs: LatestRequestGate.RunCoreAsync",
			"The public entry points forward to this asynchronous member, so the guard faults the task they return."
		),

		(
			"src/LanguageServer/Nickelony.LanguageServer.Client/LanguageServerClient.cs: LanguageServerClient..ctor",
			"A public constructor chains to this one, so the arguments are checked here."
		),

		(
			"src/LanguageServer/Nickelony.LanguageServer.Client/Configuration/SettingsSnapshotCache.cs: SettingsSnapshotCache.CreateCachedSettingsSnapshotFromProvider",
			"The guard checks the result of a host-supplied delegate."
		),

		(
			"src/LanguageServer/Nickelony.LanguageServer.Provider/LanguageServerFeatureProvider.Features.cs: LanguageServerFeatureProvider.ResolveAsync",
			"The explicit implementation of the optional resolver contract is the public entry point of the call, and its interface documents the check."
		),

		(
			"src/LanguageServer/Nickelony.LanguageServer.Provider/LanguageServerFeatureProvider.Features.cs: LanguageServerFeatureProvider.TryRunAsync",
			"The explicit implementation of the optional command-runner contract is the public entry point of the call, and its interface documents the check."
		),

		(
			"src/LanguageServer/Nickelony.LanguageServer.Provider/LanguageServerFeatureProvider.cs: LanguageServerFeatureProvider..ctor",
			"A public constructor chains to this one, so the profile is checked here."
		),

		(
			"src/LanguageServer/Nickelony.LanguageServer.Provider/LanguageServerFeatureProvider.cs: LanguageServerFeatureProvider.CreateClient",
			"The public constructor calls this member in its chained argument list, before the internal constructor takes the client and checks the profile."
		),

		(
			"src/IDEKit/Nickelony.IDEKit.FileWatching/FileWatcher.cs: FileWatcher..ctor",
			"A public constructor chains to this one, so the arguments are checked here."
		),

		(
			"src/IDEKit/Nickelony.IDEKit.FileWatching/FileWatchSupervisor.cs: FileWatchSupervisor.TryStartFileWatcher",
			"The guard checks the result of a host-supplied factory, and the enclosing catch classifies the exception."
		)
	);

	[TestMethod]
	public void ANonPublicTypeDeclaresNoPublicMember()
	{
		List<string> violations = [];

		foreach (
			IGrouping<string, Offender> type in s_scan.Value.PublicMembers.GroupBy(member => $"{member.Path}: {member.Type}", StringComparer.Ordinal)
		)
		{
			string members = string.Join(", ", type.Select(member => member.Member).Distinct(StringComparer.Ordinal));
			violations.Add($"{type.Key} declares the public members {members}, and the type is not public. Make them internal.");
		}

		Assert.IsEmpty(violations, "Public members of non-public types:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void ANonPublicMemberTakesNoNullGuard()
	{
		List<string> violations = [];

		foreach (
			string key in s_scan.Value.NullGuards.Select(member => $"{member.Path}: {member.Type}.{member.Member}").Distinct(StringComparer.Ordinal)
		)
		{
			if (!s_nullGuards.Allows(key))
				violations.Add(s_nullGuards.Rejection($"{key} guards an argument with ThrowIfNull, and the member is not public.", Guard));
		}

		violations.AddRange(s_nullGuards.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Null guards in non-public members:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void EveryTypeInAnInternalOnlyFolderIsInternal()
	{
		List<string> violations = [];

		foreach (string folder in RepositoryModel.InternalOnlyFolders)
		{
			IReadOnlyList<string> paths = RepositoryModel.SourcePathsUnder(folder);

			if (paths.Count == 0)
			{
				violations.Add(
					$"The internal-only folder '{folder}' holds no source file. Remove it from {nameof(RepositoryModel.InternalOnlyFolders)}."
				);
			}

			foreach (string path in paths)
			{
				foreach (SyntaxTree tree in RepositoryModel.Source(path).TreesOfEveryBranch([]))
				{
					foreach (BaseTypeDeclarationSyntax type in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
					{
						if (type.Parent is not TypeDeclarationSyntax && type.Modifiers.Any(SyntaxKind.PublicKeyword))
							violations.Add($"{path}: {type.Identifier.ValueText} is public, and the folder '{folder}' holds internal types only.");
					}
				}
			}
		}

		Assert.IsEmpty(
			violations,
			"Internal-only folders:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Distinct(StringComparer.Ordinal))
		);
	}

	private static (List<Offender> PublicMembers, List<Offender> NullGuards) Scan()
	{
		List<(string Path, SyntaxNode Root)> roots = [];

		foreach (string path in RepositoryModel.SourcePathsUnder("src", "shared"))
		{
			SourceFile source = RepositoryModel.Source(path);

			// A type of a visibility-selecting home is public where it is published, and the internal copy in the family keeps the same member
			// modifiers, so only the public variant is read.
			IEnumerable<SyntaxTree> trees =
				RepositoryModel.SharedHomeOf(path)?.SelectsVisibility == true ? [source.Tree()] : source.TreesOfEveryBranch([]);

			foreach (SyntaxTree tree in trees)
				roots.Add((path, tree.GetRoot()));
		}

		// The names a type's base list gives, merged over the partial declarations of one type name, and the declarations of each interface.
		Dictionary<string, HashSet<string>> basesOf = new(StringComparer.Ordinal);
		Dictionary<string, List<InterfaceDeclarationSyntax>> interfaces = new(StringComparer.Ordinal);

		foreach ((_, SyntaxNode root) in roots)
		{
			foreach (TypeDeclarationSyntax declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
			{
				string name = declaration.Identifier.ValueText;

				if (!basesOf.TryGetValue(name, out HashSet<string>? bases))
				{
					bases = new HashSet<string>(StringComparer.Ordinal);
					basesOf[name] = bases;
				}

				bases.UnionWith((declaration.BaseList?.Types ?? []).Select(baseType => SimpleName(baseType.Type)));

				if (declaration is InterfaceDeclarationSyntax declared)
				{
					if (!interfaces.TryGetValue(name, out List<InterfaceDeclarationSyntax>? list))
					{
						list = [];
						interfaces[name] = list;
					}

					list.Add(declared);
				}
			}
		}

		List<Offender> publicMembers = [];
		List<Offender> nullGuards = [];

		foreach ((string path, SyntaxNode root) in roots)
		{
			foreach (TypeDeclarationSyntax type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
			{
				bool typeIsPublic = IsPublicType(type);

				bool isConverter = basesOf[type.Identifier.ValueText]
					.Any(name => name.EndsWith("JsonConverter", StringComparison.Ordinal) || name == "JsonConverterFactory");

				HashSet<string> implemented = ImplementedNames(basesOf[type.Identifier.ValueText], interfaces);

				// A record's equality member is part of the type's contract, and the compiler requires it public.
				if (type is RecordDeclarationSyntax)
					implemented.Add("Equals");

				foreach (
					MemberDeclarationSyntax member in type.Members.Where(member =>
						member is not (BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
					)
				)
				{
					string name = NameOf(member);

					if (!typeIsPublic && IsPublicOffender(member, name, implemented, isConverter))
						publicMembers.Add(new Offender(path, type.Identifier.ValueText, name));

					bool memberIsPublic =
						typeIsPublic
						&& member.Modifiers.Any(modifier =>
							modifier.IsKind(SyntaxKind.PublicKeyword) || modifier.IsKind(SyntaxKind.ProtectedKeyword)
						);

					if (!memberIsPublic && member is BaseMethodDeclarationSyntax method && HasNullGuard(method))
						nullGuards.Add(new Offender(path, type.Identifier.ValueText, name));
				}
			}
		}

		return (publicMembers, nullGuards);
	}

	/// <summary>
	/// Reports whether a public member of a non-public type has no reason to be public: it is no override, no explicit implementation, no operator,
	/// no member that a listed interface declares, no JSON-RPC target, no member that a serializer reads and no public constructor of a converter,
	/// which the serializer creates by reflection.
	/// </summary>
	private static bool IsPublicOffender(MemberDeclarationSyntax member, string name, HashSet<string> implemented, bool isConverter)
	{
		if (!member.Modifiers.Any(SyntaxKind.PublicKeyword) || member.Modifiers.Any(SyntaxKind.OverrideKeyword))
			return false;

		bool isExplicit =
			member
			is MethodDeclarationSyntax { ExplicitInterfaceSpecifier: not null }
				or PropertyDeclarationSyntax { ExplicitInterfaceSpecifier: not null };

		bool isOperator = member is OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax;
		bool isConverterConstructor = isConverter && member is ConstructorDeclarationSyntax;

		bool isRead = member
			.AttributeLists.SelectMany(list => list.Attributes)
			.Any(attribute => attribute.Name.ToString().StartsWith("Json", StringComparison.Ordinal));

		return !isExplicit && !isOperator && !isConverterConstructor && !isRead && !implemented.Contains(name);
	}

	private static bool HasNullGuard(BaseMethodDeclarationSyntax method) =>
		method.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(call => call.Expression.ToString() == "ArgumentNullException.ThrowIfNull");

	/// <summary>
	/// The member names that the interfaces in a type's base list declare, including the interfaces they extend. An interface the repository
	/// does not declare contributes the common framework names.
	/// </summary>
	private static HashSet<string> ImplementedNames(IEnumerable<string> bases, Dictionary<string, List<InterfaceDeclarationSyntax>> interfaces)
	{
		HashSet<string> names = new(StringComparer.Ordinal);
		HashSet<string> visited = new(StringComparer.Ordinal);

		Stack<string> pending = new(bases);

		while (pending.Count > 0)
		{
			string current = pending.Pop();

			if (!visited.Add(current))
				continue;

			if (!interfaces.TryGetValue(current, out List<InterfaceDeclarationSyntax>? declarations))
			{
				names.UnionWith(s_frameworkInterfaceMembers);
				continue;
			}

			foreach (InterfaceDeclarationSyntax declaration in declarations)
			{
				names.UnionWith(declaration.Members.Select(NameOf));

				foreach (BaseTypeSyntax inherited in declaration.BaseList?.Types ?? [])
					pending.Push(SimpleName(inherited.Type));
			}
		}

		return names;
	}

	private static string SimpleName(TypeSyntax type)
	{
		string text = type.ToString();
		string simple = text[(text.LastIndexOf('.') + 1)..];

		int generic = simple.IndexOf('<', StringComparison.Ordinal);

		return generic > 0 ? simple[..generic] : simple;
	}

	/// <summary>Reports whether the type and every type around it is public.</summary>
	private static bool IsPublicType(TypeDeclarationSyntax type)
	{
		for (SyntaxNode? node = type; node is not null; node = node.Parent)
		{
			if (node is BaseTypeDeclarationSyntax declaration && !declaration.Modifiers.Any(SyntaxKind.PublicKeyword))
				return false;
		}

		return true;
	}

	private static string NameOf(MemberDeclarationSyntax member)
	{
		return member switch
		{
			MethodDeclarationSyntax method => method.Identifier.Text,
			PropertyDeclarationSyntax property => property.Identifier.Text,
			EventDeclarationSyntax @event => @event.Identifier.Text,
			EventFieldDeclarationSyntax eventField => eventField.Declaration.Variables[0].Identifier.Text,
			FieldDeclarationSyntax field => field.Declaration.Variables[0].Identifier.Text,
			IndexerDeclarationSyntax => "this[]",
			ConstructorDeclarationSyntax => ".ctor",
			_ => "?",
		};
	}

	private sealed record Offender(string Path, string Type, string Member);
}
