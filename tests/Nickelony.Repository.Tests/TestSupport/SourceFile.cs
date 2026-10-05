using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Nickelony.Repository.Tests;

/// <summary>A C# source file of the repository, with its syntax trees parsed once per set of preprocessor symbols.</summary>
internal sealed partial class SourceFile
{
	private readonly Dictionary<string, SyntaxTree> _trees = [];

	internal SourceFile(string relativePath, string text)
	{
		RelativePath = relativePath;
		Text = text;
	}

	/// <summary>The path below the repository root, with forward slashes.</summary>
	internal string RelativePath { get; }

	internal string Text { get; }

	/// <summary>Parses the file with the given preprocessor symbols defined, so a conditional block selects one branch.</summary>
	internal SyntaxTree Tree(params string[] symbols)
	{
		string key = string.Join(';', symbols.Order(StringComparer.Ordinal));

		lock (_trees)
		{
			if (!_trees.TryGetValue(key, out SyntaxTree? tree))
			{
				CSharpParseOptions options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest).WithPreprocessorSymbols(symbols);
				tree = CSharpSyntaxTree.ParseText(Text, options, RelativePath);
				_trees[key] = tree;
			}

			return tree;
		}
	}

	/// <summary>
	/// The syntax trees that together read every branch of the file's conditional blocks:
	/// under the project's own symbols, under none, and under every symbol the file tests, so a declaration inside a block that the project
	/// does not enable is not missed.
	/// </summary>
	internal IReadOnlyList<SyntaxTree> TreesOfEveryBranch(IReadOnlyList<string> projectSymbols)
	{
		SyntaxTree projectTree = Tree([.. projectSymbols]);

		if (!Text.Contains("#if", StringComparison.Ordinal))
			return [projectTree];

		string[] testedSymbols =
		[
			.. ConditionalDirectiveRegex()
				.Matches(Text)
				.SelectMany(match => IdentifierRegex().Matches(match.Groups["condition"].Value).Select(identifier => identifier.Value))
				.Where(symbol => symbol is not ("true" or "false"))
				.Distinct(StringComparer.Ordinal)
				.Order(StringComparer.Ordinal),
		];

		return [.. new[] { projectTree, Tree(), Tree(testedSymbols) }.Distinct()];
	}

	/// <summary>The namespaces the file declares under the given symbols, in source order.</summary>
	internal IReadOnlyList<string> Namespaces(params string[] symbols) =>
		[.. Tree(symbols).GetRoot().DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().Select(declaration => declaration.Name.ToString())];

	[GeneratedRegex(@"^\s*#(?:el)?if\s+(?<condition>.+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
	private static partial Regex ConditionalDirectiveRegex();

	[GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*")]
	private static partial Regex IdentifierRegex();
}
