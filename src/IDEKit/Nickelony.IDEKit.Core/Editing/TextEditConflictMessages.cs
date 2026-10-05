namespace Nickelony.IDEKit.Core.Editing;

/// <summary>Maps conflict kinds to the message each reporting channel uses.</summary>
/// <remarks>
/// A preparation issue describes the edit that was rejected, while an <see cref="ArgumentException"/> names the input contract
/// that the batch violates.
/// </remarks>
internal static class TextEditConflictMessages
{
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a conflict kind.</exception>
	internal static string GetIssueMessage(TextEditPreparationIssueKind kind)
	{
		return kind switch
		{
			TextEditPreparationIssueKind.InsertionInsideReplacement => "An insertion starts inside a replacement range.",
			TextEditPreparationIssueKind.ReplacementOverlap => "The edit ranges overlap.",
			_ => throw new ArgumentOutOfRangeException(nameof(kind)),
		};
	}

	/// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a conflict kind.</exception>
	internal static string GetExceptionMessage(TextEditPreparationIssueKind kind)
	{
		return kind switch
		{
			TextEditPreparationIssueKind.InsertionInsideReplacement => "An insertion must not start inside a replacement range.",
			TextEditPreparationIssueKind.ReplacementOverlap => "Operations must not overlap.",
			_ => throw new ArgumentOutOfRangeException(nameof(kind)),
		};
	}
}
