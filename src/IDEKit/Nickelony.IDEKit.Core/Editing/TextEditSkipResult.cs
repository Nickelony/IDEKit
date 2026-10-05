namespace Nickelony.IDEKit.Core.Editing;

/// <summary>Contains the entries a lenient edit preparation accepted and the issues for the entries it skipped.</summary>
/// <remarks>
/// Lenient preparation has no failure, so a skip result is created through <see cref="Success"/> only. It always carries a usable batch -
/// possibly empty when every entry was skipped - so a caller can apply <see cref="Edits"/> without first testing whether anything was accepted.
/// A skipped entry is reported once in <see cref="SkippedIssues"/> and contributes no operation.
/// </remarks>
public sealed class TextEditSkipResult
{
	// A result that skipped nothing shares one immutable empty list instead of allocating a wrapper.
	private static readonly IReadOnlyList<TextEditPreparationIssue> s_emptySkippedIssues = Array.AsReadOnly(Array.Empty<TextEditPreparationIssue>());

	private TextEditSkipResult(PreparedTextEdits edits, IReadOnlyList<TextEditPreparationIssue> skippedIssues)
	{
		Edits = edits;

		// Issues are copied so the result cannot observe a later caller-side mutation; the empty case
		// shares the cached list because there is nothing to copy.
		SkippedIssues = skippedIssues.Count == 0 ? s_emptySkippedIssues : Array.AsReadOnly([.. skippedIssues]);
	}

	/// <param name="edits">The accepted, ordered operations; may be empty.</param>
	/// <param name="skippedIssues">The issues for the skipped entries, in caller order; may be empty.</param>
	/// <exception cref="ArgumentNullException"><paramref name="edits"/> or <paramref name="skippedIssues"/> is <see langword="null"/>.</exception>
	public static TextEditSkipResult Success(PreparedTextEdits edits, IReadOnlyList<TextEditPreparationIssue> skippedIssues)
	{
		ArgumentNullException.ThrowIfNull(edits);
		ArgumentNullException.ThrowIfNull(skippedIssues);

		return new TextEditSkipResult(edits, skippedIssues);
	}

	/// <summary>The accepted operations, ordered from highest to lowest source offset and ready to apply in list order.</summary>
	public PreparedTextEdits Edits { get; }

	/// <summary>One issue per skipped entry, in caller order; empty when no entry was skipped.</summary>
	/// <remarks>
	/// Each issue names the skipped entry as <see cref="TextEditPreparationIssue.EditIndex"/> and, for a conflict, the earlier accepted entry
	/// as <see cref="TextEditPreparationIssue.RelatedEditIndex"/>.
	/// </remarks>
	public IReadOnlyList<TextEditPreparationIssue> SkippedIssues { get; }

	public bool HasSkippedEntries => SkippedIssues.Count > 0;
}
