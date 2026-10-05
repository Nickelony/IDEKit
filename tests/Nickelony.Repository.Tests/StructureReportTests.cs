using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>Structure measures that guide a reader and never fail a run. Read them with detailed verbosity.</summary>
[TestClass]
[TestCategory(TestCategories.RepositoryReport)]
public sealed class StructureReportTests
{
	// A folder past this many source files is worth a look; a protocol or a feature can keep one past it.
	private const int FolderFileAim = 25;

	// A shared file whose conditional blocks inside member bodies pass this many lines is a candidate for an engine-suffixed partial file.
	private const int BodyConditionalLineAim = 15;

	public TestContext TestContext { get; set; } = null!;

	[TestMethod]
	public void ReportsFoldersPastTheSourceFileAim()
	{
		IEnumerable<IGrouping<string, string>> byFolder = RepositoryModel
			.SourcePathsUnder("src", "shared", "tests/IDEKit/Shared", "tests/QuickPick/Shared")
			.GroupBy(path => path[..path.LastIndexOf('/')], StringComparer.Ordinal);

		List<(string Folder, int Files)> folders =
		[
			.. byFolder
				.Select(group => (Folder: group.Key, Files: group.Count()))
				.Where(folder => folder.Files > FolderFileAim)
				.OrderByDescending(folder => folder.Files)
				.ThenBy(folder => folder.Folder, StringComparer.Ordinal),
		];

		TestContext.WriteLine($"Folders with more than {FolderFileAim} source files: {folders.Count}");

		foreach ((string folder, int files) in folders)
			TestContext.WriteLine($"{files, 4}  {folder}");
	}

	[TestMethod]
	public void ReportsSharedFilesWithLongBodyConditionalBlocks()
	{
		List<string> shared =
		[
			.. RepositoryModel
				.SourcePathsUnder("shared", "tests/IDEKit/Shared", "tests/QuickPick/Shared")
				.Where(path => RepositoryModel.SharedHomeOf(path)?.Kind is SharedHomeKind.Editor or SharedHomeKind.QuickPick),
		];

		List<(string Path, int Lines)> withBlocks = [];

		foreach (string path in shared)
		{
			int lines = BodyConditionalLines(RepositoryModel.Source(path));

			if (lines > 0)
				withBlocks.Add((path, lines));
		}

		List<(string Path, int Lines)> past =
		[
			.. withBlocks.Where(file => file.Lines > BodyConditionalLineAim).OrderByDescending(file => file.Lines),
		];

		int total = withBlocks.Sum(file => file.Lines);
		int most = withBlocks.Select(file => file.Lines).DefaultIfEmpty().Max();

		TestContext.WriteLine(
			$"{withBlocks.Count} of {shared.Count} shared editor source and test files hold {total} lines of conditional blocks inside member bodies."
		);

		TestContext.WriteLine($"{past.Count} pass the aim of {BodyConditionalLineAim} lines, and at most {most} sit in one file.");

		foreach ((string path, int lines) in past)
			TestContext.WriteLine($"{lines, 4}  {path}");
	}

	/// <summary>Counts the lines covered by outermost conditional blocks that start inside a method, accessor or lambda body.</summary>
	private static int BodyConditionalLines(SourceFile source)
	{
		SortedDictionary<int, int> ranges = [];

		foreach (string[] symbols in new[] { ["AVALONIAEDIT"], Array.Empty<string>() })
		{
			SyntaxTree tree = source.Tree(symbols);

			foreach (SyntaxTrivia trivia in tree.GetRoot().DescendantTrivia().Where(candidate => candidate.IsKind(SyntaxKind.IfDirectiveTrivia)))
			{
				if (
					trivia.GetStructure() is not IfDirectiveTriviaSyntax directive
					|| !directive.Condition.ToString().Contains("AVALONIAEDIT", StringComparison.Ordinal)
					|| !IsInsideBody(trivia)
				)
				{
					continue;
				}

				IReadOnlyList<DirectiveTriviaSyntax> related = directive.GetRelatedDirectives();

				int first = tree.GetLineSpan(directive.Span).StartLinePosition.Line;
				int last = tree.GetLineSpan(related[^1].Span).EndLinePosition.Line;

				ranges[first] = Math.Max(ranges.GetValueOrDefault(first), last);
			}
		}

		int total = 0;
		int covered = -1;

		foreach ((int first, int last) in ranges)
		{
			int start = Math.Max(first, covered + 1);

			if (last >= start)
				total += last - start + 1;

			covered = Math.Max(covered, last);
		}

		return total;
	}

	/// <summary>
	/// Reports whether the directive sits strictly inside a member that is not a type or a namespace, so a directive that precedes a member
	/// does not count.
	/// </summary>
	private static bool IsInsideBody(SyntaxTrivia trivia)
	{
		if (trivia.Token.Parent is not { } parent)
			return false;

		return parent.AncestorsAndSelf().Any(node => IsBodyOwner(node) && node.Span.Contains(trivia.SpanStart));
	}

	private static bool IsBodyOwner(SyntaxNode node) =>
		node is MemberDeclarationSyntax and not (BaseTypeDeclarationSyntax or BaseNamespaceDeclarationSyntax or DelegateDeclarationSyntax);
}
