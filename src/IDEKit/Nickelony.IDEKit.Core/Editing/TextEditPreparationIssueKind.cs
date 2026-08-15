namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Identifies the rule that rejected one edit of a preparation batch.
/// </summary>
/// <remarks>
/// The conflict members mirror <see cref="TextEditConflictKind"/>, the rule set the shared
/// conflict detector reports with; <see cref="TextEditKernel"/> maps one onto the other and
/// throws for an unmapped rule, so a new conflict rule cannot reach a caller uncategorized.
/// </remarks>
public enum TextEditPreparationIssueKind
{
	/// <summary>
	/// The batch contained a <see langword="null"/> edit.
	/// </summary>
	NullEdit,

	/// <summary>
	/// An edit carried a <see langword="null"/> replacement text.
	/// </summary>
	NullReplacementText,

	/// <summary>
	/// An edit range did not fit inside the document.
	/// </summary>
	RangeOutOfBounds,

	/// <summary>
	/// An insertion starts strictly inside a replacement range.
	/// </summary>
	InsertionInsideReplacement,

	/// <summary>
	/// Two replacement ranges overlap.
	/// </summary>
	ReplacementOverlap,
}
