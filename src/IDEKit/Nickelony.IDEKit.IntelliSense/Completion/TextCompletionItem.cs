using Nickelony.IDEKit.Infrastructure;
using System.Diagnostics.CodeAnalysis;

namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// Represents one shared completion entry for an editor completion list.
/// </summary>
/// <remarks>
/// <para>
/// Only the label is required; every other field is optional and can be set with an object
/// initializer, for example
/// <c>new TextCompletionItem("foo") { Detail = "optional detail", Kind = TextCompletionItemKind.Method }</c>.
/// Hosts can omit the corresponding UI or commit behavior without requiring a language-specific
/// completion DTO. The built-in completion filter matches <see cref="FilterText"/>.
/// </para>
/// <para>
/// Request metadata (<see cref="RequestDocumentVersion"/>, <see cref="RequestGeneration"/>) and an
/// optional resolve callback support language-server lifecycles where item details or commit data
/// arrive only after the list is shown; hosts that complete synchronously can ignore them. This type
/// stores, normalizes, and copies data; applying request stamps or dropping a resolved edit payload
/// is done by the host through <see cref="WithRequestContext"/> and <see cref="WithoutTextEdit"/>.
/// </para>
/// <para>
/// The model projects an LSP completion item: <see cref="InsertText"/> is <c>insertText</c>,
/// <see cref="SortText"/> is <c>sortText</c>, <see cref="IsPreselected"/> is <c>preselect</c>,
/// <see cref="Tags"/> is <c>tags</c>, <see cref="CommitCharacters"/> is <c>commitCharacters</c>,
/// <see cref="AdditionalTextEdits"/> is <c>additionalTextEdits</c>, and the documentation, kind,
/// and detail members carry their same-named protocol fields. The type stores the projection
/// only; ranking, preselection, committing, and resolving stay host behavior.
/// </para>
/// </remarks>
public sealed class TextCompletionItem
{
	private readonly string _label;

	private string? _insertText;
	private string? _documentation;
	private string? _rawDocumentation;
	private double _priority;
	private string? _sortText;
	private bool _isPreselected;
	private TextCompletionItemKind? _kind;
	private string? _detail;
	private string? _filterText;
	private TextMarkupKind _documentationKind = TextMarkupKind.PlainText;
	private Func<CancellationToken, Task<TextCompletionItem>>? _resolveAsync;
	private TextCompletionTextEdit? _textEdit;
	private int? _requestDocumentVersion;
	private int? _requestGeneration;
	private TextCompletionInsertTextFormat? _insertTextFormat;
	private IReadOnlyList<TextCompletionTag> _tags = [];
	private IReadOnlyList<string> _commitCharacters = [];
	private IReadOnlyList<TextCompletionTextEdit> _additionalTextEdits = [];

	/// <summary>
	/// Initializes a new instance of the <see cref="TextCompletionItem"/> class that uses the supplied
	/// label and the default value for every optional field.
	/// </summary>
	/// <param name="label">The display label shown in the completion list.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="label"/> is <see langword="null"/>.
	/// </exception>
	public TextCompletionItem(string label)
	{
		ArgumentNullException.ThrowIfNull(label);

		_label = label;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="TextCompletionItem"/> class that copies the state
	/// of an existing item.
	/// </summary>
	/// <param name="source">The item whose state is copied.</param>
	private TextCompletionItem(TextCompletionItem source)
	{
		_label = source._label;
		_insertText = source._insertText;
		_documentation = source._documentation;
		_rawDocumentation = source._rawDocumentation;
		_priority = source._priority;
		_sortText = source._sortText;
		_isPreselected = source._isPreselected;
		_kind = source._kind;
		_detail = source._detail;
		_filterText = source._filterText;
		_documentationKind = source._documentationKind;
		_resolveAsync = source._resolveAsync;
		_textEdit = source._textEdit;
		_requestDocumentVersion = source._requestDocumentVersion;
		_requestGeneration = source._requestGeneration;
		_insertTextFormat = source._insertTextFormat;
		_tags = source._tags;
		_commitCharacters = source._commitCharacters;
		_additionalTextEdits = source._additionalTextEdits;
	}

	/// <summary>
	/// Gets the display label shown in the completion list.
	/// </summary>
	public string Label => _label;

	/// <summary>
	/// Gets the text inserted when the item is committed.
	/// </summary>
	/// <remarks>
	/// The value assigned by the producer, or <see cref="Label"/> when none was supplied; any other
	/// value is used as supplied, including a whitespace-only value. A <see langword="null"/>
	/// assignment is the absent state: the commit text falls back to the label, and
	/// <see cref="WithResolvedContent"/> may still fill it from a resolved item, so assign the label
	/// itself to pin the label fallback against a resolve merge. When
	/// <see cref="InsertTextFormat"/> is <see cref="TextCompletionInsertTextFormat.Snippet"/>, the text
	/// carries raw snippet syntax that a host expands with <see cref="TextSnippetExpander"/> before
	/// insertion. A commit path that sees an edit payload prefers the edit's replacement text; see
	/// <see cref="TextCompletionTextEdit"/> for the commit-text rule.
	/// </remarks>
	[AllowNull]
	public string InsertText
	{
		get
		{
			string? insertText = _insertText;

			return insertText ?? Label;
		}
		init => _insertText = value;
	}

	/// <summary>
	/// Gets optional documentation content shown for the item.
	/// </summary>
	/// <remarks>
	/// Blank values are treated as absent. The value is trimmed unless <see cref="DocumentationKind"/>
	/// is <see cref="TextMarkupKind.Markdown"/>, in which case it is passed through unchanged for
	/// Markdown rendering.
	/// </remarks>
	public string? Documentation
	{
		get => _documentation;
		init
		{
			_rawDocumentation = value;
			_documentation = NormalizeDocumentation(value, _documentationKind);
		}
	}

	/// <summary>
	/// Gets the producer-derived sort priority hint used to order the completion list.
	/// </summary>
	/// <remarks>
	/// The library applies no meaning to the value; hosts that order completion lists by one number
	/// conventionally surface higher values first. The default is zero. A producer derives the value
	/// (for example from protocol kind weights, scope hints, and response order) so that it agrees
	/// with the <see cref="SortText"/> value the producer assigns. <see cref="SortText"/> is the authoritative protocol
	/// ordering key; this value is the single-number alternative for hosts whose list cannot rank by
	/// sort text. The value must be finite.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is not finite.</exception>
	public double Priority
	{
		get => _priority;
		init
		{
			if (!double.IsFinite(value))
				throw new ArgumentOutOfRangeException(nameof(Priority), value, "The priority must be finite.");

			_priority = value;
		}
	}

	/// <summary>
	/// Gets the protocol sort text used to order this item relative to other items.
	/// </summary>
	/// <remarks>
	/// The value is stored as supplied and read as <see langword="null"/> when it is blank, because
	/// a blank value is treated as absent. The library stores the field only and never orders by it,
	/// so ordering stays host behavior: hosts that order by sort text rank by this value (falling
	/// back to the label when it is unset) and use <see cref="Priority"/> only when their list
	/// cannot rank by sort text.
	/// </remarks>
	public string? SortText
	{
		get
		{
			string? sortText = _sortText;

			return string.IsNullOrWhiteSpace(sortText) ? null : sortText;
		}
		init => _sortText = value;
	}

	/// <summary>
	/// Gets a value indicating whether the producer requested this item to be preselected.
	/// </summary>
	/// <remarks>
	/// The library stores the flag only; selecting and positioning the preselected item is host
	/// behavior. A producer's derived <see cref="Priority"/> may also reflect the flag, so
	/// <see cref="Priority"/> ordering alone does not identify it.
	/// </remarks>
	public bool IsPreselected
	{
		get => _isPreselected;
		init => _isPreselected = value;
	}

	/// <summary>
	/// Gets the semantic category used for icons and styling. Defaults to
	/// <see cref="TextCompletionItemKind.Generic"/> when no category was assigned; a
	/// <see langword="null"/> assignment resets the field to unset.
	/// </summary>
	[AllowNull]
	public TextCompletionItemKind Kind
	{
		get => _kind ?? TextCompletionItemKind.Generic;
		init => _kind = value;
	}

	/// <summary>
	/// Gets optional short detail text for host rendering.
	/// </summary>
	/// <remarks>
	/// Blank values are treated as absent and other values are trimmed.
	/// </remarks>
	public string? Detail
	{
		get => _detail;
		init => _detail = OptionalText.Normalize(value);
	}

	/// <summary>
	/// Gets the alternate text used to match the item. Falls back to <see cref="Label"/> when the
	/// value was never set or was assigned blank; a label that is itself blank is used as the
	/// fallback unchanged.
	/// </summary>
	/// <remarks>
	/// The value is stored without trimming because it is matching input a filter compares verbatim;
	/// only a blank value is treated as absent.
	/// </remarks>
	[AllowNull]
	public string FilterText
	{
		get => _filterText ?? Label;
		init => _filterText = string.IsNullOrWhiteSpace(value) ? null : value;
	}

	/// <summary>
	/// Gets how <see cref="Documentation"/> should be interpreted by a host. Defaults to
	/// <see cref="TextMarkupKind.PlainText"/>.
	/// </summary>
	/// <remarks>
	/// The value may be initialized before or after <see cref="Documentation"/>; the cached
	/// documentation is re-derived from the assigned value either way, so normalization does not
	/// depend on initializer order. The vocabulary is closed, so an undefined value is rejected
	/// instead of reaching a host renderer.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is not a defined <see cref="TextMarkupKind"/> value.</exception>
	public TextMarkupKind DocumentationKind
	{
		get => _documentationKind;
		init
		{
			if (!Enum.IsDefined(value))
				throw new ArgumentOutOfRangeException(nameof(DocumentationKind), value, "The documentation kind is not defined.");

			_documentationKind = value;
			_documentation = NormalizeDocumentation(_rawDocumentation, value);
		}
	}

	/// <summary>
	/// Gets the optional explicit edit payload used when committing the item.
	/// </summary>
	/// <remarks>
	/// The edit supersedes the plain insertion text; see <see cref="TextCompletionTextEdit"/> for
	/// the commit-text rule and the range semantics. When <see cref="InsertTextFormat"/> is
	/// <see cref="TextCompletionInsertTextFormat.Snippet"/>, the replacement text carries raw
	/// snippet syntax that a host expands with <see cref="TextSnippetExpander"/> before insertion.
	/// </remarks>
	public TextCompletionTextEdit? TextEdit
	{
		get => _textEdit;
		init => _textEdit = value;
	}

	/// <summary>
	/// Gets the additional annotations attached to the item, or an empty list when it has none.
	/// </summary>
	/// <remarks>
	/// Only <see cref="TextCompletionTag.Deprecated"/> is defined today, and unknown protocol values
	/// never reach the model because an undefined value in a direct assignment is rejected.
	/// Assigning <see langword="null"/> yields an empty list, and the stored value is a defensive
	/// read-only snapshot of the assigned list.
	/// <see cref="WithResolvedContent"/> merges tags as a union, so a deprecated mark adopted from a
	/// resolved item is never dropped.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">An assigned element is not a defined <see cref="TextCompletionTag"/> value.</exception>
	[AllowNull]
	public IReadOnlyList<TextCompletionTag> Tags
	{
		get => _tags;
		init
		{
			IReadOnlyList<TextCompletionTag> tags = Snapshot(value);

			for (int i = 0; i < tags.Count; i++)
			{
				if (!Enum.IsDefined(tags[i]))
					throw new ArgumentOutOfRangeException(nameof(Tags), tags[i], $"The tag at index {i} is not a defined completion tag.");
			}

			_tags = tags;
		}
	}

	/// <summary>
	/// Gets the characters that, when typed while the item is selected, accept it; an empty list
	/// when the item has none.
	/// </summary>
	/// <remarks>
	/// Entries are compared verbatim against the typed character by a host; protocol-conforming
	/// servers send single-character entries and non-conforming entries are preserved as supplied,
	/// while a <see langword="null"/> entry is
	/// rejected because comparing it against a typed character cannot succeed. Assigning
	/// <see langword="null"/> yields an empty list, and the stored value is a defensive read-only
	/// snapshot. <see cref="WithResolvedContent"/> adopts resolved characters only when this item has
	/// none.
	/// </remarks>
	/// <exception cref="ArgumentException">An assigned element is <see langword="null"/>.</exception>
	[AllowNull]
	public IReadOnlyList<string> CommitCharacters
	{
		get => _commitCharacters;
		init
		{
			IReadOnlyList<string> commitCharacters = Snapshot(value);

			for (int i = 0; i < commitCharacters.Count; i++)
			{
				if (commitCharacters[i] is null)
					throw new ArgumentException($"The commit character at index {i} must not be null.", nameof(CommitCharacters));
			}

			_commitCharacters = commitCharacters;
		}
	}

	/// <summary>
	/// Gets the secondary edits applied together with the item's commit, or an empty list when the
	/// item has none.
	/// </summary>
	/// <remarks>
	/// An entry is one secondary edit (for example an import insertion belonging to an auto-import
	/// completion). An entry that omits its replacement text is skipped by the commit path instead
	/// of failing the commit; protocol producers always supply one. A commit path applies the
	/// entries atomically with the primary insertion in one undo unit. Assigning
	/// <see langword="null"/> yields an empty list, and the stored value is a defensive read-only
	/// snapshot.
	/// <see cref="WithResolvedContent"/> adopts resolved edits only when this item has none.
	/// </remarks>
	[AllowNull]
	public IReadOnlyList<TextCompletionTextEdit> AdditionalTextEdits
	{
		get => _additionalTextEdits;
		init => _additionalTextEdits = Snapshot(value);
	}

	/// <summary>
	/// Gets the originating document version associated with the item for staleness checks.
	/// </summary>
	/// <remarks>
	/// Items that outlive the request pipeline (for example an open completion session whose entries
	/// are resolved asynchronously) are stamped with the document version and request generation they
	/// were produced for, so a host can reject or rebase them after the document changed. Use
	/// <see cref="WithRequestContext"/> to stamp or rebase a copy. The in-flight pipeline itself is
	/// superseded through the request-lifetime session (<see cref="TextCompletionRequestSession"/>),
	/// which is backed by the core request coordinator. A negative value is rejected.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative.</exception>
	public int? RequestDocumentVersion
	{
		get => _requestDocumentVersion;
		init => _requestDocumentVersion = ValidateStamp(value, nameof(RequestDocumentVersion));
	}

	/// <summary>
	/// Gets the originating request generation associated with the item for staleness checks.
	/// </summary>
	/// <remarks>
	/// The generation distinguishes repeated requests for the same document version, so a host can
	/// reject an item produced by a superseded request even though the document did not change.
	/// Stamps are applied and rebased through <see cref="WithRequestContext"/>; a negative value is
	/// rejected.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative.</exception>
	public int? RequestGeneration
	{
		get => _requestGeneration;
		init => _requestGeneration = ValidateStamp(value, nameof(RequestGeneration));
	}

	/// <summary>
	/// Gets how the commit text is interpreted. Defaults to
	/// <see cref="TextCompletionInsertTextFormat.PlainText"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// When the value is <see cref="TextCompletionInsertTextFormat.Snippet"/>, the text the commit
	/// path inserts (chosen per <see cref="TextCompletionTextEdit"/>) contains LSP snippet syntax
	/// with tabstops, placeholders, and escapes. Hosts expand it with
	/// <see cref="TextSnippetExpander"/> at commit time; the library never rewrites the text
	/// itself, so the raw payload stays available to every consumer.
	/// </para>
	/// <para>
	/// <see cref="WithResolvedContent"/> adopts the resolved item's format when the resolved item
	/// explicitly set one; an unset format behaves as absent, so a mere default never overrides,
	/// while an explicit assignment (including
	/// <see cref="TextCompletionInsertTextFormat.PlainText"/>) wins over the current value. The
	/// vocabulary is closed, so an undefined value is rejected instead of reaching a host commit
	/// path.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is not a defined <see cref="TextCompletionInsertTextFormat"/> value.</exception>
	public TextCompletionInsertTextFormat InsertTextFormat
	{
		get => _insertTextFormat ?? TextCompletionInsertTextFormat.PlainText;
		init
		{
			if (!Enum.IsDefined(value))
				throw new ArgumentOutOfRangeException(nameof(InsertTextFormat), value, "The insert-text format is not defined.");

			_insertTextFormat = value;
		}
	}

	/// <summary>
	/// Gets the optional asynchronous resolver for retrieving additional item details.
	/// </summary>
	/// <remarks>
	/// When no resolver is attached, <see cref="ResolveAsync"/> returns this item unchanged. Attach one
	/// with an object initializer or <see cref="WithResolveCallback"/>.
	/// </remarks>
	public Func<CancellationToken, Task<TextCompletionItem>>? ResolveCallback
	{
		get => _resolveAsync;
		init => _resolveAsync = value;
	}

	/// <summary>
	/// Gets a value indicating whether this item can be asynchronously resolved.
	/// </summary>
	public bool CanResolve => _resolveAsync is not null;

	/// <summary>
	/// Resolves the item to additional content when a resolve callback is available; otherwise, returns this item.
	/// </summary>
	/// <remarks>
	/// This is the raw resolve path: the resolved item is returned exactly as the callback supplied it,
	/// including any text-edit payload and request stamps it carries. Apply
	/// <see cref="WithoutTextEdit"/> and <see cref="WithRequestContext"/> to the result when a commit
	/// path must not use a resolved text-edit payload.
	/// </remarks>
	/// <param name="cancellationToken">A token that can cancel the resolve request.</param>
	/// <returns>
	/// The resolved completion item supplied by the resolve callback, or this item unchanged when no
	/// callback is attached.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	/// The resolve callback returned <see langword="null"/> or a <see langword="null"/> task.
	/// </exception>
	public Task<TextCompletionItem> ResolveAsync(CancellationToken cancellationToken = default)
	{
		Func<CancellationToken, Task<TextCompletionItem>>? resolveAsync = _resolveAsync;

		return resolveAsync is null
			? Task.FromResult(this)
			: ResolveRequiredAsync(resolveAsync, cancellationToken);
	}

	/// <summary>
	/// Creates a copy of the item with a resolve callback attached.
	/// </summary>
	/// <param name="resolveAsync">The resolve callback to attach.</param>
	/// <returns>A completion item with resolve support.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="resolveAsync"/> is <see langword="null"/>.
	/// </exception>
	public TextCompletionItem WithResolveCallback(Func<CancellationToken, Task<TextCompletionItem>> resolveAsync)
	{
		ArgumentNullException.ThrowIfNull(resolveAsync);

		TextCompletionItem item = new(this);
		item._resolveAsync = resolveAsync;

		return item;
	}

	/// <summary>
	/// Creates a copy stamped with request metadata used by hosts to reject stale completions.
	/// </summary>
	/// <remarks>
	/// Stamping is a data operation: only <see cref="RequestDocumentVersion"/> and
	/// <see cref="RequestGeneration"/> are set, and an item that already carries the supplied stamps
	/// is returned unchanged. Hosts that also keep text-edit data out of the commit path combine this
	/// with <see cref="WithoutTextEdit"/>, for example when a resolve response is used only to enrich
	/// presentation.
	/// </remarks>
	/// <param name="requestDocumentVersion">The originating document version; must not be negative.</param>
	/// <param name="requestGeneration">The originating request generation; must not be negative.</param>
	/// <returns>A completion item stamped with the supplied request context.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="requestDocumentVersion"/> or <paramref name="requestGeneration"/> is negative.
	/// </exception>
	public TextCompletionItem WithRequestContext(int requestDocumentVersion, int requestGeneration)
	{
		if (requestDocumentVersion < 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(requestDocumentVersion),
				requestDocumentVersion,
				"The request document version must not be negative.");
		}

		if (requestGeneration < 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(requestGeneration),
				requestGeneration,
				"The request generation must not be negative.");
		}

		if (RequestDocumentVersion == requestDocumentVersion
			&& RequestGeneration == requestGeneration)
		{
			return this;
		}

		TextCompletionItem item = new(this);
		item._requestDocumentVersion = requestDocumentVersion;
		item._requestGeneration = requestGeneration;

		return item;
	}

	/// <summary>
	/// Creates a copy without the explicit edit payload, so the commit uses the plain insertion text.
	/// </summary>
	/// <remarks>
	/// An item that has no text-edit payload is returned unchanged. Use this together with
	/// <see cref="WithRequestContext"/> when a commit path must use the plain insertion text even
	/// though an earlier resolve response supplied an edit payload.
	/// </remarks>
	/// <returns>A completion item without a text-edit payload.</returns>
	public TextCompletionItem WithoutTextEdit()
	{
		if (TextEdit is null)
			return this;

		TextCompletionItem item = new(this);
		item._textEdit = null;

		return item;
	}

	/// <summary>
	/// Creates a copy that merges richer resolved content onto the current item.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The resolved item's value wins for the display and metadata fields (detail, documentation with its
	/// kind, kind, and insert-text format); the commit and edit fields keep this item's value and fall back
	/// to <paramref name="resolvedItem"/> only when this item left them unset (a never-assigned value
	/// behaves as absent). A resolved item that contributes nothing returns this item.
	/// </para>
	/// <para>
	/// The per-field rules are the "Completion resolve lifecycle and merge rules" table in the package
	/// README, which is their single normative source. Identity fields (label, priority, preselect
	/// state, and request stamps) always come from this item, so a resolve response cannot retarget the
	/// originating request. The result carries no resolve callback because the resolved content has
	/// already been applied.
	/// </para>
	/// </remarks>
	/// <param name="resolvedItem">
	/// The resolved item carrying richer detail, description, kind, or commit data.
	/// </param>
	/// <returns>A merged completion item that preserves the original display and commit metadata.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="resolvedItem"/> is <see langword="null"/>.
	/// </exception>
	public TextCompletionItem WithResolvedContent(TextCompletionItem resolvedItem)
	{
		ArgumentNullException.ThrowIfNull(resolvedItem);

		return CompletionItemMerge.Apply(this, resolvedItem);
	}

	// The per-field merge policy of <see cref="WithResolvedContent"/>, nested so it can read the item's
	// private backing fields without widening them to the assembly.
	internal static class CompletionItemMerge
	{
		// Merges the resolved item's richer content into the original item, returning the original
		// instance unchanged when no field would change.
		public static TextCompletionItem Apply(TextCompletionItem item, TextCompletionItem resolvedItem)
		{
			string? detail = string.IsNullOrWhiteSpace(resolvedItem.Detail) ? item.Detail : resolvedItem.Detail;
			string? documentation = string.IsNullOrWhiteSpace(resolvedItem.Documentation) ? item.Documentation : resolvedItem.Documentation;

			// Carry the pre-normalization source of the chosen documentation alongside the normalized value, so a
			// later DocumentationKind change re-normalizes the same source instead of a value that was already
			// trimmed when the merge adopted it.
			string? rawDocumentation = string.IsNullOrWhiteSpace(resolvedItem.Documentation)
				? item._rawDocumentation
				: resolvedItem._rawDocumentation;

			TextMarkupKind documentationKind = string.IsNullOrWhiteSpace(resolvedItem.Documentation)
				? item.DocumentationKind
				: resolvedItem.DocumentationKind;

			TextCompletionItemKind kind = resolvedItem._kind ?? item.Kind;
			TextCompletionTextEdit? textEdit = item.TextEdit ?? resolvedItem.TextEdit;
			TextCompletionInsertTextFormat insertTextFormat = resolvedItem._insertTextFormat ?? item.InsertTextFormat;
			string? sortText = item.SortText ?? resolvedItem.SortText;
			IReadOnlyList<TextCompletionTag> tags = TextCompletionItem.MergeTags(item._tags, resolvedItem._tags);
			IReadOnlyList<string> commitCharacters = item._commitCharacters.Count == 0 ? resolvedItem._commitCharacters : item._commitCharacters;
			IReadOnlyList<TextCompletionTextEdit> additionalTextEdits = item._additionalTextEdits.Count == 0 ? resolvedItem._additionalTextEdits : item._additionalTextEdits;

			string? insertText = item.TextEdit is null && item._insertText is null ? resolvedItem._insertText : item._insertText;
			string? filterText = item._filterText ?? resolvedItem._filterText;

			if (string.Equals(detail, item.Detail, StringComparison.Ordinal)
				&& string.Equals(documentation, item.Documentation, StringComparison.Ordinal)
				&& documentationKind == item.DocumentationKind
				&& kind == item.Kind
				&& textEdit == item.TextEdit
				&& insertTextFormat == item.InsertTextFormat
				&& string.Equals(sortText, item.SortText, StringComparison.Ordinal)
				&& ReferenceEquals(tags, item._tags)
				&& ReferenceEquals(commitCharacters, item._commitCharacters)
				&& ReferenceEquals(additionalTextEdits, item._additionalTextEdits)
				&& string.Equals(insertText, item._insertText, StringComparison.Ordinal)
				&& string.Equals(filterText, item._filterText, StringComparison.Ordinal))
			{
				return item;
			}

			TextCompletionItem merged = new(item);
			merged._insertText = insertText;
			merged._documentation = documentation;
			merged._rawDocumentation = rawDocumentation;
			merged._documentationKind = documentationKind;
			merged._kind = kind;
			merged._detail = detail;
			merged._filterText = filterText;
			merged._textEdit = textEdit;
			merged._insertTextFormat = insertTextFormat;
			merged._sortText = sortText;
			merged._tags = tags;
			merged._commitCharacters = commitCharacters;
			merged._additionalTextEdits = additionalTextEdits;
			merged._resolveAsync = null;

			return merged;
		}
	}

	private static IReadOnlyList<T> Snapshot<T>(IReadOnlyList<T>? values)
		=> values is null || values.Count == 0 ? Array.Empty<T>() : Array.AsReadOnly([.. values]);

	private static IReadOnlyList<TextCompletionTag> MergeTags(IReadOnlyList<TextCompletionTag> current, IReadOnlyList<TextCompletionTag> resolved)
	{
		if (resolved.Count == 0)
			return current;

		var merged = new List<TextCompletionTag>(current);

		foreach (TextCompletionTag tag in resolved)
		{
			if (!merged.Contains(tag))
				merged.Add(tag);
		}

		return merged.Count == current.Count ? current : merged.AsReadOnly();
	}

	private static string? NormalizeDocumentation(string? documentation, TextMarkupKind documentationKind)
		=> string.IsNullOrWhiteSpace(documentation)
			? null
			: documentationKind == TextMarkupKind.Markdown
				? documentation
				: documentation.Trim();

	private static int? ValidateStamp(int? value, string parameterName)
		=> value is < 0
			? throw new ArgumentOutOfRangeException(parameterName, value, "The request stamp must not be negative.")
			: value;

	private static async Task<TextCompletionItem> ResolveRequiredAsync(
		Func<CancellationToken, Task<TextCompletionItem>> resolveAsync,
		CancellationToken cancellationToken)
	{
		Task<TextCompletionItem> resolveTask = resolveAsync(cancellationToken);

		if (resolveTask is null)
			throw new InvalidOperationException("The completion-item resolve callback returned a null task.");

		TextCompletionItem resolvedItem = await resolveTask.ConfigureAwait(false);

		if (resolvedItem is null)
			throw new InvalidOperationException("The completion-item resolve callback returned a null item.");

		return resolvedItem;
	}
}
