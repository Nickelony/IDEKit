using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// An options bag is a sealed record whose members a host sets through <c>init</c> and never through a public setter or a public field,
/// and it declares a static <c>Default</c> that a consumer substitutes for the all-defaults instance, so no consumer re-derives a default.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed class OptionsShapeGuardTests
{
	private const string Guard = nameof(OptionsShapeGuardTests);

	// A type whose name ends in Options and that is no sealed record, keyed "{path}: {type}".
	private static readonly AllowList s_otherShapes = new("options types that are no sealed record");

	// An options record with a public setter or a public mutable field, keyed "{path}: {type}.{member}".
	private static readonly AllowList s_mutableMembers = new("options members that a host can assign after construction");

	// An options record that declares no static Default, keyed "{path}: {type}".
	private static readonly AllowList s_withoutDefault = new(
		"options records without a Default",

		(
			"src/IDEKit/Nickelony.IDEKit.LanguageFeatures/Formatting/FormattingOptions.cs: FormattingOptions",
			"The record keeps the LSP name, and its tab size and indentation choice are required constructor arguments, so no all-defaults instance exists."
		)
	);

	[TestMethod]
	public void AnOptionsTypeIsASealedRecord()
	{
		List<string> violations = [];

		foreach (
			string offender in OptionsTypes()
				.Where(type => !IsSealedRecord(type.Type))
				.Select(type => type.Key)
				.Distinct()
				.Order(StringComparer.Ordinal)
		)
		{
			if (!s_otherShapes.Allows(offender))
				violations.Add(s_otherShapes.Rejection($"{offender} is named for an options bag and is no sealed record.", Guard));
		}

		violations.AddRange(s_otherShapes.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Options types:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void AnOptionsRecordHasNoPublicSetterAndNoPublicMutableField()
	{
		HashSet<string> offenders = new(StringComparer.Ordinal);

		foreach ((string key, TypeDeclarationSyntax type) in OptionsTypes().Where(type => IsSealedRecord(type.Type)))
		{
			foreach (
				PropertyDeclarationSyntax property in type
					.Members.OfType<PropertyDeclarationSyntax>()
					.Where(property => IsExposed(property.Modifiers))
			)
			{
				bool hasSetter =
					property.AccessorList?.Accessors.Any(accessor =>
						accessor.IsKind(SyntaxKind.SetAccessorDeclaration) && !accessor.Modifiers.Any(SyntaxKind.PrivateKeyword)
					) == true;

				if (hasSetter && !property.Modifiers.Any(SyntaxKind.StaticKeyword))
					offenders.Add($"{key}.{property.Identifier.ValueText}");
			}

			foreach (FieldDeclarationSyntax field in type.Members.OfType<FieldDeclarationSyntax>().Where(field => IsExposed(field.Modifiers)))
			{
				bool immutable =
					field.Modifiers.Any(SyntaxKind.ConstKeyword)
					|| field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword)
					|| field.Modifiers.Any(SyntaxKind.StaticKeyword);

				if (!immutable)
					offenders.UnionWith(field.Declaration.Variables.Select(variable => $"{key}.{variable.Identifier.ValueText}"));
			}
		}

		List<string> violations = [];

		foreach (string offender in offenders.Order(StringComparer.Ordinal).Where(offender => !s_mutableMembers.Allows(offender)))
		{
			violations.Add(
				s_mutableMembers.Rejection($"{offender} can be assigned after the options record exists. Make it an init-only property.", Guard)
			);
		}

		violations.AddRange(s_mutableMembers.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Options members:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void AnOptionsRecordDeclaresAStaticDefaultOfItsOwnType()
	{
		HashSet<string> offenders = new(StringComparer.Ordinal);

		foreach ((string key, TypeDeclarationSyntax type) in OptionsTypes().Where(type => IsSealedRecord(type.Type)))
		{
			if (!type.Members.Any(member => DeclaresDefault(member, type.Identifier.ValueText)))
				offenders.Add(key);
		}

		List<string> violations = [];

		foreach (string offender in offenders.Order(StringComparer.Ordinal).Where(offender => !s_withoutDefault.Allows(offender)))
		{
			violations.Add(
				s_withoutDefault.Rejection(
					$"{offender} declares no public static Default of its own type, so each consumer would re-derive the defaults of its members.",
					Guard
				)
			);
		}

		violations.AddRange(s_withoutDefault.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Options defaults:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	/// <summary>Every type whose name ends in <c>Options</c>, except a private nested type, which no host can name.</summary>
	private static IEnumerable<(string Key, TypeDeclarationSyntax Type)> OptionsTypes()
	{
		HashSet<string> seen = new(StringComparer.Ordinal);
		int read = 0;

		foreach ((ProductFile file, TypeDeclarationSyntax type) in ProductSyntax.Types())
		{
			if (!type.Identifier.ValueText.EndsWith("Options", StringComparison.Ordinal))
				continue;

			bool privateNested = type.Parent is TypeDeclarationSyntax && type.Modifiers.Any(SyntaxKind.PrivateKeyword);

			if (privateNested)
				continue;

			read++;
			string key = $"{file.Path}: {ProductSyntax.NameOf(type)}";

			if (seen.Add(key))
				yield return (key, type);
		}

		Assert.IsGreaterThan(0, read, "The guard read no options type, so it checks nothing.");
	}

	private static bool IsSealedRecord(TypeDeclarationSyntax type)
	{
		return type is RecordDeclarationSyntax record
			&& record.ClassOrStructKeyword.IsKind(SyntaxKind.None)
			&& record.Modifiers.Any(SyntaxKind.SealedKeyword);
	}

	/// <summary>Reports whether a host that holds the record can reach the member: it is public, internal or protected.</summary>
	private static bool IsExposed(SyntaxTokenList modifiers) =>
		modifiers.Any(SyntaxKind.PublicKeyword) || modifiers.Any(SyntaxKind.InternalKeyword) || modifiers.Any(SyntaxKind.ProtectedKeyword);

	private static bool DeclaresDefault(MemberDeclarationSyntax member, string typeName)
	{
		if (!member.Modifiers.Any(SyntaxKind.PublicKeyword) || !member.Modifiers.Any(SyntaxKind.StaticKeyword))
			return false;

		return member switch
		{
			PropertyDeclarationSyntax property => property.Identifier.ValueText == "Default" && IsNamed(property.Type, typeName),

			FieldDeclarationSyntax field => field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == "Default")
				&& IsNamed(field.Declaration.Type, typeName),

			_ => false,
		};
	}

	private static bool IsNamed(TypeSyntax type, string name) => type is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == name;
}
