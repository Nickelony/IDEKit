using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Describes a single incremental <c>textDocument/didChange</c> range edit computed from the difference between the last-synced and newly-synced
/// document contents.
/// </summary>
/// <param name="Range">The replaced range in the last-synced content, as zero-based line and UTF-16 character positions.</param>
/// <param name="Text">The replacement text.</param>
internal readonly record struct DocumentChangeRange(TextPositionRange Range, string Text);
