using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Contains the entries a lenient edit preparation accepted and the issues for the entries it
/// skipped.
/// </summary>
/// <remarks>
/// Produced by
/// <see cref="TextEditKernel.PrepareSkippingInvalidEntries(ITextSnapshot, IEnumerable{TextEditInput?})"/>.
/// Unlike a rejected <see cref="TextEditPreparationResult"/>, a skip result always carries a usable
/// batch - possibly empty when every entry was skipped - so a caller can apply <see cref="Edits"/>
/// without first testing whether anything was accepted. <see cref="Edits"/> is a
/// <see cref="PreparedTextEdits"/> batch, so it is already ordered and validated at construction;
/// apply or map it without re-validating. A skipped entry is reported once in
/// <see cref="SkippedIssues"/> and contributes no operation.
/// </remarks>
public sealed class TextEditSkipResult
{
	// A result that skipped nothing shares one immutable empty list instead of allocating a wrapper.
	private static readonly IReadOnlyList<TextEditPreparationIssue> s_emptySkippedIssues =
		Array.AsReadOnly(Array.Empty<TextEditPreparationIssue>());

	private TextEditSkipResult(
		PreparedTextEdits edits,
		IReadOnlyList<TextEditPreparationIssue> skippedIssues)
	{
		Edits = edits;

		// Issues are copied so the result cannot observe a later caller-side mutation; the empty case
		// shares the cached list because there is nothing to copy.
		SkippedIssues = skippedIssues.Count == 0 ? s_emptySkippedIssues : Array.AsReadOnly([.. skippedIssues]);
	}

	/// <summary>
	/// Creates a skip result from the accepted batch and the issues of the skipped entries.
	/// </summary>
	/// <param name="edits">The accepted, ordered operations; may be empty.</param>
	/// <param name="skippedIssues">The issues for the skipped entries, in caller order; may be empty.</param>
	/// <returns>The skip result.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="edits"/> or <paramref name="skippedIssues"/> is <see langword="null"/>.
	/// </exception>
	public static TextEditSkipResult Create(
		PreparedTextEdits edits,
		IReadOnlyList<TextEditPreparationIssue> skippedIssues)
	{
		ArgumentNullException.ThrowIfNull(edits);
		ArgumentNullException.ThrowIfNull(skippedIssues);

		return new TextEditSkipResult(edits, skippedIssues);
	}

	/// <summary>
	/// Gets the accepted operations, ordered from highest to lowest source offset and ready to apply
	/// in list order.
	/// </summary>
	public PreparedTextEdits Edits { get; }

	/// <summary>
	/// Gets one issue per skipped entry, in caller order; empty when no entry was skipped. Each issue
	/// names the skipped entry as <see cref="TextEditPreparationIssue.EditIndex"/> and, for a
	/// conflict, the earlier accepted entry as <see cref="TextEditPreparationIssue.RelatedEditIndex"/>.
	/// </summary>
	public IReadOnlyList<TextEditPreparationIssue> SkippedIssues { get; }

	/// <summary>
	/// Gets a value indicating whether at least one entry was skipped.
	/// </summary>
	public bool HasSkippedEntries => SkippedIssues.Count > 0;
}
