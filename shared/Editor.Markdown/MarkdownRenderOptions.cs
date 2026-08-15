using Microsoft.Extensions.Logging;
using System.Collections.Frozen;
#if AVALONIAEDIT
using AvalonTextEditor = AvaloniaEdit.TextEditor;
#else
using AvalonTextEditor = ICSharpCode.AvalonEdit.TextEditor;
#endif

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Markdown;
#else
namespace Nickelony.IDEKit.AvalonEdit.Markdown;
#endif

/// <summary>
/// Configures how Markdown content is rendered and how its links and code blocks are handled.
/// </summary>
/// <remarks>
/// <para>
/// Options instances are immutable. Assigned collections are copied and frozen on assignment, so
/// later changes to an assigned collection are not observed; record equality still compares the
/// copied collection instances by reference, not their contents, so two options with equal
/// collection contents are not equal.
/// </para>
/// <para>
/// The options also carry <see cref="OpenHyperlink"/>, <see cref="OpenExternalUri"/>,
/// <see cref="CustomHighlightingInstaller"/>, and <see cref="Logger"/>: <see cref="MarkdownRenderer"/>
/// is a static facade, so each render call supplies its own options. These callbacks act on the host's
/// behalf; the renderer performs no host-visible action of its own, such as launching a process to
/// open a hyperlink.
/// </para>
/// <para>
/// Interaction and scrolling behavior is captured when an element is created; changing an option
/// later does not affect existing elements.
/// </para>
/// </remarks>
public sealed record MarkdownRenderOptions
{
	// The defaults are shared by every instance; an assigned collection replaces its field with a
	// frozen copy under the same case-insensitive semantics. The static fields precede Default because
	// static initializers run in textual order and Default captures them in its field initializers.
	private static readonly FrozenSet<string> s_defaultSupportedHyperlinkSchemes = FrozenSet.ToFrozenSet<string>(
		[Uri.UriSchemeHttp, Uri.UriSchemeHttps],
		StringComparer.OrdinalIgnoreCase);

	private static readonly FrozenDictionary<string, string> s_defaultHighlightingAliases = FrozenDictionary.ToFrozenDictionary<string, string>(
		[],
		StringComparer.OrdinalIgnoreCase);

	/// <summary>Gets the default rendering options.</summary>
	public static MarkdownRenderOptions Default { get; } = new();

	private readonly IReadOnlySet<string> _supportedHyperlinkSchemes = s_defaultSupportedHyperlinkSchemes;
	private readonly IReadOnlyDictionary<string, string> _highlightingAliases = s_defaultHighlightingAliases;

	/// <summary>
	/// Gets or initializes whether content that exceeds the theme limits may scroll. Defaults to
	/// <see langword="true"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// When <see langword="true"/>, a viewer whose content exceeds
	/// <see cref="MarkdownRenderTheme.MaxHeight"/> shows a vertical scroll bar, and a code block that
	/// exceeds <see cref="MarkdownRenderTheme.MaxVisibleCodeBlockLines"/> scrolls inside the block.
	/// When <see langword="false"/>, content the renderer produces cannot scroll through any input (wheel,
	/// scroll bars, or keyboard) - vertical overflow is clipped at the theme limit - and, when wheel chaining
	/// is enabled, the mouse wheel is forwarded to an enclosing host scroller. Rendered content never
	/// scrolls horizontally: content wider than <see cref="MarkdownRenderTheme.MaxWidth"/> is clipped.
	/// </para>
	/// <para>
	/// This governs content the renderer creates; a standalone code-block editor from
	/// <see cref="MarkdownRenderer.CreateCodeBlockEditor"/> keeps its scroll-bar visibility and height clamp
	/// host-owned, so <see langword="false"/> reaches it through the wheel-routing gate instead.
	/// </para>
	/// </remarks>
	public bool AllowScrolling { get; init; } = true;

	/// <summary>
	/// Gets or initializes whether the created elements route mouse-wheel input. Defaults to
	/// <see langword="true"/>.
	/// </summary>
	/// <remarks>
	/// When <see langword="true"/>, a wheel over a scrollable code block moves the block before the
	/// viewer, and a wheel the viewer cannot consume is handed to an enclosing host scroller. When
	/// <see langword="false"/>, no wheel handler is attached: the viewer keeps the toolkit's default
	/// behavior, which consumes a wheel event at the viewer even when the viewer cannot scroll it.
	/// </remarks>
	public bool AllowWheelChaining { get; init; } = true;

	/// <summary>
	/// Gets or initializes whether rendered content allows interaction: text selection in the rendered
	/// viewer and keyboard focus on openable hyperlinks. Defaults to <see langword="false"/>.
	/// </summary>
	/// <remarks>
	/// When <see langword="false"/>, the rendered content is passive, matching tooltip semantics: the
	/// viewer does not allow text selection and hyperlinks are not keyboard-reachable. When
	/// <see langword="true"/>, hosts presenting content in a document-style surface can let users select
	/// text and reach openable hyperlinks with the keyboard. Rendered content is never editable,
	/// code-block interaction stays governed by <see cref="AllowCodeBlockSelection"/>, and inline code
	/// and code blocks are embedded controls, so their text stays outside the document's selection range.
	/// A link whose scheme is not allowed stays unfocusable, so interaction-enabled content adds no dead
	/// keyboard stops.
	/// </remarks>
	public bool AllowContentInteraction { get; init; }

	/// <summary>
	/// Gets or initializes whether a code block allows mouse text selection and text drag-and-drop.
	/// Defaults to <see langword="false"/>.
	/// </summary>
	/// <remarks>
	/// Code blocks are never focusable and never accept keyboard input, so keyboard copy is not
	/// available; this controls mouse selection and drag-and-drop only. When <see langword="false"/>,
	/// code blocks are passive. When <see langword="true"/>, hosts presenting content outside a tooltip
	/// can let users select code with the mouse, drag it out, or copy it programmatically through the
	/// editor. A host that needs keyboard access presents its own editor from
	/// <see cref="MarkdownRenderer.CreateCodeBlockEditor"/> and makes it focusable itself, because the
	/// returned editor is host-owned.
	/// </remarks>
	public bool AllowCodeBlockSelection { get; init; }

	/// <summary>
	/// Gets or initializes the URI schemes that may be opened from hyperlinks. Defaults to HTTP and
	/// HTTPS.
	/// </summary>
	/// <remarks>
	/// The assigned set is copied and frozen on assignment and always compared case-insensitively, so
	/// later changes to the assigned collection are not observed and scheme casing never matters.
	/// Email autolinks (for example <c>&lt;user@example.com&gt;</c>) target <c>mailto:</c> and stay
	/// unopenable until the set allows the <c>mailto</c> scheme.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// The assigned value is <see langword="null"/>.
	/// </exception>
	public IReadOnlySet<string> SupportedHyperlinkSchemes
	{
		get => _supportedHyperlinkSchemes;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			_supportedHyperlinkSchemes = FrozenSet.ToFrozenSet(value, StringComparer.OrdinalIgnoreCase);
		}
	}

	/// <summary>
	/// Gets or initializes additional highlighting aliases used when resolving a code-block language.
	/// Keys are language identifiers taken from a fence's info string; values are highlighting
	/// definition names or file extensions understood by the editor engine. Defaults to an empty map.
	/// </summary>
	/// <remarks>
	/// The assigned map is copied and frozen on assignment and always compared case-insensitively, so
	/// later changes to the assigned collection are not observed and key casing never matters.
	/// Aliases are consulted only when the language resolves neither as a highlighting definition name
	/// (matched case-insensitively) nor as a file extension. Languages whose fence name already matches
	/// an engine name or extension (for example <c>python</c>, <c>javascript</c>, <c>cs</c>, or
	/// <c>js</c>) need no alias, and a language the engine has no definition for (for example <c>lua</c>
	/// or <c>ts</c>) resolves to no highlighting unless the host registers it; use
	/// <see cref="CustomHighlightingInstaller"/> for host-owned definitions. A host whose content uses
	/// fence names the engine does not know (for example <c>json5</c>) supplies its own aliases, such as
	/// <c>json5</c> to <c>.json</c>.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// The assigned value is <see langword="null"/>.
	/// </exception>
	public IReadOnlyDictionary<string, string> HighlightingAliases
	{
		get => _highlightingAliases;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			_highlightingAliases = FrozenDictionary.ToFrozenDictionary(value, StringComparer.OrdinalIgnoreCase);
		}
	}

	/// <summary>
	/// Gets or initializes a callback that can install custom syntax highlighting on a code-block editor.
	/// The renderer invokes the callback before built-in highlighting resolution with the newly created
	/// editor and the block's language: the first token of the fence info string for a fenced block
	/// (fence attributes are dropped; an empty string when the fence has none), or
	/// <see langword="null"/> when no language was given (an indented block or a direct editor
	/// creation). Returning <see langword="true"/> means the callback handled highlighting and skips
	/// built-in resolution; returning <see langword="false"/> uses built-in resolution instead. Defaults
	/// to <see langword="null"/>, which always uses built-in resolution.
	/// </summary>
	/// <remarks>
	/// The callback owns any state or resources it installs. When custom highlighting holds disposable
	/// state (for example a TextMate model with a tokenizer thread), attach its cleanup to the editor's
	/// lifecycle, such as the editor's unloaded event; the renderer never disposes host-installed state.
	/// A claimed block is not checked again, so an unresolved-language diagnostic is not logged for it.
	/// A throwing callback produces the plain-text fallback when it runs during rendering; when a
	/// code-block editor is created directly through <see cref="MarkdownRenderer.CreateCodeBlockEditor"/>,
	/// the exception propagates to the caller.
	/// </remarks>
	public Func<AvalonTextEditor, string?, bool>? CustomHighlightingInstaller { get; init; }

	/// <summary>
	/// Gets or initializes a callback that opens a supported hyperlink. Defaults to
	/// <see langword="null"/>.
	/// </summary>
	/// <remarks>
	/// The callback runs on the UI thread when a hyperlink whose scheme is listed in
	/// <see cref="SupportedHyperlinkSchemes"/> is activated. Return <see langword="true"/> when the
	/// link was handled. A <see langword="false"/> result or an exception counts as not handled and
	/// falls through to <see cref="OpenExternalUri"/>. When neither callback handles the activation,
	/// the click is ignored; exclude a scheme from <see cref="SupportedHyperlinkSchemes"/> to keep its
	/// links from being treated as activatable at all. Exceptions are reported as a warning. Defaults
	/// to <see langword="null"/>.
	/// </remarks>
	public Func<Uri, bool>? OpenHyperlink { get; init; }

	/// <summary>
	/// Gets or initializes the callback used to open a supported hyperlink that
	/// <see cref="OpenHyperlink"/> did not handle, which includes the case where
	/// <see cref="OpenHyperlink"/> is not configured. Defaults to <see langword="null"/>.
	/// </summary>
	/// <remarks>
	/// Hosts can route external links through their own service (for example an embedded browser or the
	/// operating system shell). This callback is the last step of the activation chain; see
	/// <see cref="OpenHyperlink"/> for the chain contract. Exceptions are reported as a warning.
	/// </remarks>
	public Func<Uri, bool>? OpenExternalUri { get; init; }

	/// <summary>
	/// Gets or initializes an optional logger for rendering diagnostics. Defaults to
	/// <see langword="null"/>, which does not log.
	/// </summary>
	/// <remarks>
	/// When set, rendering failures, hyperlink-open failures, and a code-block language that is left to
	/// built-in resolution and finds no highlighting are reported through it; the events use the
	/// package's allocated id block (2000, 2001, and 2002), which the package README documents. Without a
	/// logger, failures are not reported.
	/// </remarks>
	public ILogger? Logger { get; init; }
}
