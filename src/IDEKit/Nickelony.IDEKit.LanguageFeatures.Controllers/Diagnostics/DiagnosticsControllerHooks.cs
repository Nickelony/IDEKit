using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Diagnostics;

/// <summary>Groups the required and optional host hooks used by the <see cref="DiagnosticsController"/>.</summary>
/// <remarks>
/// The controller runs every hook on its dispatcher thread. A hook should not throw: a failure that escapes one is contained and logged.
/// </remarks>
public sealed class DiagnosticsControllerHooks
{
	/// <summary>The hook that returns the current text of the document, which a pull sends to the provider.</summary>
	public required Func<string> GetText { get; init; }

	/// <summary>The hook that shows a new set of diagnostics in the editor, for example as underlines.</summary>
	/// <remarks>
	/// The controller calls it with the list that <see cref="DiagnosticsController.Current"/> holds from then on, an empty list included.
	/// A range of the list describes the text the producer read, so it can lie past the end of the text as it is now, and the hook clamps
	/// it to the text.
	/// </remarks>
	public required Action<IReadOnlyList<Diagnostic>> ShowDiagnostics { get; init; }

	/// <summary>
	/// The optional hook that finds the shown diagnostic at an offset of the document as it is now, as its position in the list
	/// that <see cref="ShowDiagnostics"/> received, or <see langword="null"/> when none covers the offset.
	/// </summary>
	/// <remarks>
	/// An editor that moves its decorations with the edits made since a set was shown answers from the moved positions,
	/// so <see cref="DiagnosticsController.FindAt"/> finds the diagnostic where its text is now. Without the hook, the controller compares the offset
	/// with the ranges the producer reported.
	/// </remarks>
	public Func<int, int?>? FindShownIndexAt { get; init; }

	/// <summary>
	/// The optional hook that returns where the shown diagnostic at a position of the list that <see cref="ShowDiagnostics"/> received sits
	/// in the document as it is now, or <see langword="null"/> when an edit removed its text.
	/// </summary>
	/// <remarks>
	/// <see cref="DiagnosticsController.FindAt"/> and <see cref="DiagnosticsController.TryGetShownRange"/>
	/// answer from it; without it, they answer the ranges the producer reported.
	/// </remarks>
	public Func<int, TextRange?>? GetShownRange { get; init; }

	/// <summary>
	/// The optional hook that releases the editor wiring, which the controller calls once, from <see cref="DiagnosticsController.Dispose"/>.
	/// </summary>
	public Action? Detach { get; init; }
}
