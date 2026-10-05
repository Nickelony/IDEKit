using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

/// <summary>Carries what a completion request is made from: the document text and the caret in it.</summary>
/// <param name="Snapshot">The document text the request is made for.</param>
/// <param name="CaretOffset">The zero-based caret offset within <paramref name="Snapshot"/>.</param>
/// <remarks>
/// The snapshot and the caret are captured together on the dispatcher's thread, so the provider reads the text the caret refers to.
/// </remarks>
public readonly record struct CompletionEditorContext(ITextSnapshot Snapshot, int CaretOffset);
