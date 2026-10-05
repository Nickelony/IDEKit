#if AVALONIAEDIT
using EditorControl = AvaloniaEdit.TextEditor;
#else
using EditorControl = ICSharpCode.AvalonEdit.TextEditor;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown;

#else

namespace Nickelony.IDEKit.AvalonEdit.Markdown;

#endif

/// <summary>Installs custom syntax highlighting on a newly created code-block editor before built-in resolution.</summary>
/// <param name="editor">The newly created editor.</param>
/// <param name="language">
/// The block's language: the first token of a fenced block's info string (attributes dropped; an empty string
/// when the fence has none), or <see langword="null"/> for an indented block or a direct editor creation.
/// </param>
/// <returns>
/// <see langword="true"/> when the callback claims the block and built-in resolution is skipped; <see langword="false"/> when built-in
/// resolution runs.
/// </returns>
/// <remarks>
/// <para>
/// The callback owns any state or resources it installs. When custom highlighting holds disposable
/// state (for example a TextMate model with a tokenizer thread), attach its cleanup to the editor's lifecycle, such as the editor's unloaded event;
/// the renderer never disposes host-installed state. A claimed block is not checked again, so an unresolved-language diagnostic is not logged for it.
/// </para>
/// <para>
/// A throwing callback produces the plain-text fallback
/// when it runs during rendering, under the failure policy in the <see cref="MarkdownRenderer"/> remarks; when a code-block editor is created
/// directly through <see cref="MarkdownRenderer.CreateCodeBlockEditor"/>, the exception propagates to the caller.
/// </para>
/// </remarks>
public delegate bool CodeBlockHighlightingInstaller(EditorControl editor, string? language);
