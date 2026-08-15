using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Describes a text replacement using a zero-based UTF-16 source range.
/// </summary>
/// <remarks>
/// This is the batch-input shape for <see cref="TextEditKernel.Prepare(ITextSnapshot, IEnumerable{TextEditInput?})"/>:
/// it is a reference type, so a <see langword="null"/> entry and a <see langword="null"/>
/// <see cref="NewText"/> surface as preparation diagnostics instead of throwing.
/// </remarks>
/// <param name="Range">The source range to replace.</param>
/// <param name="NewText">
/// The replacement text, or <see langword="null"/> to have the kernel report the input as invalid
/// instead of throwing.
/// </param>
public sealed record TextEditInput(TextRange Range, string? NewText)
{
	/// <summary>
	/// Gets a value indicating whether the input changes nothing: it replaces an empty range with
	/// an empty string.
	/// </summary>
	/// <remarks>
	/// An input whose replacement text is <see langword="null"/> is invalid rather than a no-op, so
	/// it is not reported as one.
	/// </remarks>
	public bool IsNoOp => Range.Length == 0 && NewText is { Length: 0 };
}
