using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Describes one minimal replacement that transforms one document content value into another.
/// </summary>
/// <remarks>
/// This is the lightweight result shape returned by
/// <see cref="TextIncrementalEditCalculator.Compute(string?, string?)"/>. The three editing shapes
/// differ by role: <see cref="TextEditInput"/> is a caller's edit batch input,
/// <see cref="TextEditOperation"/> is the kernel's validated output, and this struct is the
/// synchronization edit computed between two contents. To validate it as part of an edit batch,
/// construct a <see cref="TextEditInput"/> from <see cref="Range"/> and <see cref="NewText"/>.
/// </remarks>
/// <param name="Range">The zero-based UTF-16 range in the original content.</param>
/// <param name="NewText">
/// The replacement text. A <see langword="default"/> instance carries <see langword="null"/> here
/// despite the annotation; the preparation kernel reports it as an invalid replacement text instead
/// of throwing.
/// </param>
public readonly record struct TextIncrementalEdit(TextRange Range, string NewText);
