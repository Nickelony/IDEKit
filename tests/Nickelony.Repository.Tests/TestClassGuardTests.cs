using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>A test class is sealed or abstract, or it is the base of another class, so a reader knows whether a class is a leaf.</summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed class TestClassGuardTests
{
	[TestMethod]
	public void ATestClassIsSealedOrAbstractOrABaseClass()
	{
		List<(string Path, ClassDeclarationSyntax Class)> classes = [];

		foreach (string path in RepositoryModel.SourcePathsUnder("tests"))
		{
			foreach (SyntaxTree tree in RepositoryModel.Source(path).TreesOfEveryBranch([]))
			{
				foreach (ClassDeclarationSyntax declaration in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
					classes.Add((path, declaration));
			}
		}

		// A class that another class derives from is a base class, whether or not it is a test class itself.
		HashSet<string> bases = new(
			classes.SelectMany(entry => entry.Class.BaseList?.Types ?? []).Select(type => SimpleName(type.Type)),
			StringComparer.Ordinal
		);

		// A partial class states its attribute and its modifiers on any of its parts, so a class is judged by the union of its parts, and two
		// projects that name a class alike keep separate classes because their namespaces differ.
		IEnumerable<IGrouping<string, (string Path, ClassDeclarationSyntax Class)>> byType = classes.GroupBy(
			entry => QualifiedName(entry.Class),
			StringComparer.Ordinal
		);

		HashSet<string> violations = new(StringComparer.Ordinal);

		foreach (
			IGrouping<string, (string Path, ClassDeclarationSyntax Class)> group in byType.Where(group =>
				group.Any(entry => IsTestClass(entry.Class))
			)
		)
		{
			bool isLeaf = group.Any(entry =>
				entry.Class.Modifiers.Any(SyntaxKind.SealedKeyword) || entry.Class.Modifiers.Any(SyntaxKind.AbstractKeyword)
			);

			string name = group.First().Class.Identifier.ValueText;

			if (!isLeaf && !bases.Contains(name))
			{
				violations.Add(
					$"{group.First().Path}: {name} is a test class that is neither sealed nor abstract, and no class derives from it. Seal it."
				);
			}
		}

		Assert.IsEmpty(
			violations,
			"Open test classes:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal))
		);
	}

	/// <summary>The namespace, the enclosing types and the name of a class, so the parts of a partial class share one key.</summary>
	private static string QualifiedName(ClassDeclarationSyntax declaration)
	{
		IEnumerable<string> outer = declaration
			.Ancestors()
			.Where(node => node is BaseNamespaceDeclarationSyntax or BaseTypeDeclarationSyntax)
			.Reverse()
			.Select(node =>
				node is BaseNamespaceDeclarationSyntax container ? container.Name.ToString() : ((BaseTypeDeclarationSyntax)node).Identifier.ValueText
			);

		return string.Join('.', outer.Append(declaration.Identifier.ValueText));
	}

	/// <summary>Reports whether a class carries a <c>[TestClass]</c> attribute, or a derived one such as the Avalonia host's.</summary>
	private static bool IsTestClass(ClassDeclarationSyntax declaration)
	{
		return declaration
			.AttributeLists.SelectMany(list => list.Attributes)
			.Select(attribute => SimpleName(attribute.Name))
			.Any(name => name.EndsWith("TestClass", StringComparison.Ordinal) || name.EndsWith("TestClassAttribute", StringComparison.Ordinal));
	}

	/// <summary>The last name of a possibly qualified or generic type, such as <c>Base</c> from <c>Ns.Base&lt;T&gt;</c>.</summary>
	private static string SimpleName(TypeSyntax type)
	{
		return type switch
		{
			QualifiedNameSyntax qualified => SimpleName(qualified.Right),
			AliasQualifiedNameSyntax aliased => SimpleName(aliased.Name),
			SimpleNameSyntax simple => simple.Identifier.ValueText,
			_ => type.ToString(),
		};
	}
}
