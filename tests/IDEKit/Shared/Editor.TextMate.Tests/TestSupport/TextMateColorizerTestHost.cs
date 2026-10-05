using TextMateSharp.Model;
#if AVALONIAEDIT
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using static Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests.TextMateRunStyleTestHelpers;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using System.Windows.Threading;
using static Nickelony.IDEKit.AvalonEdit.TextMate.Tests.TextMateRunStyleTestHelpers;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests;

#else

namespace Nickelony.IDEKit.AvalonEdit.TextMate.Tests;

#endif

/// <summary>
/// Owns the document, line list, model, editor, style resolver, and colorizer that a paint- or notification-path test needs,
/// and disposes them in the order the colorizer contract requires: the window and the colorizer attachment first, then the model and the line list.
/// </summary>
/// <remarks>
/// The paint-path scenarios ask for a hosted editor, which attaches the colorizer to the text view and shows the editor in a window;
/// the notification-path scenarios script the redraw dispatch instead, so a test observes the coalescing behavior a real text view does not expose.
/// </remarks>
internal sealed class TextMateColorizerTestHost : IDisposable
{
	private readonly HostWindow? _window;
	private readonly bool _transformerAttached;

	private TextMateColorizerTestHost(
		TextDocument document,
		TextMateDocumentLineList lineList,
		TMModel model,
		TextEditor editor,
		TextMateColorizer colorizer,
		HostWindow? window,
		bool transformerAttached
	)
	{
		Document = document;
		LineList = lineList;
		Model = model;
		Editor = editor;
		Colorizer = colorizer;
		_window = window;
		_transformerAttached = transformerAttached;
	}

	/// <summary>The document the model tokenizes.</summary>
	internal TextDocument Document { get; }

	internal TextMateDocumentLineList LineList { get; }

	internal TMModel Model { get; }

	/// <summary>The editor that renders the document.</summary>
	internal TextEditor Editor { get; }

	internal TextMateColorizer Colorizer { get; }

	/// <summary>
	/// Creates a host whose model has tokenized the document, optionally through a grammar, and whose colorizer either uses the text view's redraw
	/// dispatch or the supplied scripted delegates.
	/// </summary>
	/// <param name="grammarLanguageId">The grammar to load; when <see langword="null"/> the model stays without one.</param>
	/// <param name="rules">The theme rules the style resolver matches, or <see langword="null"/> for none.</param>
	/// <param name="queueRedraw">The scripted redraw queue; when <see langword="null"/> the text view's dispatcher is used.</param>
	/// <param name="redrawRange">The scripted range redraw; when <see langword="null"/> the text view's content mapping is used.</param>
	/// <param name="hostInWindow">Whether to attach the colorizer and show the editor in a window.</param>
	/// <returns>The created host.</returns>
	internal static TextMateColorizerTestHost Create(
		string? grammarLanguageId = null,
		TextMateTokenThemeRule[]? rules = null,
		Func<Action, DispatcherOperation?>? queueRedraw = null,
		Action<int, int>? redrawRange = null,
		bool hostInWindow = false
	)
	{
		var document = new TextDocument("local value = 1\n");
		var lineList = new TextMateDocumentLineList(document);
		var model = new TMModel(lineList);

		if (grammarLanguageId is not null)
			model.SetGrammar(CreateGrammar(grammarLanguageId));

		var editor = new TextEditor { Document = document };
		var resolver = new TextMateThemeStyleResolver(new TextMateTokenTheme { Rules = rules ?? [] });

		TextMateColorizer colorizer =
			queueRedraw is null && redrawRange is null
				? new TextMateColorizer(editor.TextArea.TextView, model, resolver)
				: new TextMateColorizer(
					textView: editor.TextArea.TextView,
					model,
					styleResolver: resolver,
					queueRedraw: queueRedraw ?? (_ => null),
					redrawRange: redrawRange ?? ((_, _) => { })
				);

		if (!hostInWindow)
			return new TextMateColorizerTestHost(document, lineList, model, editor, colorizer, window: null, transformerAttached: false);

		editor.TextArea.TextView.LineTransformers.Add(colorizer);
		HostWindow window = TestHost.ShowInHostWindow(editor);

		return new TextMateColorizerTestHost(document, lineList, model, editor, colorizer, window, transformerAttached: true);
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		_window?.Close();

		if (_transformerAttached)
			Editor.TextArea.TextView.LineTransformers.Remove(Colorizer);

		Colorizer.Dispose();
		Model.Dispose();
		LineList.Dispose();
	}
}
