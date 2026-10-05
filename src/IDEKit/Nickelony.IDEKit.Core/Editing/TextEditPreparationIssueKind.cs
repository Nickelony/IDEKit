namespace Nickelony.IDEKit.Core.Editing;

/// <summary>Identifies the rule that rejected one edit of a preparation batch.</summary>
/// <remarks>
/// <see cref="InsertionInsideReplacement"/> and <see cref="ReplacementOverlap"/> report a conflict between two edits of the batch,
/// and the other members reject one edit on its own.
/// </remarks>
public enum TextEditPreparationIssueKind
{
	/// <summary>The batch contained a <see langword="null"/> edit.</summary>
	NullEdit,

	/// <summary>An edit carried a <see langword="null"/> replacement text.</summary>
	NullReplacementText,

	/// <summary>An edit range did not fit inside the document.</summary>
	RangeOutOfBounds,

	/// <summary>An insertion starts strictly inside a replacement range.</summary>
	InsertionInsideReplacement,

	/// <summary>Two replacement ranges overlap.</summary>
	ReplacementOverlap,
}
