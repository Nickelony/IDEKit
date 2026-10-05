using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Markdown.Model;

namespace Nickelony.IDEKit.Markdown.Tests;

/// <summary>Pins the steps the Markdown renderers share around their emitters, driven with text trees so no toolkit element is needed.</summary>
[TestClass]
public sealed class MarkdownRenderPipelineTests
{
	[TestMethod]
	public void CreateContent_BlankContent_ReturnsTheEmptyFallbackWithoutParsing()
	{
		var emitted = new List<MarkdownDocumentModel>();

		string result = RenderContent("  \r\n\t ", model => RecordModel(emitted, model));

		Assert.AreEqual("fallback:", result);
		Assert.IsEmpty(emitted);
	}

	[TestMethod]
	public void CreateContent_Markdown_EmitsTheModelOfTheNormalizedContentIntoTheViewer()
	{
		var emitted = new List<MarkdownDocumentModel>();

		string result = RenderContent("first\r\n\r\nsecond", model => RecordModel(emitted, model));

		Assert.AreEqual("viewer:tree", result);
		Assert.HasCount(1, emitted);
		Assert.HasCount(2, emitted[0].Blocks);
	}

	[TestMethod]
	public void CreateContent_EmitFails_ReturnsTheFallbackOfTheNormalizedContentAndReportsTheFailure()
	{
		var logger = new RecordingLogger();

		string result = RenderContent(
			"a\r\nb",
			_ => throw new InvalidOperationException("emit failure"),
			new MarkdownRenderOptions { Logger = logger }
		);

		Assert.AreEqual("fallback:a\nb", result);

		RecordingLogger.LogEntry entry = logger.Entries.Single();

		Assert.AreEqual(2000, entry.EventId.Id);
		Assert.AreEqual(LogLevel.Warning, entry.Level);
	}

	[TestMethod]
	public void CreateContent_EmitFailsWithoutALogger_StillReturnsTheFallback() =>
		Assert.AreEqual("fallback:text", RenderContent("text", _ => throw new InvalidOperationException("emit failure")));

	[TestMethod]
	public void CreateContent_OutOfMemoryDuringEmit_PropagatesAndIsNotReported()
	{
		var logger = new RecordingLogger();
		var options = new MarkdownRenderOptions { Logger = logger };

		Assert.ThrowsExactly<OutOfMemoryException>(() => RenderContent("text", _ => throw new OutOfMemoryException(), options));
		Assert.IsEmpty(logger.Entries);
	}

	[TestMethod]
	public void CreateContent_CancellationDuringEmit_PropagatesAndIsNotReported()
	{
		var logger = new RecordingLogger();
		var options = new MarkdownRenderOptions { Logger = logger };

		Assert.ThrowsExactly<OperationCanceledException>(() => RenderContent("text", _ => throw new OperationCanceledException(), options));
		Assert.IsEmpty(logger.Entries);
	}

	[TestMethod]
	public void CreateContent_OptionsSchemes_DecideWhichLinksAreOpenable()
	{
		var emitted = new List<MarkdownDocumentModel>();
		var options = new MarkdownRenderOptions { SupportedHyperlinkSchemes = new HashSet<string> { "ftp" } };

		RenderContent("[a](ftp://host/file) [b](https://example.com)", model => RecordModel(emitted, model), options);

		var paragraph = (MarkdownParagraph)emitted.Single().Blocks[0];
		Uri?[] targets = [.. paragraph.Inlines.OfType<MarkdownLink>().Select(link => link.OpenableUri)];

		Assert.AreSequenceEqual([new Uri("ftp://host/file"), null], targets);
	}

	[TestMethod]
	public void CreateContentTree_BlankContent_ReturnsTheEmptyTree() => Assert.AreEqual("empty", RenderTree(string.Empty));

	[TestMethod]
	public void CreateContentTree_Markdown_ReturnsTheEmittedTree() => Assert.AreEqual("tree", RenderTree("text"));

	[TestMethod]
	public void CreateContentTree_EmitFails_ReturnsThePlainTextTreeOfTheNormalizedContentAndReportsTheFailure()
	{
		var logger = new RecordingLogger();

		string result = RenderTree("a\r\nb", _ => throw new InvalidOperationException("emit failure"), new MarkdownRenderOptions { Logger = logger });

		Assert.AreEqual("plain:a\nb", result);
		Assert.AreEqual(2000, logger.Entries.Single().EventId.Id);
	}

	[TestMethod]
	public void CreatePlainTextContent_NormalizesTheLineTerminatorsAndNeverParses()
	{
		string result = MarkdownRenderPipeline.CreatePlainTextContent("# not a heading\r\nsecond\rthird", text => "fallback:" + text);
		Assert.AreEqual("fallback:# not a heading\nsecond\nthird", result);
	}

	private static string RecordModel(List<MarkdownDocumentModel> models, MarkdownDocumentModel model)
	{
		models.Add(model);
		return "tree";
	}

	private static string RenderContent(string content, Func<MarkdownDocumentModel, string>? emit = null, MarkdownRenderOptions? options = null)
	{
		return MarkdownRenderPipeline.CreateContent(
			content,
			options: options ?? MarkdownRenderOptions.Default,
			emit: emit ?? (_ => "tree"),
			createViewer: tree => "viewer:" + tree,
			createFallback: text => "fallback:" + text
		);
	}

	private static string RenderTree(string content, Func<MarkdownDocumentModel, string>? emit = null, MarkdownRenderOptions? options = null)
	{
		return MarkdownRenderPipeline.CreateContentTree(
			content,
			options: options ?? MarkdownRenderOptions.Default,
			emit: emit ?? (_ => "tree"),
			createEmpty: () => "empty",
			createPlainText: text => "plain:" + text
		);
	}
}
