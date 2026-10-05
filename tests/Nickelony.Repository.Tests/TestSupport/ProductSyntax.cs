using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Nickelony.Repository.Tests;

/// <summary>A source file of a library project, parsed under the symbols of that project.</summary>
internal sealed record ProductFile(ProjectFile Project, string Path, SyntaxNode Root);

/// <summary>The syntax of the library projects, read once per project and file, for the guards that look at member declarations.</summary>
internal static class ProductSyntax
{
	/// <summary>
	/// Every source file of every library project under that project's own symbols, so a shared file yields the variant that the project compiles.
	/// A file that several projects compile appears once for each.
	/// </summary>
	internal static IEnumerable<ProductFile> Files()
	{
		foreach (ProjectFile project in RepositoryModel.SourceProjects)
		{
			foreach (string path in project.Sources)
				yield return new ProductFile(project, path, RepositoryModel.Source(path).Tree([.. project.DefineConstants]).GetRoot());
		}
	}

	/// <summary>Every class, struct, interface and record that the files declare, including nested ones.</summary>
	internal static IEnumerable<(ProductFile File, TypeDeclarationSyntax Type)> Types() =>
		Files().SelectMany(file => file.Root.DescendantNodes().OfType<TypeDeclarationSyntax>().Select(type => (file, type)));

	/// <summary>
	/// Reports whether a host that references the package can name the type: the type and each type around it is public, or a nested type
	/// is protected.
	/// </summary>
	internal static bool IsReachable(BaseTypeDeclarationSyntax type)
	{
		for (SyntaxNode? node = type; node is not null; node = node.Parent)
		{
			if (node is not BaseTypeDeclarationSyntax declaration)
				continue;

			bool nested = declaration.Parent is TypeDeclarationSyntax;
			bool visible = declaration.Modifiers.Any(SyntaxKind.PublicKeyword) || (nested && IsProtected(declaration.Modifiers));

			if (!visible)
				return false;
		}

		return true;
	}

	/// <summary>Reports whether a member is public or protected, so a host can call or read it; an interface member is public by default.</summary>
	internal static bool IsVisible(MemberDeclarationSyntax member, TypeDeclarationSyntax owner)
	{
		if (owner is InterfaceDeclarationSyntax)
			return !member.Modifiers.Any(SyntaxKind.PrivateKeyword) && !member.Modifiers.Any(SyntaxKind.InternalKeyword);

		return member.Modifiers.Any(SyntaxKind.PublicKeyword) || IsProtected(member.Modifiers);
	}

	/// <summary>The names of a type and the types around it, outermost first and joined by dots.</summary>
	internal static string NameOf(BaseTypeDeclarationSyntax type)
	{
		return string.Join(
			".",
			type.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().Reverse().Select(declaration => declaration.Identifier.ValueText)
		);
	}

	private static bool IsProtected(SyntaxTokenList modifiers) =>
		modifiers.Any(SyntaxKind.ProtectedKeyword) && !modifiers.Any(SyntaxKind.PrivateKeyword);
}
