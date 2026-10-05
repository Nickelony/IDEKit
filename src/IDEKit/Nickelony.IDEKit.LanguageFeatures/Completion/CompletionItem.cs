using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Core.Validation;
using Nickelony.IDEKit.LanguageFeatures.Markup;

namespace Nickelony.IDEKit.LanguageFeatures.Completion;

/// <summary>Represents one shared completion entry for an editor completion list.</summary>
/// <remarks>
/// Only the label is required; set every other field with an object initializer. The item is a protocol-agnostic payload that stores, normalizes,
/// and copies the producer's values only, so ranking, preselection, committing, and resolving stay host behavior. An item can also carry request
/// stamps and a resolve callback so a provider can deliver item details or commit data after the list is shown.
/// </remarks>
public sealed class CompletionItem
{
	internal string? _insertText;
	internal MarkupContent? _documentation;

	private double _priority;
	internal string? _sortText;
	internal CompletionItemKind? _kind;

	internal string? _detail;
	internal string? _filterText;

	internal Func<CancellationToken, Task<CompletionItem>>? _resolveAsync;
	internal CompletionTextEdit? _textEdit;

	private int? _requestDocumentVersion;
	private int? _requestGeneration;

	internal CompletionInsertTextFormat? _insertTextFormat;
	internal IReadOnlyList<CompletionItemTag> _tags = [];
	internal IReadOnlyList<string> _commitCharacters = [];
	internal IReadOnlyList<CompletionTextEdit> _additionalTextEdits = [];

	/// <summary>
	/// Initializes a new instance of the <see cref="CompletionItem"/> class that uses the supplied label and the default value
	/// for every optional field.
	/// </summary>
	/// <param name="label">The display label shown in the completion list.</param>
	/// <exception cref="ArgumentNullException"><paramref name="label"/> is <see langword="null"/>.</exception>
	public CompletionItem(string label)
	{
		ArgumentNullException.ThrowIfNull(label);
		Label = label;
	}

	/// <summary>Initializes a new instance of the <see cref="CompletionItem"/> class that copies the state of an existing item.</summary>
	/// <param name="source">The item whose state is copied.</param>
	internal CompletionItem(CompletionItem source)
	{
		Label = source.Label;
		_insertText = source._insertText;
		_documentation = source._documentation;
		_priority = source._priority;
		_sortText = source._sortText;
		IsPreselected = source.IsPreselected;
		_kind = source._kind;
		_detail = source._detail;
		_filterText = source._filterText;
		_resolveAsync = source._resolveAsync;
		_textEdit = source._textEdit;
		_requestDocumentVersion = source._requestDocumentVersion;
		_requestGeneration = source._requestGeneration;
		_insertTextFormat = source._insertTextFormat;
		_tags = source._tags;
		_commitCharacters = source._commitCharacters;
		_additionalTextEdits = source._additionalTextEdits;
	}

	/// <summary>The display label shown in the completion list.</summary>
	public string Label { get; }

	/// <summary>The text inserted when the item is committed.</summary>
	/// <remarks>
	/// Falls back to <see cref="Label"/> when unset or assigned <see langword="null"/>; any other value,
	/// including a whitespace-only one, is used as supplied. When <see cref="InsertTextFormat"/> is <see cref="CompletionInsertTextFormat.Snippet"/>,
	/// the text carries raw snippet syntax that a host expands before insertion; <see cref="CompletionTextEdit"/> owns the commit-text rule.
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

	/// <summary>The optional documentation content shown for the item, with the markup kind that decides how a host renders it.</summary>
	/// <remarks>
	/// Blank content is treated as absent.
	/// </remarks>
	public MarkupContent? Documentation
	{
		get => _documentation;
		init => _documentation = value is { IsBlank: true } ? null : value;
	}

	/// <summary>The producer-derived sort priority hint for the completion list.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is not finite.</exception>
	/// <remarks>
	/// The library stores the value only; a host that ranks by it conventionally surfaces higher values first, and the default is zero.
	/// <see cref="SortText"/> is the authoritative ordering key, and this value is the single-number alternative for a host whose list cannot rank
	/// by sort text.
	/// </remarks>
	public double Priority
	{
		get => _priority;
		init => _priority = NumericValidation.Finite(value, nameof(Priority));
	}

	/// <summary>The protocol sort text that orders this item relative to other items.</summary>
	/// <remarks>
	/// Stored as supplied and read as <see langword="null"/> when blank. The library never orders by it: a host ranks by this value,
	/// falling back to the label when it is unset, and uses <see cref="Priority"/> only when its list cannot rank by sort text.
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

	/// <summary>Indicates whether the producer requested this item to be preselected.</summary>
	/// <remarks>
	/// The library stores the flag only; selecting and positioning the preselected item is host behavior. A producer's derived <see cref="Priority"/>
	/// may also reflect the flag, so <see cref="Priority"/> ordering alone does not identify it.
	/// </remarks>
	public bool IsPreselected { get; init; }

	/// <summary>The item's semantic category.</summary>
	/// <remarks>
	/// Defaults to <see cref="CompletionItemKind.Generic"/> when no category was assigned; a <see langword="null"/> assignment resets the field
	/// to unset.
	/// </remarks>
	[AllowNull]
	public CompletionItemKind Kind
	{
		get => _kind ?? CompletionItemKind.Generic;
		init => _kind = value;
	}

	/// <summary>The optional short detail text for host rendering.</summary>
	/// <remarks>
	/// Blank values are treated as absent and other values are trimmed.
	/// </remarks>
	public string? Detail
	{
		get => _detail;
		init => _detail = OptionalTextNormalizer.Normalize(value);
	}

	/// <summary>The alternate text that matches the item.</summary>
	/// <remarks>
	/// Falls back to <see cref="Label"/> when unset or assigned blank, and the label is used unchanged even when it is blank. Stored without trimming
	/// because a filter compares it verbatim, and only a blank value is treated as absent.
	/// </remarks>
	[AllowNull]
	public string FilterText
	{
		get => _filterText ?? Label;
		init => _filterText = string.IsNullOrWhiteSpace(value) ? null : value;
	}

	/// <summary>The optional explicit edit payload used when committing the item.</summary>
	/// <remarks>
	/// The edit supersedes the plain insertion text; <see cref="CompletionTextEdit"/> owns the commit-text rule and the range semantics.
	/// When <see cref="InsertTextFormat"/> is <see cref="CompletionInsertTextFormat.Snippet"/>, the replacement text is raw snippet syntax
	/// that a host expands before insertion.
	/// </remarks>
	public CompletionTextEdit? TextEdit
	{
		get => _textEdit;
		init => _textEdit = value;
	}

	/// <summary>The additional annotations attached to the item, or an empty list when it has none.</summary>
	/// <exception cref="ArgumentOutOfRangeException">An assigned element is not a defined <see cref="CompletionItemTag"/> value.</exception>
	/// <remarks>
	/// Assigning <see langword="null"/> yields an empty list, and the stored value is a defensive read-only snapshot.
	/// <see cref="WithResolvedContent"/> merges tags as a union, so a mark adopted from a resolved item is never dropped.
	/// </remarks>
	[AllowNull]
	public IReadOnlyList<CompletionItemTag> Tags
	{
		get => _tags;
		init
		{
			ReadOnlyCollection<CompletionItemTag> tags = CollectionSnapshots.Capture(value, "tags", nameof(Tags));

			for (int i = 0; i < tags.Count; i++)
			{
				if (!Enum.IsDefined(tags[i]))
					throw new ArgumentOutOfRangeException(nameof(Tags), tags[i], $"The tag at index {i} is not a defined completion tag.");
			}

			_tags = tags;
		}
	}

	/// <summary>The characters that, when typed while the item is selected, accept it; an empty list when the item has none.</summary>
	/// <exception cref="ArgumentException">An assigned element is <see langword="null"/>.</exception>
	/// <remarks>
	/// The host owns the commit-character matching rule. Assigning <see langword="null"/> yields an empty list, and the stored value
	/// is a defensive read-only snapshot; <see cref="WithResolvedContent"/> adopts resolved characters only when this item has none.
	/// </remarks>
	[AllowNull]
	public IReadOnlyList<string> CommitCharacters
	{
		get => _commitCharacters;
		init => _commitCharacters = CollectionSnapshots.Capture(value, "commit characters", nameof(CommitCharacters));
	}

	/// <summary>The secondary edits applied together with the item's commit, or an empty list when the item has none.</summary>
	/// <remarks>
	/// An entry that omits its replacement text is skipped instead of failing the commit. The commit path applies the entries atomically
	/// with the primary insertion in one undo unit; assigning <see langword="null"/> yields an empty list and stores a defensive read-only snapshot,
	/// and <see cref="WithResolvedContent"/> adopts resolved edits only when this item has none.
	/// </remarks>
	[AllowNull]
	public IReadOnlyList<CompletionTextEdit> AdditionalTextEdits
	{
		get => _additionalTextEdits;
		init => _additionalTextEdits = CollectionSnapshots.Capture(value, "additional text edits", nameof(AdditionalTextEdits));
	}

	/// <summary>The originating document version associated with the item for staleness checks.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative.</exception>
	/// <remarks>
	/// Stamped by <see cref="WithRequestContext"/> so a host can reject or rebase an item that outlived the pipeline.
	/// </remarks>
	public int? RequestDocumentVersion
	{
		get => _requestDocumentVersion;
		init => _requestDocumentVersion = ValidateStamp(value, nameof(RequestDocumentVersion));
	}

	/// <summary>The originating request generation associated with the item for staleness checks.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative.</exception>
	/// <remarks>
	/// Distinguishes repeated requests for the same document version, so a host can reject an item produced by a superseded request
	/// even though the document did not change. Stamps are applied and rebased through <see cref="WithRequestContext"/>.
	/// </remarks>
	public int? RequestGeneration
	{
		get => _requestGeneration;
		init => _requestGeneration = ValidateStamp(value, nameof(RequestGeneration));
	}

	/// <summary>The format in which the host interprets the commit text.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is not a defined <see cref="CompletionInsertTextFormat"/> value.</exception>
	/// <remarks>
	/// Defaults to <see cref="CompletionInsertTextFormat.PlainText"/>. When the value is <see cref="CompletionInsertTextFormat.Snippet"/>,
	/// the commit text is raw snippet syntax that a host expands before insertion.
	/// </remarks>
	public CompletionInsertTextFormat InsertTextFormat
	{
		get => _insertTextFormat ?? CompletionInsertTextFormat.PlainText;
		init
		{
			if (!Enum.IsDefined(value))
				throw new ArgumentOutOfRangeException(nameof(InsertTextFormat), value, "The insert-text format is not defined.");

			_insertTextFormat = value;
		}
	}

	/// <summary>The optional asynchronous resolver for retrieving additional item details.</summary>
	/// <remarks>
	/// When no resolver is attached, <see cref="ResolveAsync"/> returns this item unchanged. Attach one with an object initializer
	/// or <see cref="WithResolveCallback"/>.
	/// </remarks>
	public Func<CancellationToken, Task<CompletionItem>>? ResolveCallback
	{
		get => _resolveAsync;
		init => _resolveAsync = value;
	}

	/// <summary>Whether a resolve callback is attached, so <see cref="ResolveAsync"/> can return more than this item.</summary>
	public bool CanResolve => _resolveAsync is not null;

	/// <summary>Resolves the item to additional content when a resolve callback is available; otherwise, returns this item.</summary>
	/// <param name="cancellationToken">A token that can cancel the resolve request.</param>
	/// <returns>The resolved completion item supplied by the resolve callback, or this item unchanged when no callback is attached.</returns>
	/// <exception cref="InvalidOperationException">The resolve callback returned <see langword="null"/> or a <see langword="null"/> task.</exception>
	/// <remarks>
	/// This is the raw resolve path: the resolved item is returned exactly as the callback supplied it, including any text-edit payload and request
	/// stamps it carries. Apply <see cref="WithoutTextEdit"/> and <see cref="WithRequestContext"/> to the result when a commit path must not use
	/// a resolved text-edit payload.
	/// </remarks>
	public Task<CompletionItem> ResolveAsync(CancellationToken cancellationToken = default)
	{
		Func<CancellationToken, Task<CompletionItem>>? resolveAsync = _resolveAsync;
		return resolveAsync is null ? Task.FromResult(this) : ResolveRequiredAsync(resolveAsync, cancellationToken);
	}

	/// <summary>Creates a copy of the item with a resolve callback attached.</summary>
	/// <param name="resolveAsync">The resolve callback to attach.</param>
	/// <returns>A completion item with resolve support.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="resolveAsync"/> is <see langword="null"/>.</exception>
	public CompletionItem WithResolveCallback(Func<CancellationToken, Task<CompletionItem>> resolveAsync)
	{
		ArgumentNullException.ThrowIfNull(resolveAsync);

		CompletionItem item = new(this) { _resolveAsync = resolveAsync };

		return item;
	}

	/// <summary>Creates a copy stamped with request metadata used by hosts to reject stale completions.</summary>
	/// <param name="requestDocumentVersion">The originating document version; must not be negative.</param>
	/// <param name="requestGeneration">The originating request generation; must not be negative.</param>
	/// <returns>A completion item stamped with the supplied request context.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="requestDocumentVersion"/> or <paramref name="requestGeneration"/> is negative.
	/// </exception>
	/// <remarks>
	/// Only <see cref="RequestDocumentVersion"/> and <see cref="RequestGeneration"/> are set, and an item that already carries the supplied stamps
	/// is returned unchanged. Combines with <see cref="WithoutTextEdit"/> to keep resolved edit data out of the commit path.
	/// </remarks>
	public CompletionItem WithRequestContext(int requestDocumentVersion, int requestGeneration)
	{
		if (requestDocumentVersion < 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(requestDocumentVersion),
				requestDocumentVersion,
				"The request document version must not be negative."
			);
		}

		if (requestGeneration < 0)
			throw new ArgumentOutOfRangeException(nameof(requestGeneration), requestGeneration, "The request generation must not be negative.");

		if (RequestDocumentVersion == requestDocumentVersion && RequestGeneration == requestGeneration)
			return this;

		CompletionItem item = new(this) { _requestDocumentVersion = requestDocumentVersion, _requestGeneration = requestGeneration };

		return item;
	}

	/// <summary>Creates a copy without the explicit edit payload, so the commit uses the plain insertion text.</summary>
	/// <returns>A completion item without a text-edit payload.</returns>
	/// <remarks>
	/// An item with no text-edit payload is returned unchanged. Combines with <see cref="WithRequestContext"/> when a commit path must ignore an edit
	/// payload a resolve response supplied.
	/// </remarks>
	public CompletionItem WithoutTextEdit()
	{
		if (TextEdit is null)
			return this;

		CompletionItem item = new(this) { _textEdit = null };

		return item;
	}

	/// <summary>Creates a copy that merges richer resolved content onto the current item.</summary>
	/// <param name="resolvedItem">The resolved item carrying richer detail, description, kind, or commit data.</param>
	/// <returns>
	/// A merged completion item that keeps this item's identity, takes richer display fields from the resolved item, and keeps this item's commit
	/// and edit fields unless they are unset.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="resolvedItem"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// The commit and edit fields fall back to <paramref name="resolvedItem"/> only when unset.
	/// A merged copy carries no resolve callback, and a merge that contributes nothing returns this item unchanged. The per-field rules are the table
	/// in the <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/LanguageFeaturesGuide.md">language features guide</see>,
	/// their single normative source.
	/// </remarks>
	public CompletionItem WithResolvedContent(CompletionItem resolvedItem)
	{
		ArgumentNullException.ThrowIfNull(resolvedItem);
		return CompletionItemMerge.Apply(this, resolvedItem);
	}

	private static int? ValidateStamp(int? value, string parameterName) =>
		value is < 0 ? throw new ArgumentOutOfRangeException(parameterName, value, "The request stamp must not be negative.") : value;

	private static async Task<CompletionItem> ResolveRequiredAsync(
		Func<CancellationToken, Task<CompletionItem>> resolveAsync,
		CancellationToken cancellationToken
	)
	{
		Task<CompletionItem> resolveTask =
			resolveAsync(cancellationToken) ?? throw new InvalidOperationException("The completion-item resolve callback returned a null task.");

		CompletionItem resolvedItem =
			await resolveTask.ConfigureAwait(false)
			?? throw new InvalidOperationException("The completion-item resolve callback returned a null item.");

		return resolvedItem;
	}
}
