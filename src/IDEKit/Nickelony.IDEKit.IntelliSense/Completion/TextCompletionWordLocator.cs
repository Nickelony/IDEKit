using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// Locates the word being typed at a caret offset within a document snapshot.
/// </summary>
/// <remarks>
/// The <see cref="TextCompletionSessionKernel"/> validates <paramref name="caretOffset"/> against
/// the snapshot before invoking the locator, so an implementation only ever sees in-range values.
/// The locator is skipped entirely when a call supplies an explicit
/// <see cref="TextCompletionWordSpan"/>, and it must be thread-safe when the kernel is shared between
/// callers.
/// </remarks>
/// <param name="snapshot">The document snapshot to inspect.</param>
/// <param name="caretOffset">The zero-based caret offset, already validated against the snapshot.</param>
/// <returns>The word being typed and the range it occupies.</returns>
public delegate TextCompletionWordSpan TextCompletionWordLocator(ITextSnapshot snapshot, int caretOffset);
