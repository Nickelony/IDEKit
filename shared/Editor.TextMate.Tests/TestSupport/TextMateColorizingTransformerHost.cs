#if AVALONIAEDIT
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.TextMate.Highlighting;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
using static Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests.TextMateThemeTestHelpers;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;
using System.Windows.Threading;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
using static Nickelony.IDEKit.AvalonEdit.TextMate.Tests.TextMateThemeTestHelpers;
#endif
using TextMateSharp.Model;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests;
#else
namespace Nickelony.IDEKit.AvalonEdit.TextMate.Tests;
#endif

/// <summary>
/// Owns the document, line list, model, editor, style resolver, and colorizing transformer that a paint- or
/// notification-path test needs, and disposes them in the order the transformer contract requires: the
/// window and the transformer attachment first, then the model and the line list.
/// </summary>
/// <remarks>
/// The paint-path scenarios ask for a hosted editor, which attaches the transformer to the text view and
/// shows the editor in a window; the notification-path scenarios script the redraw dispatch instead, so a
/// test observes the coalescing behavior a real text view does not expose.
/// </remarks>
internal sealed class TextMateColorizingTransformerHost : IDisposable
{
	private readonly HostWindow? _window;
	private readonly bool _transformerAttached;

	private TextMateColorizingTransformerHost(
		TextDocument document,
		TextMateDocumentLineList lineList,
		TMModel model,
		TextEditor editor,
		TextMateColorizingTransformer transformer,
		HostWindow? window,
		bool transformerAttached)
	{
		Document = document;
		LineList = lineList;
		Model = model;
		Editor = editor;
		Transformer = transformer;
		_window = window;
		_transformerAttached = transformerAttached;
	}

	/// <summary>Gets the document the model tokenizes.</summary>
	internal TextDocument Document { get; }

	/// <summary>Gets the line list backing the model.</summary>
	internal TextMateDocumentLineList LineList { get; }

	/// <summary>Gets the TextMate model.</summary>
	internal TMModel Model { get; }

	/// <summary>Gets the editor that renders the document.</summary>
	internal TextEditor Editor { get; }

	/// <summary>Gets the transformer under test.</summary>
	internal TextMateColorizingTransformer Transformer { get; }

	/// <summary>
	/// Creates a host whose model has tokenized the document, optionally through a grammar, and whose
	/// transformer either uses the text view's redraw dispatch or the supplied scripted delegates.
	/// </summary>
	/// <param name="grammarLanguageId">The grammar to load; when <see langword="null"/> the model stays without one.</param>
	/// <param name="rules">The theme rules the style resolver matches, or <see langword="null"/> for none.</param>
	/// <param name="queueRedraw">The scripted redraw queue; when <see langword="null"/> the text view's dispatcher is used.</param>
	/// <param name="redrawRange">The scripted range redraw; when <see langword="null"/> the text view's content mapping is used.</param>
	/// <param name="hostInWindow">Whether to attach the transformer and show the editor in a window.</param>
	/// <returns>The created host.</returns>
	internal static TextMateColorizingTransformerHost Create(
		string? grammarLanguageId = null,
		TextMateTokenThemeRule[]? rules = null,
		Func<Action, DispatcherOperation?>? queueRedraw = null,
		Action<int, int>? redrawRange = null,
		bool hostInWindow = false)
	{
		var document = new TextDocument("local value = 1\n");
		var lineList = new TextMateDocumentLineList(document);
		var model = new TMModel(lineList);

		if (grammarLanguageId is not null)
			model.SetGrammar(CreateGrammar(grammarLanguageId));

		var editor = new TextEditor { Document = document };
		var resolver = new TextMateThemeStyleResolver(new TextMateTokenTheme { Rules = rules ?? [] });

		TextMateColorizingTransformer transformer = queueRedraw is null && redrawRange is null
			? new TextMateColorizingTransformer(editor.TextArea.TextView, model, resolver)
			: new TextMateColorizingTransformer(
				editor.TextArea.TextView,
				model,
				resolver,
				queueRedraw ?? (_ => null),
				redrawRange ?? ((_, _) => { }));

		if (!hostInWindow)
			return new TextMateColorizingTransformerHost(document, lineList, model, editor, transformer, window: null, transformerAttached: false);

		editor.TextArea.TextView.LineTransformers.Add(transformer);
		HostWindow window = TestHost.ShowInHostWindow(editor);

		return new TextMateColorizingTransformerHost(document, lineList, model, editor, transformer, window, transformerAttached: true);
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		_window?.Close();

		if (_transformerAttached)
			Editor.TextArea.TextView.LineTransformers.Remove(Transformer);

		Transformer.Dispose();
		Model.Dispose();
		LineList.Dispose();
	}
}
