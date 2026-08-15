using Avalonia.Controls;
using AvaloniaEdit.CodeCompletion;
using System.Diagnostics.CodeAnalysis;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

/// <summary>
/// Provides access to the tooltip associated with an AvaloniaEdit <see cref="CompletionWindow"/>.
/// </summary>
/// <remarks>
/// <para>
/// The shared completion pipeline resolves a window's tooltip through this type in both bindings, reading
/// <see cref="IsFieldAvailable"/> and calling <see cref="TryGetTooltip"/>.
/// </para>
/// <para>
/// The WPF completion window exposes its tooltip in a private field, which the WPF binding reflects over.
/// AvaloniaEdit does not: it shows its tooltip through an engine-owned popup of a private type, held in a
/// private field and driven from the window's own selection-changed handler, and it publishes no tooltip
/// member to reflect over. There is therefore nothing this accessor can read.
/// </para>
/// <para>
/// A failed lookup is the type's contract rather than an error: <see cref="IsFieldAvailable"/> is always
/// <see langword="false"/> and <see cref="TryGetTooltip"/> always reports a failed attempt, so the
/// tooltip-aware paths disable tooltip styling and asynchronous description resolution, exactly as they do
/// when an engine's tooltip surface goes missing.
/// </para>
/// </remarks>
internal static class CompletionWindowTooltipAccess
{
	/// <summary>
	/// Gets a value indicating whether the running AvaloniaEdit exposes a tooltip member this accessor can
	/// read. Always <see langword="false"/>, because AvaloniaEdit exposes no tooltip member.
	/// </summary>
	internal static bool IsFieldAvailable => false;

	/// <summary>
	/// Tries to retrieve the tooltip associated with the completion window.
	/// </summary>
	/// <param name="completionWindow">The completion window whose tooltip is requested.</param>
	/// <param name="tooltip">Always <see langword="null"/>, because AvaloniaEdit exposes no tooltip member.</param>
	/// <returns>
	/// Always <see langword="false"/>, because AvaloniaEdit exposes no tooltip member for this accessor to read.
	/// </returns>
	internal static bool TryGetTooltip(CompletionWindow completionWindow, [NotNullWhen(true)] out ToolTip? tooltip)
	{
		_ = completionWindow;
		tooltip = null;
		return false;
	}
}
