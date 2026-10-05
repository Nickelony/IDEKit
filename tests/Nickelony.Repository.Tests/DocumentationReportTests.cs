using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>Lists the documentation runs that pass a length aim of AGENTS.md, for a reviewer to read. It never fails.</summary>
[TestClass]
[TestCategory(TestCategories.RepositoryReport)]
public sealed partial class DocumentationReportTests
{
	public TestContext TestContext { get; set; } = null!;

	/// <summary>Reads the paragraph word aim from AGENTS.md.</summary>
	[GeneratedRegex(@"A paragraph aims for at most (?<limit>\d+) words", RegexOptions.CultureInvariant)]
	private static partial Regex ParagraphAim();

	/// <summary>Reads the list-item word aim from AGENTS.md.</summary>
	[GeneratedRegex(@"A list item aims for at most (?<limit>\d+) words", RegexOptions.CultureInvariant)]
	private static partial Regex ListItemAim();

	/// <summary>Reads the sentence length that is worth splitting from AGENTS.md.</summary>
	[GeneratedRegex(@"one past (?<limit>\d+) is worth splitting", RegexOptions.CultureInvariant)]
	private static partial Regex SentenceAim();

	/// <summary>Reads the XML doc block line aim from AGENTS.md.</summary>
	[GeneratedRegex(@"An XML doc block aims for at most (?<limit>\d+)", RegexOptions.CultureInvariant)]
	private static partial Regex XmlDocBlockAim();

	/// <summary>Reads the inline comment run line aim from AGENTS.md.</summary>
	[GeneratedRegex(@"inline comment run aims for at most (?<limit>\d+)", RegexOptions.CultureInvariant)]
	private static partial Regex InlineCommentAim();

	[TestMethod]
	public void ReportsRunsPastTheAims()
	{
		// The aims are guidance. A protocol term can keep a run past them, so the report lists the runs and
		// the run never fails. Read it with detailed verbosity.
		List<string> paragraphs = DocumentationModel.ParagraphAndListItemRunsPastAim(
			paragraphAim: ShapeAim(ParagraphAim(), "paragraph"),
			itemAim: ShapeAim(ListItemAim(), "list item")
		);

		List<string> sentences = DocumentationModel.SentencesPastAim(ShapeAim(SentenceAim(), "sentence"));
		List<string> docBlocks = DocumentationModel.SourceDocBlocksPastAim(ShapeAim(XmlDocBlockAim(), "XML doc block"));
		List<string> comments = DocumentationModel.InlineCommentRunsPastAim(ShapeAim(InlineCommentAim(), "inline comment run"));

		Report("Paragraphs and list items", paragraphs);
		Report("Sentences", sentences);
		Report("XML doc blocks", docBlocks);
		Report("Inline comment runs", comments);
	}

	private void Report(string title, List<string> runs)
	{
		TestContext.WriteLine($"{title} past an aim: {runs.Count}");

		foreach (string run in runs)
			TestContext.WriteLine(run);
	}

	private static int ShapeAim(Regex pattern, string name)
	{
		Match match = pattern.Match(File.ReadAllText(RepositoryPaths.Below("AGENTS.md")));

		Assert.IsTrue(match.Success, $"AGENTS.md does not state the {name} aim.");

		return int.Parse(match.Groups["limit"].Value, CultureInfo.InvariantCulture);
	}
}
