#if AVALONIAEDIT
using Avalonia.Media;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
#else
using System.Windows.Media;
using Nickelony.IDEKit.AvalonEdit.Rendering;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.SemanticTokens;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.SemanticTokens;

#endif

/// <summary>Caches the typeface that a style's bold and italic settings derive from a base typeface, up to a fixed entry count.</summary>
/// <remarks>
/// The key holds every input of the derivation: the bold and italic flags, and the base typeface with its family, style, weight, and stretch,
/// so a style that differs from another one only in foreground or decorations shares its entry. The cache is cleared when it reaches its entry limit,
/// because a derived typeface is cheap to rebuild. It is confined to the thread that paints and is not thread-safe.
/// </remarks>
internal sealed class DerivedTypefaceCache
{
	internal const int MaxEntryCount = 64;

	private readonly Dictionary<(bool IsBold, bool IsItalic, Typeface BaseTypeface), Typeface> _entries = [];

	/// <summary>The number of cached typefaces; never exceeds <see cref="MaxEntryCount"/>.</summary>
	internal int Count => _entries.Count;

	/// <summary>Returns the typeface for a style and a base typeface, deriving and caching it on first use.</summary>
	/// <param name="style">The style whose bold and italic settings are applied.</param>
	/// <param name="baseTypeface">The base typeface to derive from.</param>
	/// <returns>The derived typeface, shared with every element that resolves to the same key.</returns>
	internal Typeface GetOrAdd(TextRunStyle style, Typeface baseTypeface)
	{
		(bool IsBold, bool IsItalic, Typeface BaseTypeface) key = (style.IsBold, style.IsItalic, baseTypeface);

		// The binding's typeface type is a value type in one binding and a reference type in the other,
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

		if (_entries.Count >= MaxEntryCount)
			_entries.Clear();

		_entries.Add(key, typeface);
		return typeface;
	}
}
