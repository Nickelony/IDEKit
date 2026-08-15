#if AVALONIAEDIT
using Avalonia.Media;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
#else
using Nickelony.IDEKit.AvalonEdit.Rendering;
using System.Windows.Media;
#endif

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Highlighting;
#else
namespace Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;
#endif

/// <summary>
/// Caches typefaces derived from a style's bold and italic settings and a base typeface, up to a fixed
/// entry count.
/// </summary>
/// <remarks>
/// Bounds the derived typefaces for hosts that assign many distinct typefaces to visual line elements;
/// the combinations observed while painting one view are few, because the resolver shares one instance
/// per resolved scope sequence. The key is the derived typeface's actual inputs - the bold and italic
/// flags and the base typeface - so two styles that differ only in foreground or decorations share one
/// entry. The cache is cleared when it reaches its entry limit, which is preferable to tracking usage
/// for values this cheap to rebuild. The cache is UI-thread confined and not thread-safe; create and use
/// it on the thread that paints.
/// </remarks>
internal sealed class TextMateTypefaceCache
{
	internal const int MaxCacheEntryCount = 64;

	private readonly Dictionary<(bool IsBold, bool IsItalic, Typeface BaseTypeface), Typeface> _entries = [];

	/// <summary>
	/// Gets the number of cached typefaces; never exceeds <see cref="MaxCacheEntryCount"/>.
	/// </summary>
	internal int Count => _entries.Count;

	/// <summary>
	/// Gets the typeface for the given style and base typeface, deriving and caching it on first use.
	/// </summary>
	/// <param name="style">The style whose bold and italic settings are applied.</param>
	/// <param name="baseTypeface">The base typeface to derive from.</param>
	/// <returns>The derived typeface, shared with every element that resolves to the same key.</returns>
	internal Typeface GetOrAdd(TextRunStyle style, Typeface baseTypeface)
	{
		var key = (style.IsBold, style.IsItalic, baseTypeface);

		// The binding's typeface type is a value type on AvaloniaEdit and a reference type on AvalonEdit,
		// so the cached value is declared per binding to satisfy the nullable analysis.
#if AVALONIAEDIT
		if (_entries.TryGetValue(key, out Typeface cachedTypeface))
#else
		if (_entries.TryGetValue(key, out Typeface? cachedTypeface))
#endif
		{
			return cachedTypeface;
		}

		Typeface typeface = style.CreateTypeface(baseTypeface);

		if (_entries.Count >= MaxCacheEntryCount)
			_entries.Clear();

		_entries.Add(key, typeface);
		return typeface;
	}
}
