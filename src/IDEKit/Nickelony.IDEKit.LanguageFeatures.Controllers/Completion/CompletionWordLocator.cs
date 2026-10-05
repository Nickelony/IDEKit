using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

/// <summary>Locates the word being typed at a caret offset within a document snapshot.</summary>
/// <param name="snapshot">The document snapshot to inspect.</param>
/// <param name="caretOffset">The zero-based caret offset, already validated against the snapshot.</param>
/// <returns>The word being typed and the range it occupies.</returns>
/// <remarks>
/// The locator is skipped entirely when a call supplies an explicit <see cref="CompletionWordSpan"/>, and it must be thread-safe when the kernel
/// is shared between callers.
/// </remarks>
public delegate CompletionWordSpan CompletionWordLocator(ITextSnapshot snapshot, int caretOffset);
