using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// A log event id lies in the numeric block that the architecture guide gives its package, and no two events share an id, so an id identifies
/// one event across the packages.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed partial class EventIdGuardTests
{
	// The words that a path carries for each package that owns a block, in the order the guide lists them.
	private static readonly string[] s_packageWords = ["LanguageFeatures", "Markdown", "TextMate", "KeyBindings"];

	[TestMethod]
	public void EveryEventIdLiesInTheBlockOfItsPackageAndIsUnique()
	{
		List<(string Words, int First, int Last)> blocks = ReadBlocks();
		Dictionary<int, string> seen = [];
		List<string> violations = [];

		foreach (string words in blocks.Select(block => block.Words))
		{
			if (!s_packageWords.Any(word => words.Contains(word, StringComparison.Ordinal)))
				violations.Add($"The block of '{words}' names none of the packages the guard knows: {string.Join(", ", s_packageWords)}.");
		}

		for (int index = 0; index < blocks.Count; index++)
		{
			for (int other = index + 1; other < blocks.Count; other++)
			{
				if (blocks[index].First <= blocks[other].Last && blocks[other].First <= blocks[index].Last)
					violations.Add($"The blocks {blocks[index].First}-{blocks[index].Last} and {blocks[other].First}-{blocks[other].Last} overlap.");
			}
		}

		int idCount = 0;

		foreach (string path in RepositoryModel.SourcePathsUnder("src", "shared"))
		{
			foreach (Match match in EventIdAssignment().Matches(RepositoryModel.Source(path).Text))
			{
				int id = int.Parse(match.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture);
				string word = s_packageWords.FirstOrDefault(candidate => path.Contains(candidate, StringComparison.Ordinal)) ?? string.Empty;

				(string Words, int First, int Last) block = blocks.FirstOrDefault(candidate =>
					candidate.Words.Contains(word, StringComparison.Ordinal) && word.Length > 0
				);

				idCount++;

				if (block.Words is null)
					violations.Add($"{path}: the event id {id} belongs to a package with no block in the architecture guide.");
				else if (id < block.First || id > block.Last)
					violations.Add($"{path}: the event id {id} lies outside the block {block.First}-{block.Last} of its package.");

				if (!seen.TryAdd(id, path))
					violations.Add($"{path}: the event id {id} is also used in {seen[id]}.");
			}
		}

		Assert.IsGreaterThan(0, idCount, "The guard read no event id from src or shared, so it checks nothing.");
		Assert.IsEmpty(violations, "Log event ids:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	/// <summary>The rows of the block table of the architecture guide: the package words of the row and the first and last id of its block.</summary>
	private static List<(string Words, int First, int Last)> ReadBlocks()
	{
		string[] lines = File.ReadAllLines(RepositoryPaths.Below("docs", "ArchitectureGuide.md"));
		int start = Array.FindIndex(lines, line => line == "## Log event ids");

		Assert.IsGreaterThanOrEqualTo(0, start, "docs/ArchitectureGuide.md holds no '## Log event ids' section.");

		List<(string Words, int First, int Last)> blocks = [];

		foreach (string line in lines.Skip(start + 1).Where(line => line.StartsWith('|')))
		{
			Match row = BlockRow().Match(line);

			if (row.Success)
			{
				blocks.Add(
					(
						row.Groups["words"].Value,
						int.Parse(row.Groups["first"].Value, System.Globalization.CultureInfo.InvariantCulture),
						int.Parse(row.Groups["last"].Value, System.Globalization.CultureInfo.InvariantCulture)
					)
				);
			}
		}

		Assert.IsNotEmpty(blocks, "The 'Log event ids' section of docs/ArchitectureGuide.md holds no block row such as '| Packages | 1000-1031 |'.");

		return blocks;
	}

	[GeneratedRegex(@"\bEventId\s*=\s*(?<id>\d+)", RegexOptions.CultureInvariant)]
	private static partial Regex EventIdAssignment();

	[GeneratedRegex(@"^\|\s*(?<words>[^|]+?)\s*\|\s*(?<first>\d+)-(?<last>\d+)\s*\|", RegexOptions.CultureInvariant)]
	private static partial Regex BlockRow();
}
