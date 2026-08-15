#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Markdown;
#else
namespace Nickelony.IDEKit.AvalonEdit.Markdown;
#endif

/// <summary>
/// The engine-neutral Markdown content model: the root of the block tree that
/// <see cref="MarkdownDocumentModelBuilder"/> produces and each binding's emitter turns into toolkit elements.
/// </summary>
/// <param name="Blocks">The document's top-level blocks, in source order.</param>
internal sealed record MarkdownDocumentModel(IReadOnlyList<MarkdownBlock> Blocks);
