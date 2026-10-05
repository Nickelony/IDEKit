using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// A public payload collection is read-only: the property, field and return type of a public member is never an array, a mutable collection
/// or a bare sequence, so a host cannot change what the library hands out and the collection types stay uniform.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed class PublicCollectionGuardTests
{
	private const string Guard = nameof(PublicCollectionGuardTests);

	// The collection types a public member does not declare: the read-only list, set and dictionary interfaces replace them.
	private static readonly HashSet<string> s_ruledOutTypes = new(StringComparer.Ordinal)
	{
		"Collection",
		"Dictionary",
		"HashSet",
		"ICollection",
		"IDictionary",
		"IEnumerable",
		"IList",
		"ImmutableArray",
		"ImmutableDictionary",
		"ImmutableHashSet",
		"ImmutableList",
		"ImmutableSortedDictionary",
		"ImmutableSortedSet",
		"IReadOnlyCollection",
		"ISet",
		"KeyedCollection",
		"List",
		"Queue",
		"ReadOnlyCollection",
		"ReadOnlyDictionary",
		"SortedDictionary",
		"SortedList",
		"SortedSet",
		"Stack",
	};

	// A public member that keeps a ruled-out collection type, keyed "{path}: {type}.{member}", where the member of a positional record is its parameter.
	private static readonly AllowList s_collections = new(
		"public collections",

		(
			"shared/Editor/Highlighting/RegexHighlightingDefinition.cs: RegexHighlightingDefinition.BuildRules",
			"An abstract hook that a host implements with yield returns the sequence it builds."
		),

		(
			"shared/Editor/Highlighting/RegexHighlightingDefinition.cs: RegexHighlightingDefinition.BuildSpans",
			"A virtual hook that a host overrides with yield returns the sequence it builds."
		),

		(
			"shared/Editor/Highlighting/RegexHighlightingDefinition.cs: RegexHighlightingDefinition.NamedHighlightingColors",
			"IHighlightingDefinition of the engine declares the member with this type."
		),

		(
			"shared/Editor/Highlighting/RegexHighlightingDefinition.cs: RegexHighlightingDefinition.Properties",
			"IHighlightingDefinition of the engine declares the member with this type."
		),

		(
			"src/IDEKit/Nickelony.IDEKit.Core/Editing/PreparedTextEdits.cs: PreparedTextEdits.MapOffsets",
			"The method hands the caller a new array that the caller owns."
		),

		(
			"src/IDEKit/Nickelony.IDEKit.Core/Text/TextLineSplitter.cs: TextLineSplitter.Split",
			"The method hands the caller a new array that the caller owns."
		),

		(
			"src/IDEKit/Nickelony.IDEKit.Core/Text/TextLineSplitter.cs: TextLineSplitter.SplitIncludingTerminators",
			"The method hands the caller a new array that the caller owns."
		),

		(
			"src/IDEKit/Nickelony.IDEKit.FileWatching/FileSnapshotTracker.cs: FileSnapshotTracker.CloneTrackedSnapshot",
			"The method hands the caller a clone that the caller owns."
		),

		(
			"src/IDEKit/Nickelony.IDEKit.FileWatching/FileSnapshotTracker.cs: FileSnapshotTracker.ReplaceTrackedSnapshotWithCurrent",
			"The method hands the caller a clone that the caller owns."
		),

		(
			"src/IDEKit/Nickelony.IDEKit.FileWatching/IFileSnapshotTracker.cs: IFileSnapshotTracker.CloneTrackedSnapshot",
			"The method hands the caller a clone that the caller owns."
		),

		(
			"src/IDEKit/Nickelony.IDEKit.FileWatching/IFileSnapshotTracker.cs: IFileSnapshotTracker.ReplaceTrackedSnapshotWithCurrent",
			"The method hands the caller a clone that the caller owns."
		),

		(
			"src/IDEKit/Nickelony.IDEKit.Workspace/FileSystem/WorkspaceTextCodec.cs: WorkspaceTextCodec.Encode",
			"The method returns the bytes of a file, which no host reads as a payload collection."
		),

		(
			"src/KeyBindings/Nickelony.KeyBindings/Overrides/Entries/KeyBindingOverrideChord.cs: KeyBindingOverrideChord.Strokes",
			"The override document keeps mutable lists for its serializer."
		),

		(
			"src/KeyBindings/Nickelony.KeyBindings/Overrides/Entries/KeyBindingOverrideEntry.cs: KeyBindingOverrideEntry.Bindings",
			"The override document keeps mutable lists for its serializer."
		),

		(
			"src/KeyBindings/Nickelony.KeyBindings/Overrides/KeyBindingOverrides.cs: KeyBindingOverrides.Entries",
			"The override document keeps mutable lists for its serializer."
		),

		(
			"src/LanguageServer/Nickelony.LanguageServer.Client/Protocol/SemanticTokens/SemanticTokensResponse.cs: SemanticTokensResponse.Data",
			"A serializer DTO that ClientGuide names keeps an array member."
		)
	);

	[TestMethod]
	public void APublicPayloadCollectionIsNotAnArrayAMutableCollectionOrABareSequence()
	{
		HashSet<string> offenders = new(StringComparer.Ordinal);

		foreach ((ProductFile file, TypeDeclarationSyntax type) in ProductSyntax.Types())
		{
			if (!ProductSyntax.IsReachable(type))
				continue;

			string owner = ProductSyntax.NameOf(type);

			if (type is RecordDeclarationSyntax { ParameterList: not null } record)
			{
				foreach (ParameterSyntax parameter in record.ParameterList.Parameters)
				{
					if (IsRuledOut(parameter.Type))
						offenders.Add($"{file.Path}: {owner}.{parameter.Identifier.ValueText}");
				}
			}

			foreach (MemberDeclarationSyntax member in type.Members.Where(member => ProductSyntax.IsVisible(member, type)))
			{
				if (member.Modifiers.Any(SyntaxKind.OverrideKeyword) || IsExplicitImplementation(member) || HasExtensionDataAttribute(member))
					continue;

				string? name = member switch
				{
					PropertyDeclarationSyntax property when IsRuledOut(property.Type) => property.Identifier.ValueText,
					IndexerDeclarationSyntax indexer when IsRuledOut(indexer.Type) => "this[]",
					FieldDeclarationSyntax field when IsRuledOut(field.Declaration.Type) => field.Declaration.Variables[0].Identifier.ValueText,
					MethodDeclarationSyntax method when IsRuledOut(method.ReturnType) => method.Identifier.ValueText,
					_ => null,
				};

				if (name is not null)
					offenders.Add($"{file.Path}: {owner}.{name}");
			}
		}

		List<string> violations = [];

		foreach (string offender in offenders.Order(StringComparer.Ordinal))
		{
			if (!s_collections.Allows(offender))
			{
				violations.Add(
					s_collections.Rejection(
						$"{offender} is public and its type is an array, a mutable collection or a bare sequence. Type it IReadOnlyList, IReadOnlySet or IReadOnlyDictionary.",
						Guard
					)
				);
			}
		}

		violations.AddRange(s_collections.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Public collections:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	/// <summary>Reports whether the declared type is an array or one of the ruled-out collection types, once a task or a nullable wraps it.</summary>
	private static bool IsRuledOut(TypeSyntax? type)
	{
		return type switch
		{
			NullableTypeSyntax nullable => IsRuledOut(nullable.ElementType),
			ArrayTypeSyntax => true,

			GenericNameSyntax { Identifier.ValueText: "Task" or "ValueTask" } task when task.TypeArgumentList.Arguments.Count == 1 => IsRuledOut(
				task.TypeArgumentList.Arguments[0]
			),

			GenericNameSyntax generic => s_ruledOutTypes.Contains(generic.Identifier.ValueText),
			QualifiedNameSyntax qualified => IsRuledOut(qualified.Right),
			_ => false,
		};
	}

	private static bool IsExplicitImplementation(MemberDeclarationSyntax member)
	{
		return member
			is MethodDeclarationSyntax { ExplicitInterfaceSpecifier: not null }
				or PropertyDeclarationSyntax { ExplicitInterfaceSpecifier: not null }
				or IndexerDeclarationSyntax { ExplicitInterfaceSpecifier: not null };
	}

	private static bool HasExtensionDataAttribute(MemberDeclarationSyntax member)
	{
		return member
			.AttributeLists.SelectMany(list => list.Attributes)
			.Any(attribute => attribute.Name.ToString().EndsWith("JsonExtensionData", StringComparison.Ordinal));
	}
}
