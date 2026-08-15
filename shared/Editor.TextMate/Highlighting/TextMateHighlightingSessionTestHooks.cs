#if AVALONIAEDIT
using AvaloniaEdit.Rendering;
#else
using ICSharpCode.AvalonEdit.Rendering;
#endif

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Highlighting;
#else
namespace Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;
#endif

/// <summary>
/// Groups the test-only hooks of <see cref="TextMateHighlightingSession"/> so the session's public
/// <c>Start</c> signature stays free of test-only parameters.
/// </summary>
/// <remarks>
/// The hooks are installed through an <c>InternalsVisibleTo</c> construction path: the internal <c>Start</c>
/// overload takes this object while the public overload passes <see langword="null"/>, so production callers
/// never reach it.
/// </remarks>
internal sealed class TextMateHighlightingSessionTestHooks
{
	/// <summary>
	/// Gets or initializes the hook invoked once the session's resources exist and before the grammar is
	/// applied, so a test can capture the created line list or force the setup to fail inside the rollback
	/// window, or <see langword="null"/> in production.
	/// </summary>
	internal Action<TextMateDocumentLineList>? LineListCreated { get; init; }
}
