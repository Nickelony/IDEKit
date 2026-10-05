using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Markdown.Model;

namespace Nickelony.IDEKit.Markdown;

/// <summary>
/// Runs the steps the Markdown renderers share around their toolkit-specific emitters: line-terminator normalization, the whitespace test,
/// the guarded render with its failure report, and the plain-text fallback.
/// </summary>
/// <remarks>
/// A renderer supplies the toolkit-specific steps as delegates, so the order of the steps and the failure policy are defined once. A fatal exception
/// (out of memory, or a canceled operation) is not absorbed by the guarded render and propagates to the caller.
/// </remarks>
public static class MarkdownRenderPipeline
{
	/// <summary>Renders the content into an element, or into the fallback element when the content is blank or the render fails.</summary>
	/// <typeparam name="TTree">The type the emitter produces from the document model.</typeparam>
	/// <typeparam name="TContent">The type of the element the entry point returns.</typeparam>
	/// <param name="content">The Markdown content to render.</param>
	/// <param name="options">The rendering options that carry the logger and the supported hyperlink schemes.</param>
	/// <param name="emit">Emits the document model into the toolkit's content tree.</param>
	/// <param name="createViewer">Wraps an emitted tree in the viewer the entry point returns.</param>
	/// <param name="createFallback">Creates the plain-text element for the given normalized content.</param>
	/// <returns>The viewer of the emitted tree, or the plain-text element.</returns>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	/// <remarks>
	/// Blank content yields the fallback element for an empty string without parsing the content.
	/// </remarks>
	public static TContent CreateContent<TTree, TContent>(
		string content,
		MarkdownRenderOptions options,
		Func<MarkdownDocumentModel, TTree> emit,
		Func<TTree, TContent> createViewer,
		Func<string, TContent> createFallback
	)
		where TTree : class
	{
		ArgumentNullException.ThrowIfNull(content);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(emit);
		ArgumentNullException.ThrowIfNull(createViewer);
		ArgumentNullException.ThrowIfNull(createFallback);

		string normalizedContent = LineTerminatorNormalizer.NormalizeToLineFeeds(content);

		if (string.IsNullOrWhiteSpace(normalizedContent))
			return createFallback(string.Empty);

		TTree? tree = RenderOrNull(normalizedContent, options, emit);

		return tree is null ? createFallback(normalizedContent) : createViewer(tree);
	}

	/// <summary>Renders the content into the toolkit's content tree, or into its plain-text tree when the render fails.</summary>
	/// <typeparam name="TTree">The type the emitter produces from the document model.</typeparam>
	/// <param name="content">The Markdown content to render.</param>
	/// <param name="options">The rendering options that carry the logger and the supported hyperlink schemes.</param>
	/// <param name="emit">Emits the document model into the toolkit's content tree.</param>
	/// <param name="createEmpty">Creates the empty tree that blank content yields.</param>
	/// <param name="createPlainText">Creates the tree that shows the given normalized content as plain text.</param>
	/// <returns>The emitted tree, the empty tree, or the plain-text tree.</returns>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public static TTree CreateContentTree<TTree>(
		string content,
		MarkdownRenderOptions options,
		Func<MarkdownDocumentModel, TTree> emit,
		Func<TTree> createEmpty,
		Func<string, TTree> createPlainText
	)
		where TTree : class
	{
		ArgumentNullException.ThrowIfNull(content);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(emit);
		ArgumentNullException.ThrowIfNull(createEmpty);
		ArgumentNullException.ThrowIfNull(createPlainText);

		string normalizedContent = LineTerminatorNormalizer.NormalizeToLineFeeds(content);

		if (string.IsNullOrWhiteSpace(normalizedContent))
			return createEmpty();

		return RenderOrNull(normalizedContent, options, emit) ?? createPlainText(normalizedContent);
	}

	/// <summary>Creates the plain-text element for the content without parsing it as Markdown.</summary>
	/// <typeparam name="TContent">The type of the element the entry point returns.</typeparam>
	/// <param name="content">The text content to show.</param>
	/// <param name="createFallback">Creates the plain-text element for the given normalized content.</param>
	/// <returns>The plain-text element.</returns>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public static TContent CreatePlainTextContent<TContent>(string content, Func<string, TContent> createFallback)
	{
		ArgumentNullException.ThrowIfNull(content);
		ArgumentNullException.ThrowIfNull(createFallback);

		return createFallback(LineTerminatorNormalizer.NormalizeToLineFeeds(content));
	}

	private static TTree? RenderOrNull<TTree>(string normalizedContent, MarkdownRenderOptions options, Func<MarkdownDocumentModel, TTree> emit)
		where TTree : class
	{
		try
		{
			MarkdownDocumentModel model = MarkdownDocumentModelBuilder.Build(normalizedContent, options.SupportedHyperlinkSchemes);
			return emit(model);
		}
		catch (Exception exception) when (exception is not (OutOfMemoryException or OperationCanceledException))
		{
			// A non-fatal failure is reported and yields null, which selects the plain-text fallback. The fallback keeps
			// tooltips resilient, but the failure stays observable.
			MarkdownRenderFailureReporter.ReportFailure(exception, options);
			return null;
		}
	}
}
