namespace Nickelony.IDEKit.LanguageFeatures.Editing;

/// <summary>Describes how the conversion of language-server text edits to a validated offset batch ended.</summary>
public enum TextEditConversionOutcome
{
	/// <summary>Every edit mapped to an offset range and the batch validated.</summary>
	Converted,

	/// <summary>An edit ends before it starts.</summary>
	InvalidRange,

	/// <summary>The edits overlap or otherwise conflict, so the batch is rejected in full.</summary>
	Conflict,
}
