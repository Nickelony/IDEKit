using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nickelony.Repository.Tests;

/// <summary>The role words of the vocabulary table in <c>CONVENTIONS.md</c>, read once, so the guards and the table cannot disagree.</summary>
internal static partial class VocabularyTable
{
	private static readonly Lazy<Vocabulary> s_vocabulary = new(Load);

	/// <summary>The last words that a product type may end in: the table's suffix rows, its plain agent nouns and the framework words.</summary>
	internal static IReadOnlyList<string> RoleWords => s_vocabulary.Value.RoleWords;

	/// <summary>The last words that a product type never ends in.</summary>
	internal static IReadOnlyList<string> RetiredWords => s_vocabulary.Value.RetiredWords;

	/// <summary>The words that a test double begins with, such as <c>Fake</c> and <c>Recording</c>.</summary>
	internal static IReadOnlyList<string> TestDoublePrefixes => s_vocabulary.Value.TestDoublePrefixes;

	/// <summary>The suffixes of a test helper type, such as <c>TestHost</c>.</summary>
	internal static IReadOnlyList<string> TestSuffixes => s_vocabulary.Value.TestSuffixes;

	/// <summary>The last word of a type name, with the interface prefix removed.</summary>
	internal static string LastWord(string typeName)
	{
		string stem = typeName.Length > 1 && typeName[0] == 'I' && char.IsUpper(typeName[1]) ? typeName[1..] : typeName;
		Match last = LastWordPattern().Match(stem);

		return last.Success ? last.Value : stem;
	}

	private static Vocabulary Load()
	{
		string[] lines = File.ReadAllLines(RepositoryPaths.Below("CONVENTIONS.md"));
		int header = Array.FindIndex(lines, line => line.StartsWith("| Word | Meaning | Examples |", StringComparison.Ordinal));

		Assert.IsGreaterThanOrEqualTo(0, header, "CONVENTIONS.md holds no vocabulary table that starts with '| Word | Meaning | Examples |'.");

		// Words of types that derive from a framework type keep the framework's word.
		List<string> roleWords = ["Exception", "EventArgs", "Attribute"];
		List<string> prefixes = [];
		List<string> testSuffixes = [];

		foreach (string line in lines.Skip(header + 2).TakeWhile(line => line.StartsWith('|')))
		{
			string[] cells = line.Split('|', StringSplitOptions.TrimEntries);
			string[] first = [.. Tokens(cells[1])];

			if (cells[1].StartsWith("plain agent nouns", StringComparison.Ordinal))
			{
				roleWords.AddRange(Tokens(cells[3]));
				continue;
			}

			string[] suffixes = [.. first.Where(token => token.StartsWith("...", StringComparison.Ordinal)).Select(token => token[3..])];

			roleWords.AddRange(suffixes);
			testSuffixes.AddRange(suffixes.Where(suffix => suffix.StartsWith("Test", StringComparison.Ordinal) && suffix != "TestHooks"));
			prefixes.AddRange(first.Where(token => !token.StartsWith("...", StringComparison.Ordinal) && token.All(char.IsLetter)));
		}

		return new Vocabulary(
			RoleWords: [.. roleWords.Distinct(StringComparer.Ordinal)],
			RetiredWords: ReadRetiredWords(lines),
			TestDoublePrefixes: [.. prefixes],
			TestSuffixes: [.. testSuffixes]
		);
	}

	/// <summary>The words of the paragraph that starts "Retired words are not used in a type name", up to the blank line that ends it.</summary>
	private static string[] ReadRetiredWords(string[] lines)
	{
		int start = Array.FindIndex(lines, line => line.StartsWith("Retired words are not used in a type name", StringComparison.Ordinal));

		Assert.IsGreaterThanOrEqualTo(0, start, "CONVENTIONS.md holds no paragraph that starts 'Retired words are not used in a type name'.");

		string paragraph = string.Join(' ', lines.Skip(start).TakeWhile(line => line.Length > 0));

		return
		[
			.. Tokens(paragraph[..paragraph.IndexOf("A type derived", StringComparison.Ordinal)])
				.Where(token => token.All(char.IsLetter))
				.Distinct(StringComparer.Ordinal),
		];
	}

	private static IEnumerable<string> Tokens(string text) => Backticks().Matches(text).Select(match => match.Groups[1].Value);

	[GeneratedRegex("[A-Z][a-z0-9]*$", RegexOptions.CultureInvariant)]
	private static partial Regex LastWordPattern();

	[GeneratedRegex("`([^`]+)`", RegexOptions.CultureInvariant)]
	private static partial Regex Backticks();

	private sealed record Vocabulary(
		IReadOnlyList<string> RoleWords,
		IReadOnlyList<string> RetiredWords,
		IReadOnlyList<string> TestDoublePrefixes,
		IReadOnlyList<string> TestSuffixes
	);
}
