using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// A public type sits in the root namespace of an IDEKit package with feature namespaces only when the whole package uses it and its name carries
/// the package concept, or when it belongs to the host-facing surface that the package's main audience imports.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed class RootNamespaceGuardTests
{
	private const string Guard = nameof(RootNamespaceGuardTests);

	// A public type in the root namespace of a package that also has feature namespaces, keyed by its qualified name. An entry that ends in *
	// covers every type whose qualified name starts with the text before the star.
	private static readonly AllowList s_rootTypes = new(
		"root-namespace types",

		(
			"Nickelony.IDEKit.Markdown.MarkdownRenderTheme",
			"The host-facing surface: the options, theme, pipeline and seams that a host composes, while the content model and the render plan sit in their own namespaces."
		),

		(
			"Nickelony.IDEKit.Markdown.MarkdownThickness",
			"The host-facing surface: the options, theme, pipeline and seams that a host composes, while the content model and the render plan sit in their own namespaces."
		),

		(
			"Nickelony.IDEKit.Markdown.IMarkdownHighlightingCatalog",
			"The host-facing surface: the options, theme, pipeline and seams that a host composes, while the content model and the render plan sit in their own namespaces."
		),

		(
			"Nickelony.IDEKit.Markdown.MarkdownFlowDirection",
			"The host-facing surface: the options, theme, pipeline and seams that a host composes, while the content model and the render plan sit in their own namespaces."
		),

		(
			"Nickelony.IDEKit.Markdown.MarkdownHighlightingResolver",
			"The host-facing surface: the options, theme, pipeline and seams that a host composes, while the content model and the render plan sit in their own namespaces."
		),

		(
			"Nickelony.IDEKit.Markdown.MarkdownHyperlinkActivator",
			"The host-facing surface: the options, theme, pipeline and seams that a host composes, while the content model and the render plan sit in their own namespaces."
		),

		(
			"Nickelony.IDEKit.Markdown.MarkdownRenderOptions",
			"The host-facing surface: the options, theme, pipeline and seams that a host composes, while the content model and the render plan sit in their own namespaces."
		),

		(
			"Nickelony.IDEKit.Markdown.MarkdownRenderPipeline",
			"The host-facing surface: the options, theme, pipeline and seams that a host composes, while the content model and the render plan sit in their own namespaces."
		),

		(
			"Nickelony.IDEKit.Workspace.Views.ApplyRequestedEventArgs",
			"The view contract: a host implements it once, and every Coordination type reaches a view through it."
		),

		(
			"Nickelony.IDEKit.Workspace.Views.IWorkspaceDocument*",
			"The view contract: the interfaces a host implements once, and every Coordination type reaches a view through them."
		),

		(
			"Nickelony.IDEKit.Workspace.Views.WorkspaceDocumentTextViewBase",
			"The view contract's implementation for an editor: a host derives its text view from it instead of implementing the contract."
		),

		(
			"Nickelony.IDEKit.Workspace.Views.WorkspaceDocumentView*",
			"The view contract: the results, outcomes and identity changes that a view exchanges with every Coordination type."
		),

		(
			"Nickelony.IDEKit.Workspace.Views.WorkspaceViewOperationFailureCodes",
			"The failure codes of the view contract: a view reports them, and every Coordination type reports them for a view."
		),

		(
			"Nickelony.IDEKit.Workspace.WorkspaceOperationFailure",
			"Every Workspace namespace reports a failure as one of these records, and the name carries the package concept."
		),

		(
			"Nickelony.IDEKit.Workspace.WorkspaceOperationFailureCodes",
			"Every Workspace namespace reports a failure with one of these codes, and the name carries the package concept."
		),

		(
			"Nickelony.IDEKit.Workspace.WorkspaceOptions",
			"One value serves every Workspace component that compares document identities, and the name carries the package concept."
		)
	);

	[TestMethod]
	public void APublicTypeInTheRootNamespaceOfAPackageWithFeatureNamespacesIsListed()
	{
		List<string> violations = [];

		foreach (
			ProjectFile project in RepositoryModel.SourceProjects.Where(candidate =>
				candidate.RelativePath.StartsWith("src/IDEKit/", StringComparison.Ordinal)
			)
		)
		{
			bool hasFeatureNamespaces = false;
			List<string> rootTypes = [];

			foreach (string path in project.Sources)
			{
				SyntaxNode root = RepositoryModel.Source(path).Tree([.. project.DefineConstants]).GetRoot();

				foreach (BaseNamespaceDeclarationSyntax declaration in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
				{
					string name = declaration.Name.ToString();

					if (name.StartsWith(project.Name + ".", StringComparison.Ordinal))
						hasFeatureNamespaces = true;
					else if (name == project.Name)
						rootTypes.AddRange(PublicTypeNames(declaration));
				}
			}

			if (!hasFeatureNamespaces)
				continue;

			foreach (string type in rootTypes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
			{
				string qualified = project.Name + "." + type;

				if (!s_rootTypes.Allows(qualified))
				{
					violations.Add(
						s_rootTypes.Rejection($"{qualified} is public in the root namespace of a package with feature namespaces.", Guard)
					);
				}
			}
		}

		violations.AddRange(s_rootTypes.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Root-namespace types:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	private static IEnumerable<string> PublicTypeNames(BaseNamespaceDeclarationSyntax declaration)
	{
		foreach (MemberDeclarationSyntax member in declaration.Members)
		{
			switch (member)
			{
				case BaseTypeDeclarationSyntax type when type.Modifiers.Any(SyntaxKind.PublicKeyword):
					yield return type.Identifier.ValueText;
					break;

				case DelegateDeclarationSyntax @delegate when @delegate.Modifiers.Any(SyntaxKind.PublicKeyword):
					yield return @delegate.Identifier.ValueText;
					break;
			}
		}
	}
}
