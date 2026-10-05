using System.Collections.Frozen;
using Microsoft.Extensions.Logging;

namespace Nickelony.IDEKit.Markdown;

/// <summary>Configures how Markdown content is rendered and how its links and code blocks are handled.</summary>
/// <remarks>
/// <para>
/// Options instances are immutable. Assigned collections are copied and frozen on assignment, so later changes to an assigned collection
/// are not observed; record equality still compares the copied collection instances by reference, not their contents, so two options with equal
/// collection contents are not equal.
/// </para>
/// <para>
/// The options carry the host callbacks and the logger: the binding's <c>MarkdownRenderer</c> is a static facade, so each render call
/// supplies its own options, and those callbacks act on the host's behalf. The renderer performs no host-visible action of its own, such as opening
/// a hyperlink.
/// </para>
/// <para>
/// Interaction and scrolling behavior is captured when an element is created; changing an option later does not affect existing elements.
/// </para>
/// </remarks>
public sealed record MarkdownRenderOptions
{
	// The defaults are shared by every instance; an assigned collection replaces its field with a
	// frozen copy under the same case-insensitive semantics. The static fields precede Default because
	// static initializers run in textual order and Default captures them in its field initializers.
	private static readonly FrozenSet<string> s_defaultSupportedHyperlinkSchemes = FrozenSet.ToFrozenSet<string>(
		[Uri.UriSchemeHttp, Uri.UriSchemeHttps],
		StringComparer.OrdinalIgnoreCase
	);

	private static readonly FrozenDictionary<string, string> s_defaultHighlightingAliases = FrozenDictionary.ToFrozenDictionary<string, string>(
		[],
		StringComparer.OrdinalIgnoreCase
	);

	public static MarkdownRenderOptions Default { get; } = new();

	private readonly IReadOnlySet<string> _supportedHyperlinkSchemes = s_defaultSupportedHyperlinkSchemes;
	private readonly IReadOnlyDictionary<string, string> _highlightingAliases = s_defaultHighlightingAliases;

	/// <summary>Indicates whether content that exceeds the theme limits may scroll; defaults to <see langword="true"/>.</summary>
	/// <remarks>
	/// <para>
	/// When <see langword="true"/>, a viewer whose content exceeds <see cref="MarkdownRenderTheme.MaxHeight"/>
	/// shows a vertical scroll bar, and a code block that exceeds <see cref="MarkdownRenderTheme.MaxVisibleCodeBlockLines"/>
	/// scrolls inside the block. When <see langword="false"/>, rendered content cannot scroll
	/// through any input: scroll bars and keyboard scrolling are unavailable, and vertical overflow is clipped at the theme limit.
	/// Wheel chaining is governed by <see cref="AllowWheelChaining"/>, not by this flag. Rendered content never scrolls horizontally:
	/// content wider than <see cref="MarkdownRenderTheme.MaxWidth"/> is clipped.
	/// </para>
	/// <para>
	/// This governs content the renderer creates. A standalone code-block editor from <c>MarkdownRenderer.CreateCodeBlockEditor</c>
	/// follows the same policy for its vertical scroll bar, so <see langword="false"/> leaves it unscrollable; the height clamp stays host-owned,
	/// because a standalone editor is not clamped.
	/// </para>
	/// </remarks>
	public bool AllowScrolling { get; init; } = true;

	/// <summary>Indicates whether the created elements route mouse-wheel input; defaults to <see langword="true"/>.</summary>
	/// <remarks>
	/// When <see langword="true"/>, a wheel over a scrollable code block moves the block before the viewer, and a wheel the viewer
	/// cannot consume is handed to an enclosing host scroller. When <see langword="false"/>, no wheel handler is attached: the viewer keeps
	/// the toolkit's default behavior, which consumes a wheel event at the viewer even when the viewer cannot scroll it.
	/// </remarks>
	public bool AllowWheelChaining { get; init; } = true;

	/// <summary>
	/// Whether rendered content allows interaction: text selection in the rendered viewer and keyboard focus on openable hyperlinks;
	/// defaults to <see langword="false"/>.
	/// </summary>
	/// <remarks>
	/// When <see langword="false"/>, the rendered content is passive, matching tooltip semantics: the viewer does not allow
	/// text selection and hyperlinks are not keyboard-reachable. When <see langword="true"/>, hosts presenting content in a document-style surface
	/// can let users select text and reach openable hyperlinks with the keyboard. Rendered content is never editable, code-block interaction stays
	/// governed by <see cref="AllowCodeBlockSelection"/>, and inline code and code blocks are embedded controls, so their text stays
	/// outside the document's selection range. A link whose scheme is not allowed stays unfocusable, so interaction-enabled content adds no dead
	/// keyboard stops.
	/// </remarks>
	public bool AllowContentInteraction { get; init; }

	/// <summary>Whether a code block allows mouse text selection and text drag-and-drop; defaults to <see langword="false"/>.</summary>
	/// <remarks>
	/// Code blocks are never focusable and never accept keyboard input, so keyboard copy is not available; this controls mouse
	/// selection and drag-and-drop only. When <see langword="false"/>, code blocks are passive. When <see langword="true"/>, hosts presenting content
	/// outside a tooltip can let users select code with the mouse, drag it out, or copy it programmatically through the editor. A host that needs
	/// keyboard access presents its own editor from <c>MarkdownRenderer.CreateCodeBlockEditor</c> and makes it focusable itself, because the returned
	/// editor is host-owned.
	/// </remarks>
	public bool AllowCodeBlockSelection { get; init; }

	/// <summary>The URI schemes that may be opened from hyperlinks; defaults to HTTP and HTTPS.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	/// <remarks>
	/// The assigned set is copied and frozen on assignment and always compared case-insensitively, so later changes to the assigned collection
	/// are not observed and scheme casing never matters. Email autolinks (for example <c>&lt;user@example.com&gt;</c>) target <c>mailto:</c> and stay
	/// unopenable until the set allows the <c>mailto</c> scheme.
	/// </remarks>
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
	/// Additional highlighting aliases used when resolving a code-block language: keys are language identifiers taken from a fence's info string,
	/// and values are highlighting definition names or file extensions the editor recognizes; defaults to an empty map.
	/// </summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	/// <remarks>
	/// The assigned map is copied and frozen on assignment and always compared case-insensitively, so later changes to the assigned collection
	/// are not observed and key casing never matters. Aliases are consulted only when the language resolves neither as a highlighting definition
	/// name (matched case-insensitively) nor as a file extension. A fence name the engine already knows needs no alias, and a language the engine
	/// has no definition for resolves to no highlighting unless the host registers it; a host supplies host-owned definitions through the custom
	/// highlighting installer of the renderer. A host whose content uses fence names the engine does not know supplies its own aliases.
	/// </remarks>
	public IReadOnlyDictionary<string, string> HighlightingAliases
	{
		get => _highlightingAliases;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			_highlightingAliases = FrozenDictionary.ToFrozenDictionary(value, StringComparer.OrdinalIgnoreCase);
		}
	}

	/// <summary>The callback that opens a supported hyperlink; defaults to <see langword="null"/>.</summary>
	/// <remarks>
	/// The callback runs on the UI thread when a hyperlink whose scheme is listed in <see cref="SupportedHyperlinkSchemes"/> is activated.
	/// Return <see langword="true"/> when the link was handled. A <see langword="false"/> result or an exception
	/// counts as not handled and falls through to <see cref="OpenExternalUri"/>, except an out-of-memory or cancellation exception, which propagates.
	/// When neither callback handles the activation, the click is ignored; exclude a scheme from <see cref="SupportedHyperlinkSchemes"/> to keep
	/// its links from being treated as activatable at all. An exception that falls through is reported as a warning through <see cref="Logger"/>
	/// when one is set.
	/// </remarks>
	public Func<Uri, bool>? OpenHyperlink { get; init; }

	/// <summary>
	/// The callback that opens a supported hyperlink which <see cref="OpenHyperlink"/> did not handle, including the case
	/// where <see cref="OpenHyperlink"/> is not configured; defaults to <see langword="null"/>.
	/// </summary>
	/// <remarks>
	/// Hosts can route external links through their own service (for example an embedded browser or the operating system shell).
	/// This callback is the last step of the activation chain; see <see cref="OpenHyperlink"/> for the chain contract, which governs the result
	/// and exceptions of both callbacks.
	/// </remarks>
	public Func<Uri, bool>? OpenExternalUri { get; init; }

	/// <summary>An optional logger for rendering diagnostics; defaults to <see langword="null"/>, which does not log.</summary>
	/// <remarks>
	/// When set, rendering failures, hyperlink-open failures, and a code-block language that is left to built-in resolution and finds no highlighting
	/// are reported through it; the binding that reports an event declares its id on a <c>LoggerMessage</c> attribute. Without a logger,
	/// failures are not reported.
	/// </remarks>
	public ILogger? Logger { get; init; }
}
