using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>Describes one replacement of a zero-based UTF-16 range: the diff between two document contents and an input of the edit kernel.</summary>
/// <param name="Range">The zero-based UTF-16 range in the original content.</param>
/// <param name="NewText">
/// The replacement text. A <see langword="default"/> instance carries <see langword="null"/> here despite the annotation; the preparation kernel
/// reports it as an invalid replacement text instead of throwing.
/// </param>
/// <remarks>
/// <see cref="TextIncrementalEditCalculator"/> returns it as the diff between two document contents,
/// and <see cref="TextEditKernel.Prepare(ITextSnapshot, IEnumerable{TextIncrementalEdit})"/> validates a batch of them. It is a value type
/// because both paths run once per document change or commit.
/// </remarks>
public readonly record struct TextIncrementalEdit(TextRange Range, string NewText)
{
	/// <summary><see langword="true"/> when the edit replaces an empty range with an empty string.</summary>
	/// <remarks>
	/// An edit whose replacement text is <see langword="null"/> is invalid rather than a no-op, so it is not reported as one.
	/// </remarks>
	public bool IsNoOp => Range.Length == 0 && NewText is { Length: 0 };
}
