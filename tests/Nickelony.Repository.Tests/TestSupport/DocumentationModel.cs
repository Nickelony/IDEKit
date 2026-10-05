using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Nickelony.Repository.Tests;

/// <summary>Reads the prose the repository owns: the files and comments that hold it, and the runs, sentences and phrases inside them.</summary>
/// <remarks>
/// The guard tests hold the rules (the banned phrases, the spellings and the aims); this type answers where the prose is and what it contains.
/// </remarks>
internal static partial class DocumentationModel
{
	// Files that state the rules rather than follow them - AGENTS.md and the guard class, whose word lists ARE
	// the patterns. The prose scans skip them, because each would read the rules it states as violations.
	private static readonly string[] s_ruleSourceExclusions = ["AGENTS.md", "tests/Nickelony.Repository.Tests/DocumentationGuardTests.cs"];

	// Generated output and scratch space hold no docs. DocFX writes its API reference and site under docs.
	private static readonly string[] s_generatedPaths = ["docs/_site", "docs/api", ".codebase-memory", "temp"];

	/// <summary>A fence marker: an opener or a closer, counted by toggling.</summary>
	[GeneratedRegex(@"^\s*(?:`{3,}|~{3,})", RegexOptions.CultureInvariant)]
	private static partial Regex Fence();

	/// <summary>An ATX heading, which is a title rather than prose.</summary>
	[GeneratedRegex(@"^\s*#{1,6}\s", RegexOptions.CultureInvariant)]
	private static partial Regex AtxHeading();

	/// <summary>A table row, which carries data rather than prose.</summary>
	[GeneratedRegex(@"^\s*\|", RegexOptions.CultureInvariant)]
	private static partial Regex TableRow();

	/// <summary>A readme badge row: an image link, not a sentence.</summary>
	[GeneratedRegex(@"^\s*(?:\[!\[|!\[)", RegexOptions.CultureInvariant)]
	private static partial Regex BadgeLine();

	/// <summary>A list item marker, ordered or not.</summary>
	[GeneratedRegex(@"^\s*(?:[-*+]|\d+[.)])\s+", RegexOptions.CultureInvariant)]
	private static partial Regex ListItem();

	/// <summary>A line indented against the left margin, continuing the item above it.</summary>
	[GeneratedRegex(@"^[ \t]", RegexOptions.CultureInvariant)]
	private static partial Regex Indented();

	/// <summary>A code span, which counts as one word.</summary>
	[GeneratedRegex(@"`+[^`]+`+", RegexOptions.CultureInvariant)]
	private static partial Regex CodeSpan();

	/// <summary>The target of a link, which is no word.</summary>
	[GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)", RegexOptions.CultureInvariant)]
	private static partial Regex LinkTarget();

	[GeneratedRegex(@"https?://\S+", RegexOptions.CultureInvariant)]
	private static partial Regex BareUrl();

	/// <summary>The marker that opens a comment or a list line, which is not part of its prose.</summary>
	[GeneratedRegex(@"^\s*(?:///?|#|\*)", RegexOptions.CultureInvariant)]
	private static partial Regex CommentMarker();

	/// <summary>A tag that closes itself, which stands for one word.</summary>
	[GeneratedRegex(@"<[A-Za-z][^<>]*/>", RegexOptions.CultureInvariant)]
	private static partial Regex SelfClosingTag();

	[GeneratedRegex(@"</?[A-Za-z][^<>]*>", RegexOptions.CultureInvariant)]
	private static partial Regex MarkupTag();

	/// <summary>A sentence ends at a mark that whitespace follows.</summary>
	[GeneratedRegex(@"(?<=[.!?])\s+", RegexOptions.CultureInvariant)]
	private static partial Regex SentenceEnd();

	/// <summary>Finds the banned phrases in every page, source comment, config comment and external member doc.</summary>
	/// <param name="pagePhrases">The phrases a page may not use.</param>
	/// <param name="commentPhrases">The phrases a comment may not use.</param>
	/// <returns>One line per hit, naming the file, the line, the phrase and the text.</returns>
	internal static List<string> PhraseViolations((string Name, Regex Pattern)[] pagePhrases, (string Name, Regex Pattern)[] commentPhrases)
	{
		List<string> violations = [];

		void AddCommentPhraseHits(string path, IEnumerable<(string Text, int Line)> lines)
		{
			if (s_ruleSourceExclusions.Contains(RepoPath(path), StringComparer.Ordinal))
				return;

			foreach ((string name, int line, string text) in PhraseHits(JoinedRuns(lines), commentPhrases))
				violations.Add($"{RepoPath(path)}:{line}: {name} in \"{text}\"");
		}

		foreach (string page in DocumentationFiles())
		{
			string path = RepoPath(page);

			if (s_ruleSourceExclusions.Contains(path, StringComparer.Ordinal))
				continue;

			foreach ((string name, int line, string text) in PhraseHits(JoinedRuns(Lines(File.ReadAllLines(page))), pagePhrases))
				violations.Add($"{path}:{line}: {name} in \"{text}\"");
		}

		foreach (string file in SourceFiles())
		{
			string path = RepoPath(file);

			if (s_ruleSourceExclusions.Contains(path, StringComparer.Ordinal))
				continue;

			// The scan reads comment trivia only, so the sample code a fixture holds in a string literal is not scanned,
			// and the comments of every project are.
			HashSet<int> commentLines = CommentLines(file, SyntaxKind.SingleLineDocumentationCommentTrivia, SyntaxKind.SingleLineCommentTrivia);

			foreach (
				(string name, int line, string text) in PhraseHits(
					JoinedRuns(Lines(File.ReadAllLines(file)).Where(line => commentLines.Contains(line.Line))),
					commentPhrases
				)
			)
			{
				violations.Add($"{path}:{line}: {name} in \"{text}\"");
			}
		}

		foreach (string file in XmlCommentFiles())
			AddCommentPhraseHits(file, XmlCommentLines(file));

		foreach (string file in HashCommentFiles())
			AddCommentPhraseHits(file, HashCommentLines(File.ReadAllLines(file)));

		foreach (string file in ExternalMemberDocs())
			AddCommentPhraseHits(file, Lines(File.ReadAllLines(file)));

		return violations;
	}

	/// <summary>Finds the spellings that a pattern matches in every text file the repository owns.</summary>
	/// <param name="spelling">The pattern of the spellings to find.</param>
	/// <returns>One line per hit, naming the file, the line, the spelling and the text.</returns>
	internal static List<string> SpellingViolations(Regex spelling)
	{
		List<string> violations = [];

		foreach (string file in SpellingFiles())
		{
			string path = RepoPath(file);

			if (s_ruleSourceExclusions.Contains(path, StringComparer.Ordinal))
				continue;

			foreach ((string text, int line) in Lines(File.ReadAllLines(file)))
			{
				Match match = spelling.Match(text);

				if (match.Success)
					violations.Add($"{path}:{line}: {match.Value} in \"{text.Trim()}\"");
			}
		}

		return violations;
	}

	internal static List<string> ParagraphAndListItemRunsPastAim(int paragraphAim, int itemAim)
	{
		List<string> runs = [];

		foreach (string page in DocumentationFiles())
			runs.AddRange(LongBodyRuns(page, paragraphAim, itemAim));

		return runs;
	}

	internal static List<string> SentencesPastAim(int aim)
	{
		List<string> violations = [];

		foreach (string page in DocumentationFiles())
		{
			string path = RepoPath(page);

			if (s_ruleSourceExclusions.Contains(path, StringComparer.Ordinal))
				continue;

			string[] lines = File.ReadAllLines(page);

			foreach ((_, int first, int last) in BodyRuns(lines))
				AddSentenceHits(violations, path, line: first, text: RunText(lines, first, last), aim);
		}

		foreach (string file in SourceFiles())
		{
			string path = RepoPath(file);

			if (s_ruleSourceExclusions.Contains(path, StringComparer.Ordinal))
				continue;

			HashSet<int> commentLines = CommentLines(file, SyntaxKind.SingleLineDocumentationCommentTrivia, SyntaxKind.SingleLineCommentTrivia);

			AddCommentSentenceHits(
				violations,
				path,

				commentLines: Lines(File.ReadAllLines(file))
					.Where(line => commentLines.Contains(line.Line))
					.Select(line => (Text: line.Text.Trim().TrimStart('/'), line.Line)),

				aim
			);
		}

		foreach (string file in XmlCommentFiles())
		{
			string path = RepoPath(file);

			if (s_ruleSourceExclusions.Contains(path, StringComparer.Ordinal))
				continue;

			AddCommentSentenceHits(
				violations,
				path,

				commentLines: XmlCommentLines(file)
					.Select(line => (Text: line.Text.Replace("<!--", string.Empty).Replace("-->", string.Empty), line.Line)),

				aim
			);
		}

		foreach (string file in HashCommentFiles())
		{
			string path = RepoPath(file);

			if (s_ruleSourceExclusions.Contains(path, StringComparer.Ordinal))
				continue;

			AddCommentSentenceHits(
				violations,
				path,
				commentLines: HashCommentLines(File.ReadAllLines(file)).Select(line => (Text: line.Text.TrimStart().TrimStart('#'), line.Line)),
				aim
			);
		}

		foreach (string file in ExternalMemberDocs())
		{
			string path = RepoPath(file);

			if (s_ruleSourceExclusions.Contains(path, StringComparer.Ordinal))
				continue;

			AddCommentSentenceHits(violations, path, commentLines: Lines(File.ReadAllLines(file)), aim);
		}

		return violations;
	}

	internal static List<string> SourceDocBlocksPastAim(int blockAim)
	{
		List<string> violations = [];

		foreach (string file in SourceFiles())
		{
			string path = RepoPath(file);

			foreach ((int first, int length) in LineRuns(CommentLines(file, SyntaxKind.SingleLineDocumentationCommentTrivia)))
			{
				if (length > blockAim)
					violations.Add($"{path}: {length}-line /// block at line {first}, aim {blockAim} lines");
			}
		}

		return violations;
	}

	internal static List<string> InlineCommentRunsPastAim(int aim)
	{
		List<string> violations = [];

		foreach (string file in SourceFiles())
		{
			string path = RepoPath(file);

			foreach ((int first, int length) in LineRuns(CommentLines(file, SyntaxKind.SingleLineCommentTrivia)))
			{
				if (length > aim)
					violations.Add($"{path}: {length}-line // run at line {first}, aim {aim} lines");
			}
		}

		return violations;
	}

	private static string[] DocumentationFiles() =>
		[.. Walk(RepositoryPaths.Root(), "*.md", SearchOption.AllDirectories).OrderBy(file => file, StringComparer.Ordinal)];

	private static string[] SourceFiles()
	{
		return
		[
			.. new[] { "src", "samples", "shared", "tests" }
				.SelectMany(folder => Walk(RepositoryPaths.Below(folder), "*.cs", SearchOption.AllDirectories))
				.OrderBy(file => file, StringComparer.Ordinal),
		];
	}

	// AGENTS.md counts a comment in a project, config or script file as a doc, so the phrase scan reads
	// the comment lines of those files too. An XML config comment is a <!-- --> block; a script or
	// editorconfig comment is a # line.
	private static string[] XmlCommentFiles()
	{
		return
		[
			.. Walk(RepositoryPaths.Root(), "*.csproj", SearchOption.AllDirectories),
			.. Walk(RepositoryPaths.Root(), "*.props", SearchOption.AllDirectories),
			.. Walk(RepositoryPaths.Root(), "*.targets", SearchOption.AllDirectories),
			.. Walk(RepositoryPaths.Root(), "*.slnx", SearchOption.AllDirectories),
			.. Walk(RepositoryPaths.Root(), "*.runsettings", SearchOption.AllDirectories),
			.. Walk(RepositoryPaths.Root(), "NuGet.Config", SearchOption.AllDirectories),
		];
	}

	// The GitHub .gitignore template is vendored, so its comments are not read as docs.
	private static string[] HashCommentFiles()
	{
		return
		[
			.. Walk(RepositoryPaths.Root(), "*.ps1", SearchOption.AllDirectories),
			.. Walk(RepositoryPaths.Root(), "*.yml", SearchOption.AllDirectories),
			.. Walk(RepositoryPaths.Root(), ".editorconfig", SearchOption.AllDirectories),
		];
	}

	// The external member docs under shared/docs are XML that carries prose inside its elements.
	private static string[] ExternalMemberDocs() =>
		[.. Walk(RepositoryPaths.Below("shared", "docs"), "*.xml", SearchOption.AllDirectories).OrderBy(file => file, StringComparer.Ordinal)];

	private static IEnumerable<(string Text, int Line)> HashCommentLines(string[] lines) =>
		Lines(lines).Where(line => line.Text.TrimStart().StartsWith('#'));

	// Every text file the repository owns, read in full: a page, a source file, an external member doc, or
	// a project, config or script. A British spelling in an identifier or a string is still one.
	private static string[] SpellingFiles()
	{
		string[] files = [.. DocumentationFiles(), .. SourceFiles(), .. ExternalMemberDocs(), .. XmlCommentFiles(), .. HashCommentFiles()];
		return [.. files.Distinct(StringComparer.Ordinal).OrderBy(file => file, StringComparer.Ordinal)];
	}

	private static IEnumerable<(string Text, int Line)> XmlCommentLines(string path)
	{
		bool inside = false;

		foreach ((string text, int line) in Lines(File.ReadAllLines(path)))
		{
			bool opens = text.Contains("<!--", StringComparison.Ordinal);

			if (inside || opens)
				yield return (text, line);

			if (opens)
				inside = true;

			if (inside && text.Contains("-->", StringComparison.Ordinal))
				inside = false;
		}
	}

	private static IEnumerable<string> Walk(string root, string pattern, SearchOption option)
	{
		if (option == SearchOption.TopDirectoryOnly)
		{
			foreach (string file in Directory.EnumerateFiles(root, pattern))
				yield return file;

			yield break;
		}

		Stack<string> directories = new();
		directories.Push(root);

		while (directories.Count > 0)
		{
			string directory = directories.Pop();

			foreach (string child in Directory.EnumerateDirectories(directory))
			{
				string name = Path.GetFileName(child);

				if (name is ".git" or ".vs" or "bin" or "obj" or "artifacts" or "node_modules")
					continue;

				if (s_generatedPaths.Contains(RepoPath(child), StringComparer.Ordinal))
					continue;

				directories.Push(child);
			}

			foreach (string file in Directory.EnumerateFiles(directory, pattern))
				yield return file;
		}
	}

	private static IEnumerable<string> LongBodyRuns(string file, int paragraphAim, int itemAim)
	{
		string path = RepoPath(file);

		if (s_ruleSourceExclusions.Contains(path, StringComparer.Ordinal))
			yield break;

		string[] lines = File.ReadAllLines(file);

		foreach ((string kind, int first, int last) in BodyRuns(lines))
		{
			int words = CountWords(lines, first, last);
			int aim = kind == "paragraph" ? paragraphAim : itemAim;

			if (words > aim)
				yield return $"{path}: {kind} of {words} words, aim {aim} (line {first})";
		}
	}

	// Words rather than lines, so the aim holds however a page is wrapped; a list marker is no word.
	private static int CountWords(string[] lines, int first, int last) => WordCount(RunText(lines, first, last));

	private static int WordCount(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

	// The text of a run, joined across its lines, with the marker of a list item left out.
	private static string RunText(string[] lines, int first, int last)
	{
		List<string> parts = [];

		for (int line = first; line <= last; line++)
		{
			string text = lines[line - 1];
			parts.Add(line == first ? ListItem().Replace(text, string.Empty, 1) : text);
		}

		return string.Join(" ", parts);
	}

	private static void AddSentenceHits(List<string> violations, string path, int line, string text, int aim)
	{
		foreach (string sentence in Sentences(text))
		{
			int words = WordCount(sentence);

			if (words > aim)
				violations.Add($"{path}: sentence of {words} words, aim {aim} (line {line}), starting \"{Preview(sentence)}\"");
		}
	}

	// The comment lines of one file, grouped into runs the way a paragraph is, so a sentence that wraps
	// is counted whole.
	private static void AddCommentSentenceHits(List<string> violations, string path, IEnumerable<(string Text, int Line)> commentLines, int aim)
	{
		var textByLine = commentLines.ToDictionary(line => line.Line, line => line.Text);

		foreach ((int first, int length) in LineRuns(textByLine.Keys))
		{
			string text = string.Join(" ", Enumerable.Range(first, length).Select(line => textByLine[line]));
			AddSentenceHits(violations, path, line: first, text, aim);
		}
	}

	// Each code span and self-closing tag stands for one word, a link counts its text, and a URL or a
	// tag counts for nothing, so a dot inside code cannot end a sentence.
	private static IEnumerable<string> Sentences(string text)
	{
		string prose = CodeSpan().Replace(text, " code ");
		prose = LinkTarget().Replace(prose, "$1");
		prose = BareUrl().Replace(prose, " ");
		prose = SelfClosingTag().Replace(prose, " code ");
		prose = MarkupTag().Replace(prose, " ");
		prose = prose.Replace("*", string.Empty);

		foreach (string sentence in SentenceEnd().Split(prose))
		{
			if (sentence.Trim().Length > 0)
				yield return sentence;
		}
	}

	private static string Preview(string sentence) => string.Join(" ", sentence.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(6));

	private static List<(string Kind, int First, int Last)> BodyRuns(string[] lines)
	{
		List<(string Kind, int First, int Last)> runs = [];
		string kind = string.Empty;

		int first = 0;
		int last = 0;

		bool fenced = false;

		foreach ((string line, int index) in Lines(lines))
		{
			bool body =
				!Fence().IsMatch(line)
				&& !fenced
				&& !string.IsNullOrWhiteSpace(line)
				&& !AtxHeading().IsMatch(line)
				&& !TableRow().IsMatch(line)
				&& !BadgeLine().IsMatch(line);

			if (Fence().IsMatch(line))
				fenced = !fenced;

			if (!body)
			{
				if (kind.Length > 0)
					runs.Add((kind, first, last));

				kind = string.Empty;
				continue;
			}

			if (kind == "list item" && ListItem().IsMatch(line))
			{
				runs.Add((kind, first, last));
				first = index;
				last = index;
			}
			else if (kind == "list item" && Indented().IsMatch(line))
			{
				last = index;
			}
			else if (kind == "paragraph" && !ListItem().IsMatch(line))
			{
				last = index;
			}
			else
			{
				if (kind.Length > 0)
					runs.Add((kind, first, last));

				kind = ListItem().IsMatch(line) ? "list item" : "paragraph";
				first = index;
				last = index;
			}
		}

		if (kind.Length > 0)
			runs.Add((kind, first, last));

		return runs;
	}

	/// <summary>The lines of the comments of the given trivia kinds that start their line; strings and disabled text are not comments.</summary>
	private static HashSet<int> CommentLines(string path, params SyntaxKind[] kinds)
	{
		var source = SourceText.From(File.ReadAllText(path));

		SyntaxTree tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview), path);

		HashSet<int> lines = [];

		foreach (SyntaxTrivia trivia in tree.GetRoot().DescendantTrivia())
		{
			if (Array.IndexOf(kinds, trivia.Kind()) < 0 || !StartsLine(source, trivia))
				continue;

			int start = source.Lines.IndexOf(trivia.FullSpan.Start) + 1;
			int last = trivia.FullSpan.End;

			// A doc comment's span reaches past its last line; the walk takes the ending back to the
			// last character the comment itself holds.
			while (last > trivia.FullSpan.Start && char.IsWhiteSpace(source[last - 1]))
				last--;

			int end = source.Lines.IndexOf(last - 1) + 1;

			for (int line = start; line <= end; line++)
				lines.Add(line);
		}

		return lines;
	}

	// True when the comment is the first text on its line, so a comment that trails code is skipped. The test
	// compares against FullSpan because the `///` markers sit in a doc comment's FullSpan but outside its own span.
	private static bool StartsLine(SourceText source, SyntaxTrivia trivia)
	{
		TextLine line = source.Lines.GetLineFromPosition(trivia.SpanStart);
		int pointer = line.Start;

		while (pointer < line.End && char.IsWhiteSpace(source[pointer]))
			pointer++;

		return pointer >= trivia.FullSpan.Start && pointer < trivia.FullSpan.End;
	}

	private static IEnumerable<(int First, int Length)> LineRuns(IEnumerable<int> lines)
	{
		int[] sorted = [.. lines.OrderBy(line => line)];

		int index = 0;

		while (index < sorted.Length)
		{
			int first = sorted[index];
			int last = first;

			index++;

			while (index < sorted.Length && sorted[index] == last + 1)
			{
				last = sorted[index];
				index++;
			}

			yield return (first, last - first + 1);
		}
	}

	private static IEnumerable<(string Text, int Line)> Lines(string[] lines) => lines.Select((text, offset) => (text, offset + 1));

	private static IEnumerable<(string Name, int Line, string Text)> PhraseHits(
		IEnumerable<(string Text, int Line)> lines,
		(string Name, Regex Pattern)[] phrases
	)
	{
		foreach ((string text, int line) in lines)
		{
			foreach ((string name, Regex pattern) in phrases)
			{
				if (pattern.IsMatch(text))
					yield return (name, line, text.Trim());
			}
		}
	}

	// A phrase can break across a line, so each run of consecutive prose lines is scanned as one text. A
	// blank line, or a line that holds only a marker, ends a run.
	private static IEnumerable<(string Text, int Line)> JoinedRuns(IEnumerable<(string Text, int Line)> lines)
	{
		Dictionary<int, string> textByLine = [];

		foreach ((string text, int line) in lines)
		{
			string prose = CommentMarker().Replace(text, " ", 1).Trim();

			if (prose.Length > 0)
				textByLine[line] = prose;
		}

		foreach ((int first, int length) in LineRuns(textByLine.Keys))
			yield return (string.Join(" ", Enumerable.Range(first, length).Select(line => textByLine[line])), first);
	}

	private static string RepoPath(string path) => Path.GetRelativePath(RepositoryPaths.Root(), path).Replace('\\', '/');
}
