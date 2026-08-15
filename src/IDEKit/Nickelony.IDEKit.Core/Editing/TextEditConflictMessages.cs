namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Maps conflict kinds to their messages so the preparation kernel (issues) and
/// <see cref="PreparedTextEdits"/> (constructor validation) report one rule set with one wording
/// per reporting channel.
/// </summary>
/// <remarks>
/// The two registers serve different audiences: a preparation issue describes the edit that
/// was rejected, while an <see cref="ArgumentException"/> names the input contract that the batch
/// violates. Both switches are exhaustive and throw for an unhandled kind, so a new conflict rule
/// cannot be reported with a fallback message.
/// </remarks>
internal static class TextEditConflictMessages
{
	/// <summary>
	/// Gets the preparation-issue message for the supplied conflict kind.
	/// </summary>
	/// <param name="kind">The conflict kind.</param>
	/// <returns>The issue message.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined value.</exception>
	internal static string GetIssueMessage(TextEditConflictKind kind)
		=> kind switch
		{
			TextEditConflictKind.InsertionInsideReplacement => "An insertion starts inside a replacement range.",
			TextEditConflictKind.ReplacementOverlap => "The edit ranges overlap.",
			_ => throw new ArgumentOutOfRangeException(nameof(kind)),
		};

	/// <summary>
	/// Gets the <see cref="ArgumentException"/> message for the supplied conflict kind in a
	/// hand-built batch.
	/// </summary>
	/// <param name="kind">The conflict kind.</param>
	/// <returns>The exception message.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined value.</exception>
	internal static string GetExceptionMessage(TextEditConflictKind kind)
		=> kind switch
		{
			TextEditConflictKind.InsertionInsideReplacement => "An insertion must not start inside a replacement range.",
			TextEditConflictKind.ReplacementOverlap => "Operations must not overlap.",
			_ => throw new ArgumentOutOfRangeException(nameof(kind)),
		};
}
