#if AVALONIAEDIT
using Avalonia.Media;
using Brush = Avalonia.Media.IBrush;
#else
using System.Windows;
using Brush = System.Windows.Media.Brush;
#endif

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Rendering;
#else
namespace Nickelony.IDEKit.AvalonEdit.Rendering;
#endif

/// <summary>
/// Describes the visual formatting of a text run that a colorizing transformer applies to the editor's
/// visual line elements.
/// </summary>
/// <remarks>
/// <para>
/// Styles are resolved and applied on every paint pass of a text view, so implementations shared
/// between calls should hold brushes and decoration collections that are not mutated after they are
/// created.
/// </para>
/// <para>
/// Use <see cref="TextRunStyleApplier"/> to apply a style to a visual line element.
/// </para>
/// </remarks>
public interface ITextRunStyle
{
	/// <summary>
	/// Gets the foreground brush to apply, when one is resolved.
	/// </summary>
	Brush? Foreground { get; }

	/// <summary>
	/// Gets a value indicating whether the run is rendered bold.
	/// </summary>
	bool IsBold { get; }

	/// <summary>
	/// Gets a value indicating whether the run is rendered italic.
	/// </summary>
	bool IsItalic { get; }

	/// <summary>
	/// Gets the text decorations to apply, when any are resolved.
	/// </summary>
	TextDecorationCollection? TextDecorations { get; }

	/// <summary>
	/// Gets a value indicating whether the style requests any formatting: it has a foreground brush,
	/// is bold, is italic, or carries a non-empty text decoration collection.
	/// </summary>
	/// <remarks>
	/// A <see langword="null"/> or empty decoration collection requests nothing and does not count as
	/// formatting.
	/// <see cref="TextRunStyle"/> expresses exactly those four conditions, so callers can skip applying
	/// an empty style. The rule stays on the implementation because a default interface implementation
	/// would only be callable through the interface, which boxes struct styles at the call site.
	/// </remarks>
	bool HasFormatting { get; }
}
