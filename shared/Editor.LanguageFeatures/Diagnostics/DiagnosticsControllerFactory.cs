using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
#if AVALONIAEDIT
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Diagnostics;
using Nickelony.IDEKit.AvaloniaEdit.Threading;
#else
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.Diagnostics;
using Nickelony.IDEKit.AvalonEdit.Threading;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Diagnostics;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Diagnostics;

#endif

/// <summary>Creates a <see cref="DiagnosticsController"/> that underlines the diagnostics of a text area.</summary>
/// <remarks>
/// The controller reads the text of the text area and runs on the text area's dispatcher, so it must be created on the text area's thread.
/// </remarks>
public static class DiagnosticsControllerFactory
{
	/// <summary>Creates a controller that pulls after the edits of <paramref name="textArea"/> settle and underlines its diagnostics there.</summary>
	/// <param name="textArea">The text area whose text the controller pulls for and whose text view shows the underlines.</param>
	/// <param name="documentId">The document whose diagnostics the controller shows: the id that each pull carries and each push must name.</param>
	/// <param name="provider">The provider the controller asks, or <see langword="null"/> to pull nothing.</param>
	/// <param name="source">The source whose pushes the controller shows, or <see langword="null"/> to take no pushes.</param>
	/// <param name="configureRenderer">
	/// Configures the renderer, for example its pens or its layer, before it is added to the text view, or <see langword="null"/> to keep
	/// its defaults.
	/// </param>
	/// <param name="options">The controller options, or <see langword="null"/> to use the defaults.</param>
	/// <param name="logger">An optional logger for pull and hook failures.</param>
	/// <returns>The controller, which the caller disposes; disposal removes the renderer from the text view.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="textArea"/> or <paramref name="documentId"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="documentId"/> is blank, or <paramref name="provider"/> and <paramref name="source"/> are both <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">The caller is not on the thread that owns <paramref name="textArea"/>.</exception>
	/// <remarks>
	/// The underlines follow the edits made after a set was shown, and <see cref="DiagnosticsController.FindAt"/> answers
	/// from their moved positions. When the text area replaces its document, the controller clears its underlines and pulls
	/// and shows nothing more, so a host recreates it, as <see cref="DiagnosticsController.HandleDocumentReplaced"/> says.
	/// </remarks>
	public static DiagnosticsController Create(
		TextArea textArea,
		string documentId,
		IDiagnosticsProvider? provider,
		IDiagnosticsSource? source,
		Action<DiagnosticsRenderer>? configureRenderer = null,
		DiagnosticsControllerOptions? options = null,
		ILogger? logger = null
	)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

		var dispatcher = UiDispatcher.From(textArea.Dispatcher);

		// The wiring subscribes to the text area, so the thread check comes first and a call from another thread touches nothing.
		dispatcher.VerifyAccess();

		var renderer = new DiagnosticsRenderer(textArea.TextView);
		configureRenderer?.Invoke(renderer);

		DiagnosticsController? controller = null;
		TextDocument? document = null;

		void handleTextChanged(object? sender, EventArgs e) => controller?.HandleTextChanged();

		void attach(TextDocument? next)
		{
			if (document is not null)
				document.TextChanged -= handleTextChanged;

			document = next;

			if (document is not null)
				document.TextChanged += handleTextChanged;
		}

		// The pulls carry the fixed id, so a replacement document retires the controller instead of being pulled under that id.
		void handleDocumentChanged(object? sender, EventArgs e)
		{
			attach(null);
			controller?.HandleDocumentReplaced();
		}

		void detach()
		{
			textArea.DocumentChanged -= handleDocumentChanged;
			attach(null);
			renderer.ClearSegments();
			textArea.TextView.BackgroundRenderers.Remove(renderer);
		}

		var hooks = new DiagnosticsControllerHooks
		{
			GetText = () => textArea.Document?.Text ?? string.Empty,
			ShowDiagnostics = diagnostics => renderer.ApplySegments(DiagnosticSegmentConversion.Project(diagnostics)),
			FindShownIndexAt = offset => renderer.TryFindSegmentAt(offset, out int index) ? index : null,
			GetShownRange = index => renderer.TryGetSegmentRange(index, out TextRange range) ? range : null,
			Detach = detach,
		};

		textArea.TextView.BackgroundRenderers.Add(renderer);
		textArea.DocumentChanged += handleDocumentChanged;
		attach(textArea.Document);

		try
		{
			controller = new DiagnosticsController(documentId, provider, source, hooks, dispatcher, options, logger);
		}
		catch
		{
			// The controller takes over the wiring only when its constructor returns, so a constructor that throws leaves the release to this catch.
			detach();
			throw;
		}

		return controller;
	}
}
