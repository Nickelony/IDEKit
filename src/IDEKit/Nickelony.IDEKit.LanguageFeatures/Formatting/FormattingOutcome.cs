namespace Nickelony.IDEKit.LanguageFeatures.Formatting;

/// <summary>How a provider-backed document formatting ended.</summary>
public enum FormattingOutcome
{
	/// <summary>The provider's edits were applied to the editor's document.</summary>
	Applied,

	/// <summary>The provider declined, returned no edits for the document, or returned edits that change nothing.</summary>
	NothingToApply,

	/// <summary>The document changed while the provider was working, so its edits do not fit the new text and none were applied.</summary>
	Stale,

	/// <summary>
	/// The provider's edits could not be applied together, because one lies outside the document or two conflict, so none were applied.
	/// </summary>
	Rejected,
}
