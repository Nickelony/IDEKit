#if AVALONIAEDIT
using AvaloniaEdit.Document;
#else
using ICSharpCode.AvalonEdit.Document;
#endif

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Documents;
#else
namespace Nickelony.IDEKit.AvalonEdit.Documents;
#endif

/// <summary>
/// Caches a value derived from the editor's <see cref="TextDocument"/> until the document instance,
/// its version reference, or a caller-supplied revision changes.
/// </summary>
/// <remarks>
/// <para>
/// The document replaces its version object on every change, so an unchanged version reference proves
/// the cached value still applies without recomputing it. This relies on the editor implementation
/// detail that <see cref="TextDocument.Version"/> returns a replacement object per change instead of
/// mutating one version instance.
/// </para>
/// <para>
/// The revision is a caller-owned epoch for inputs that live outside the document. A caller whose
/// value factory reads such inputs owns a revision field that its writers bump with
/// <see cref="System.Threading.Interlocked"/>; each read captures the field's current value, passes it to
/// <see cref="GetOrCreate(TextDocument, Func{TextDocument, T}, long)"/>, and invalidates the cache
/// after the bump. A value produced while a writer changes the epoch is stored with the epoch captured
/// before the factory ran, so the next request sees a mismatch and rebuilds instead of serving it.
/// </para>
/// <para>
/// The cache is not thread-safe: use it on the document's owner thread, like all editor document
/// access.
/// </para>
/// </remarks>
/// <typeparam name="T">The cached reference type.</typeparam>
internal sealed class DocumentVersionCache<T> where T : class
{
	private T? _cachedValue;
	private TextDocument? _cachedDocument;
	private ITextSourceVersion? _cachedVersion;
	private long _cachedRevision;

	/// <summary>
	/// Gets the cached value for <paramref name="document"/>, creating it through
	/// <paramref name="valueFactory"/> when the document, its version, or the revision changed.
	/// </summary>
	/// <param name="document">The document the cached value is derived from.</param>
	/// <param name="valueFactory">
	/// Creates the value when the cache is empty or stale. It may return <see langword="null"/>; a
	/// <see langword="null"/> result is returned to the caller but never cached, so it is recomputed on
	/// every request.
	/// </param>
	/// <param name="revision">
	/// The caller-owned epoch for this request, captured before <paramref name="valueFactory"/> runs.
	/// Defaults to <c>0</c> for callers without an external epoch.
	/// </param>
	/// <returns>The cached or newly created value.</returns>
	internal T GetOrCreate(TextDocument document, Func<TextDocument, T> valueFactory, long revision = 0)
	{
		if (_cachedValue is not null
			&& ReferenceEquals(_cachedDocument, document)
			&& ReferenceEquals(_cachedVersion, document.Version)
			&& _cachedRevision == revision)
		{
			return _cachedValue;
		}

		// The version is captured before the factory runs. A factory that mutates the document produced
		// a value for text the mutated document no longer describes, so the value is returned without
		// being cached: the next request recomputes it instead of serving a stale value forever.
		ITextSourceVersion version = document.Version;
		T value = valueFactory(document);

		if (!ReferenceEquals(document.Version, version))
			return value;

		_cachedValue = value;
		_cachedDocument = document;
		_cachedVersion = version;
		_cachedRevision = revision;

		return value;
	}

	internal void Invalidate()
	{
		_cachedValue = null;
		_cachedDocument = null;
		_cachedVersion = null;
		_cachedRevision = 0;
	}
}
