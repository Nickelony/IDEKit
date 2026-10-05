using Nickelony.IDEKit.LanguageFeatures.Controllers.Diagnostics;
#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Diagnostics;
#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Diagnostics;
#endif

namespace Nickelony.Samples;

/// <summary>
/// Underlines the diagnostics of one editor, which the in-process provider answers after each edit settles or a language server pushes.
/// </summary>
internal static class SampleDiagnostics
{
	/// <param name="editor">The editor whose text the diagnostics underline.</param>
	/// <param name="features">The language features, whose provider or source produces the diagnostics.</param>
	/// <param name="documentId">The identity of the edited document: its local file path, which a language server names in each push.</param>
	/// <returns>The controller, or <see langword="null"/> when the features produce no diagnostics.</returns>
	internal static DiagnosticsController? Create(TextEditor editor, SampleLanguageFeatures features, string documentId)
	{
		if (features.Diagnostics is null && features.DiagnosticsSource is null)
			return null;

		return DiagnosticsControllerFactory.Create(
			textArea: editor.TextArea,
			documentId,
			provider: features.Diagnostics,
			source: features.DiagnosticsSource,

			options: DiagnosticsControllerOptions.Default with
			{
				DocumentIdComparer = SampleDocuments.PathComparer,
			}
		);
	}
}
