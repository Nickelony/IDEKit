namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Identifies the rule violated by a conflicting edit batch.
/// </summary>
internal enum TextEditConflictKind
{
	/// <summary>
	/// An insertion starts strictly inside a replacement range.
	/// </summary>
	InsertionInsideReplacement,

	/// <summary>
	/// Two replacement ranges overlap.
	/// </summary>
	ReplacementOverlap
}
