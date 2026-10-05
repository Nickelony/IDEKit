namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Commit;

/// <summary>Decides whether a typed character commits the selected completion item.</summary>
/// <remarks>
/// A binding reads the input event, the open window, and the selection from its engine, asks this policy, and performs the insertion itself.
/// </remarks>
public static class CompletionCommitPolicy
{
	/// <summary>Decides whether the typed text commits the selected item.</summary>
	/// <param name="acceptOnCommitCharacters">
	/// Whether commit characters are accepted at all (the <c>AcceptOnCommitCharacters</c> option of a binding's presentation options).
	/// </param>
	/// <param name="isHandled">Whether another handler already handled the input event.</param>
	/// <param name="typedText">The text the input event carries.</param>
	/// <param name="selected">The selected item when it declares commit characters, or <see langword="null"/> for any other selection.</param>
	/// <returns>
	/// <see langword="true"/> when the typed text is one character
	/// that the selected item lists, compared ordinally; otherwise, <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// Commit characters are single characters, so any other input, for example a multi-character composition result, never commits.
	/// </remarks>
	public static bool ShouldCommit(bool acceptOnCommitCharacters, bool isHandled, string? typedText, ICompletionCommitCharacters? selected)
	{
		if (!acceptOnCommitCharacters || isHandled)
			return false;

		if (typedText is not { Length: 1 })
			return false;

		if (selected is null)
			return false;

		IReadOnlyList<string> commitCharacters = selected.CommitCharacters;

		for (int index = 0; index < commitCharacters.Count; index++)
		{
			if (string.Equals(commitCharacters[index], typedText, StringComparison.Ordinal))
				return true;
		}

		return false;
	}
}
