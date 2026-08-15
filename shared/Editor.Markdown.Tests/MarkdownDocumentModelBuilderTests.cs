#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Markdown.Tests;
#else
namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;
#endif

/// <summary>
/// Pins the Markdig-to-model mapping performed by the shared, engine-neutral
/// <see cref="MarkdownDocumentModelBuilder"/>.
/// </summary>
/// <remarks>
/// The model is toolkit-free, so these tests run unchanged in every editor binding's suite and cover the
/// walk once instead of once per binding.
/// </remarks>
[TestClass]
public sealed class MarkdownDocumentModelBuilderTests
{
	private static MarkdownDocumentModel Build(string markdown, MarkdownRenderOptions? options = null)
		=> MarkdownDocumentModelBuilder.Build(markdown, options ?? MarkdownRenderOptions.Default);

	private static MarkdownBlock SingleBlock(MarkdownDocumentModel model)
		=> model.Blocks.Single();

	private static MarkdownParagraph SingleParagraph(MarkdownDocumentModel model)
		=> (MarkdownParagraph)SingleBlock(model);

	private static string GetText(MarkdownDocumentModel model)
		=> GetText(SingleParagraph(model).Inlines);

	private static string GetText(IReadOnlyList<MarkdownInline> inlines)
	{
		var builder = new System.Text.StringBuilder();

		AppendText(builder, inlines);

		return builder.ToString();
	}

	private static void AppendText(System.Text.StringBuilder builder, IReadOnlyList<MarkdownInline> inlines)
	{
		foreach (MarkdownInline inline in inlines)
			AppendText(builder, inline);
	}

	private static void AppendText(System.Text.StringBuilder builder, MarkdownInline inline)
	{
		switch (inline)
		{
			case MarkdownText text:
				builder.Append(text.Text);
				break;

			case MarkdownCodeSpan codeSpan:
				builder.Append(codeSpan.Text);
				break;

			case MarkdownImagePlaceholder placeholder:
				builder.Append(placeholder.Text);
				break;

			case MarkdownLineBreak:
				builder.Append('\n');
				break;

			case MarkdownLink link:
				AppendText(builder, link.Inlines);
				break;

			case MarkdownEmphasis emphasis:
				AppendText(builder, emphasis.Inlines);
				break;

			case MarkdownSpan span:
				AppendText(builder, span.Inlines);
				break;
		}
	}

	[TestMethod]
	public void Build_LiteralText_ProducesParagraphWithText()
	{
		MarkdownDocumentModel model = Build("plain text");

		Assert.AreEqual(1, model.Blocks.Count);
		Assert.AreEqual("plain text", GetText(model));
	}

	[TestMethod]
	public void Build_Heading_ProducesHeadingWithLevel()
	{
		var heading = (MarkdownHeading)SingleBlock(Build("### Heading"));

		Assert.AreEqual(3, heading.Level);
		Assert.AreEqual("Heading", GetText(heading.Inlines));
	}

	[TestMethod]
	public void Build_BoldItalicAndStrikethrough_ProduceEmphasisKinds()
	{
		MarkdownParagraph paragraph = SingleParagraph(Build("**bold** *italic* ~~gone~~"));

		var kinds = paragraph.Inlines.OfType<MarkdownEmphasis>().Select(emphasis => emphasis.Kind).ToList();

		CollectionAssert.AreEqual(
			new[] { MarkdownEmphasisKind.Bold, MarkdownEmphasisKind.Italic, MarkdownEmphasisKind.Strikethrough },
			kinds);
	}

	[DataRow("~sub~")]
	[DataRow("^sup^")]
	[DataRow("==mark==")]
	[DataRow("++ins++")]
	[TestMethod]
	public void Build_UnsupportedEmphasisVariant_StaysLiteralText(string variant)
	{
		MarkdownParagraph paragraph = SingleParagraph(Build(variant));

		Assert.AreEqual(variant, GetText(paragraph.Inlines));
		Assert.AreEqual(0, paragraph.Inlines.OfType<MarkdownEmphasis>().Count());
	}

	[TestMethod]
	public void Build_SoftLineBreak_ProducesSpace()
	{
		Assert.AreEqual("alpha beta", GetText(Build("alpha\nbeta")));
	}

	[TestMethod]
	public void Build_HardLineBreak_ProducesLineBreak()
	{
		MarkdownParagraph paragraph = SingleParagraph(Build("alpha  \nbeta"));

		Assert.AreEqual(1, paragraph.Inlines.OfType<MarkdownLineBreak>().Count());
		Assert.AreEqual("alpha\nbeta", GetText(paragraph.Inlines));
	}

	[TestMethod]
	public void Build_InlineCode_ProducesCodeSpan()
	{
		var codeSpan = (MarkdownCodeSpan)SingleParagraph(Build("Use `TextEditor` here.")).Inlines[1];

		Assert.AreEqual("TextEditor", codeSpan.Text);
	}

	[TestMethod]
	public void Build_HttpsLink_ProducesOpenableUri()
	{
		var link = (MarkdownLink)SingleParagraph(Build("[docs](https://example.com)")).Inlines.Single();

		Assert.AreEqual("https://example.com/", link.OpenableUri?.AbsoluteUri);
		Assert.AreEqual("docs", GetText(link.Inlines));
	}

	[TestMethod]
	public void Build_UnsupportedSchemeLink_ProducesLinkWithoutOpenableUri()
	{
		var link = (MarkdownLink)SingleParagraph(Build("[x](javascript:alert(1))")).Inlines.Single();

		Assert.IsNull(link.OpenableUri);
		Assert.AreEqual("javascript:alert(1)", link.Url);
	}

	[TestMethod]
	public void Build_ReferenceLink_ResolvesTargetFromDefinition()
	{
		var link = (MarkdownLink)SingleParagraph(Build("[docs][ref]\n\n[ref]: https://example.com")).Inlines.Single();

		Assert.AreEqual("https://example.com/", link.OpenableUri?.AbsoluteUri);
	}

	[TestMethod]
	public void Build_Autolink_ProducesOpenableUri()
	{
		MarkdownParagraph paragraph = SingleParagraph(Build("See <https://example.com> for details."));

		var link = paragraph.Inlines.OfType<MarkdownLink>().Single();

		Assert.AreEqual("https://example.com/", link.OpenableUri?.AbsoluteUri);
	}

	[TestMethod]
	public void Build_EmailAutolink_ProducesMailtoTargetWithBareAddressText()
	{
		MarkdownParagraph paragraph = SingleParagraph(Build("Write to <user@example.com> today."));
		var link = paragraph.Inlines.OfType<MarkdownLink>().Single();

		// The mailto target is only reported as openable when the options allow the scheme, but the target
		// and the displayed address are always the mailto destination and the bare address.
		Assert.AreEqual("mailto:user@example.com", link.Url);
		Assert.IsNull(link.OpenableUri);
		Assert.AreEqual("user@example.com", GetText(link.Inlines));
	}

	[TestMethod]
	public void Build_EmailAutolinkWithMailtoScheme_ReportsOpenableUri()
	{
		var options = new MarkdownRenderOptions
		{
			SupportedHyperlinkSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mailto" }
		};

		MarkdownParagraph paragraph = SingleParagraph(Build("Write to <user@example.com> today.", options));
		var link = paragraph.Inlines.OfType<MarkdownLink>().Single();

		Assert.AreEqual("mailto:user@example.com", link.OpenableUri?.AbsoluteUri);
	}

	[TestMethod]
	public void Build_ImageWithAltText_ProducesSpanWithAltText()
	{
		var span = (MarkdownSpan)SingleParagraph(Build("![alt text](https://example.com/image.png)")).Inlines.Single();

		Assert.AreEqual("alt text", GetText(span.Inlines));
		Assert.AreEqual(0, span.Inlines.OfType<MarkdownImagePlaceholder>().Count());
	}

	[TestMethod]
	public void Build_ImageWithoutAltText_ProducesPlaceholderWithTarget()
	{
		var span = (MarkdownSpan)SingleParagraph(Build("![](https://example.com/image.png)")).Inlines.Single();
		var placeholder = span.Inlines.OfType<MarkdownImagePlaceholder>().Single();

		Assert.AreEqual("https://example.com/image.png", placeholder.Text);
	}

	[TestMethod]
	public void Build_ImageWithoutAltTextOrTarget_ProducesGenericPlaceholder()
	{
		var span = (MarkdownSpan)SingleParagraph(Build("![]()")).Inlines.Single();
		var placeholder = span.Inlines.OfType<MarkdownImagePlaceholder>().Single();

		Assert.AreEqual("image", placeholder.Text);
	}

	[TestMethod]
	public void Build_LinkedImage_ProducesLinkSpanningAltTextOnly()
	{
		MarkdownParagraph paragraph = SingleParagraph(Build("[![alt text](https://example.com/image.png)](https://example.com)"));
		var link = paragraph.Inlines.OfType<MarkdownLink>().Single();

		Assert.AreEqual("https://example.com/", link.OpenableUri?.AbsoluteUri);
		Assert.AreEqual("alt text", GetText(link.Inlines));
	}

	[TestMethod]
	public void Build_HtmlEntity_DecodesToCharacter()
	{
		Assert.AreEqual("AT&T <ok> A", GetText(Build("AT&amp;T &lt;ok&gt; &#65;")));
	}

	[TestMethod]
	public void Build_RawHtmlBlock_StaysLiteralText()
	{
		Assert.AreEqual("<div>raw</div>", GetText(Build("<div>raw</div>")));
	}

	[TestMethod]
	public void Build_InlineHtml_StaysLiteralText()
	{
		Assert.AreEqual("before <b>bold</b> after", GetText(Build("before <b>bold</b> after")));
	}

	[TestMethod]
	public void Build_FencedCodeBlock_ProducesLanguageAndCode()
	{
		var codeBlock = (MarkdownCodeBlock)SingleBlock(Build("```lua\nlocal value = 1\nprint(value)\n```"));

		Assert.AreEqual("lua", codeBlock.Language);
		Assert.AreEqual("local value = 1\nprint(value)", codeBlock.Code.TrimEnd('\n'));
	}

	[TestMethod]
	public void Build_FencedCodeBlockWithAttributes_KeepsOnlyTheLanguageToken()
	{
		// The fence attributes after the language token are dropped, so the token resolves as the language.
		var codeBlock = (MarkdownCodeBlock)SingleBlock(Build("```cs title=\"sample\"\nvar x = 1;\n```"));

		Assert.AreEqual("cs", codeBlock.Language);
	}

	[TestMethod]
	public void Build_BareFence_ProducesEmptyLanguageToken()
	{
		var codeBlock = (MarkdownCodeBlock)SingleBlock(Build("```\ncode\n```"));

		Assert.AreEqual(string.Empty, codeBlock.Language);
	}

	[TestMethod]
	public void Build_IndentedCodeBlock_HasNullLanguage()
	{
		var codeBlock = (MarkdownCodeBlock)SingleBlock(Build("    var x = 1;"));

		Assert.IsNull(codeBlock.Language);
	}

	[TestMethod]
	public void Build_Quote_ProducesQuotedBlocks()
	{
		var quote = (MarkdownQuote)SingleBlock(Build("> quoted"));

		Assert.AreEqual(1, quote.Blocks.Count);
		Assert.AreEqual("quoted", GetText(((MarkdownParagraph)quote.Blocks.Single()).Inlines));
	}

	[TestMethod]
	public void Build_BulletList_IsUnorderedStartingAtOne()
	{
		var list = (MarkdownList)SingleBlock(Build("- one\n- two"));

		Assert.IsFalse(list.IsOrdered);
		Assert.AreEqual(1, list.StartIndex);
		Assert.AreEqual(2, list.Items.Count);
	}

	[TestMethod]
	public void Build_OrderedList_ReportsStartIndex()
	{
		var list = (MarkdownList)SingleBlock(Build("3. three\n4. four"));

		Assert.IsTrue(list.IsOrdered);
		Assert.AreEqual(3, list.StartIndex);
	}

	[TestMethod]
	public void Build_OrderedListStartingAtZero_ClampsToOne()
	{
		var list = (MarkdownList)SingleBlock(Build("0. zero\n1. one"));

		Assert.AreEqual(1, list.StartIndex);
		Assert.AreEqual(2, list.Items.Count);
	}

	[TestMethod]
	public void Build_TightList_ReportsTightAndLooseListReportsLoose()
	{
		Assert.IsFalse(((MarkdownList)SingleBlock(Build("- one\n- two"))).IsLoose);
		Assert.IsTrue(((MarkdownList)SingleBlock(Build("- one\n\n- two"))).IsLoose);
	}

	[TestMethod]
	public void Build_Table_ProducesHeaderRowCellsAndColumnAlignments()
	{
		var table = (MarkdownTable)SingleBlock(Build("| left | center | right |\n|:--|:-:|--:|\n| 1 | 2 | 3 |"));

		Assert.AreEqual(2, table.Rows.Count);
		Assert.IsTrue(table.Rows[0].IsHeader);
		Assert.IsFalse(table.Rows[1].IsHeader);
		Assert.AreEqual(3, table.Rows[1].Cells.Count);

		CollectionAssert.AreEqual(
			new MarkdownColumnAlignment?[]
			{
				MarkdownColumnAlignment.Left,
				MarkdownColumnAlignment.Center,
				MarkdownColumnAlignment.Right
			},
			table.ColumnAlignments.ToArray());

		Assert.AreEqual(MarkdownColumnAlignment.Left, table.Rows[1].Cells[0].Alignment);
		Assert.AreEqual(MarkdownColumnAlignment.Center, table.Rows[1].Cells[1].Alignment);
		Assert.AreEqual(MarkdownColumnAlignment.Right, table.Rows[1].Cells[2].Alignment);
	}

	[TestMethod]
	public void Build_TableWithoutAlignment_ReportsNullColumnAlignment()
	{
		var table = (MarkdownTable)SingleBlock(Build("| a |\n|---|\n| 1 |"));

		Assert.AreEqual(1, table.ColumnAlignments.Count);
		Assert.IsNull(table.ColumnAlignments[0]);
		Assert.IsNull(table.Rows[1].Cells[0].Alignment);
	}

	[TestMethod]
	public void Build_ThematicBreak_ProducesThematicBreak()
	{
		MarkdownDocumentModel model = Build("before\n\n---\n\nafter");

		Assert.AreEqual(3, model.Blocks.Count);
		Assert.IsInstanceOfType<MarkdownThematicBreak>(model.Blocks[1]);
	}

	[TestMethod]
	public void Build_CodeBlockInsideBlockquote_ProducesNestedCodeBlock()
	{
		var quote = (MarkdownQuote)SingleBlock(Build("> ```\n> local x = 1\n> ```"));

		var codeBlock = (MarkdownCodeBlock)quote.Blocks.Single();

		Assert.AreEqual("local x = 1", codeBlock.Code.TrimEnd('\n'));
	}
}
