namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Commit;

/// <summary>The text a completion item inserts and where the caret lands inside it.</summary>
/// <param name="Text">The text to insert, with a snippet already expanded.</param>
/// <param name="CaretOffset">
/// The offset inside <paramref name="Text"/> where the caret lands, or <see langword="null"/> to place the caret after the inserted text.
/// </param>
public readonly record struct CompletionInsertion(string Text, int? CaretOffset);
