#if AVALONIAEDIT
using AvaloniaEdit.Document;
#else
using ICSharpCode.AvalonEdit.Document;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Text;

#else

namespace Nickelony.IDEKit.AvalonEdit.Text;

#endif

/// <summary>
/// Caches a value derived from the editor's <see cref="TextDocument"/> until the document instance, its version reference, or a caller-supplied
/// revision changes.
/// </summary>
/// <typeparam name="T">The cached reference type.</typeparam>
/// <remarks>
/// The revision is a caller-owned epoch for inputs the value factory reads outside the document.
/// Callers increment it with <see cref="System.Threading.Interlocked"/>, and the next request after the increment recomputes the value.
/// Each request captures the revision before the factory runs, so a value produced while the epoch changes is rebuilt on the next request
/// instead of being served. Use the cache on the document's owner thread, like all editor document access; it is not thread-safe. The cache relies
/// on the editor replacing <c>TextDocument.Version</c> on every change, which a test pins against the engine; an engine that mutated the version
/// object in place would need a change counter instead of the reference comparison.
/// </remarks>
internal sealed class DocumentVersionCache<T>
	where T : class
{
	private T? _cachedValue;
	private TextDocument? _cachedDocument;
	private ITextSourceVersion? _cachedVersion;
	private long _cachedRevision;

	/// <summary>
	/// Returns the cached value for <paramref name="document"/>, creating it through <paramref name="valueFactory"/> when the document, its version,
	/// or the revision changed.
	/// </summary>
	/// <param name="document">The document the cached value is derived from.</param>
	/// <param name="valueFactory">Creates the value when the cache is empty or stale. It must not return <see langword="null"/>.</param>
	/// <param name="revision">
	/// The caller-owned epoch for this request, captured before <paramref name="valueFactory"/> runs; callers without an external epoch leave
	/// the default.
	/// </param>
	/// <exception cref="InvalidOperationException"><paramref name="valueFactory"/> returned <see langword="null"/>.</exception>
	internal T GetOrCreate(TextDocument document, Func<TextDocument, T> valueFactory, long revision = 0)
	{
		if (
			_cachedValue is not null
			&& ReferenceEquals(_cachedDocument, document)
			&& ReferenceEquals(_cachedVersion, document.Version)
			&& _cachedRevision == revision
		)
		{
			return _cachedValue;
		}

		// The version is captured before the factory runs. A factory that mutates the document produces
		// a value for text the mutated document does not describe, so the value is returned without
		// being cached: the next request recomputes it instead of serving a stale value forever.
		ITextSourceVersion version = document.Version;
		T value = valueFactory(document) ?? throw new InvalidOperationException("The value factory returned null.");

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
