namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

/// <summary>Names the kinds of text a typed character can land in, which decide whether it opens completion.</summary>
/// <remarks>
/// A host answers one scope for an offset through the controller's scope hook, and a <see cref="CompletionTriggerPolicy"/> combines scopes to say
/// where typed word characters open completion.
/// </remarks>
[Flags]
public enum CompletionScopes
{
	/// <summary>
	/// No scope: as a policy's word scopes, no typed word character opens completion, and as the scope at the caret, no character does.
	/// </summary>
	None = 0,

	/// <summary>Source code outside comments and strings.</summary>
	Code = 1 << 0,

	/// <summary>The text of a comment.</summary>
	Comment = 1 << 1,

	/// <summary>The content of a string literal.</summary>
	StringLiteral = 1 << 2,
}
