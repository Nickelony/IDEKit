using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

#if AVALONIAEDIT
namespace Nickelony.Samples.Avalonia.Tests;

#else
namespace Nickelony.Samples.Wpf.Tests;

#endif

/// <summary>Pushes the diagnostics a test raises, as a language server publishes them, and holds none before the first push.</summary>
internal sealed class FakeDiagnosticsSource : IDiagnosticsSource
{
	public event EventHandler<DiagnosticsUpdatedEventArgs>? DiagnosticsUpdated;

	public IReadOnlyList<Diagnostic> GetDiagnostics(string documentId) => [];

	internal void Raise(string documentId, IReadOnlyList<Diagnostic> diagnostics) =>
		DiagnosticsUpdated?.Invoke(this, new DiagnosticsUpdatedEventArgs(documentId, diagnostics));
}
