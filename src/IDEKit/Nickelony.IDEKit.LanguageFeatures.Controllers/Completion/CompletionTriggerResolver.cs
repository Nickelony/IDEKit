namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

/// <summary>Decides how a typed character opens completion under a <see cref="CompletionTriggerPolicy"/>.</summary>
internal static class CompletionTriggerResolver
{
	/// <summary>Resolves how the typed character opens completion.</summary>
	/// <param name="typed">The character that was typed.</param>
	/// <param name="scope">The scope the character landed in, or <see cref="CompletionScopes.None"/> where completion is off.</param>
	/// <param name="triggerCharacters">The provider's trigger characters, compared verbatim with the typed character.</param>
	/// <param name="policy">The rule that decides which characters open completion, and in which scopes.</param>
	/// <returns>
	/// <see cref="CompletionTriggerMode.Immediate"/> for a trigger character that the policy admits in <paramref name="scope"/>,
	/// <see cref="CompletionTriggerMode.Scheduled"/> for a character that starts a word in one of the policy's word scopes,
	/// and <see cref="CompletionTriggerMode.None"/> otherwise, always for a scope of <see cref="CompletionScopes.None"/>. A trigger character
	/// that also starts a word resolves as a trigger character.
	/// </returns>
	internal static CompletionTriggerMode Resolve(
		char typed,
		CompletionScopes scope,
		IReadOnlyList<string> triggerCharacters,
		CompletionTriggerPolicy policy
	)
	{
		if (scope == CompletionScopes.None)
			return CompletionTriggerMode.None;

		bool isInWordScope = (policy.WordScopes & scope) != 0;

		if ((policy.TriggerCharactersInEveryScope || isInWordScope) && IsTriggerCharacter(typed, triggerCharacters))
			return CompletionTriggerMode.Immediate;

		return isInWordScope && policy.WordStart(typed) ? CompletionTriggerMode.Scheduled : CompletionTriggerMode.None;
	}

	private static bool IsTriggerCharacter(char typed, IReadOnlyList<string> triggerCharacters)
	{
		for (int index = 0; index < triggerCharacters.Count; index++)
		{
			if (triggerCharacters[index] is { Length: 1 } entry && entry[0] == typed)
				return true;
		}

		return false;
	}
}
