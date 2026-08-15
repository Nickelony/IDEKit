using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Input;

namespace Nickelony.KeyBindings.Wpf;

/// <summary>
/// Renders key combos with WPF's <see cref="KeyGesture"/> display strings for the configured
/// culture.
/// </summary>
/// <remarks>
/// <para>
/// The text comes from WPF's gesture converter: letters and main-row digits render as characters,
/// and the remaining keys render as their <see cref="Key"/> enum names, so
/// <see cref="KeyCode.Slash"/> renders as <c>OemQuestion</c> rather than the US glyph. A chord is
/// written as its gestures separated by commas, such as <c>Ctrl+K, Ctrl+S</c> for two strokes.
/// </para>
/// <para>
/// WPF rejects a letter or main-row digit without Ctrl, Alt, or the Windows key as a gesture (Shift
/// alone is not sufficient). Those combos, which the key model still allows, render as the
/// modifier-and-key text WPF's own converters produce, such as <c>Shift+S</c>, instead of throwing.
/// </para>
/// <para>
/// The formatter derives from <see cref="KeyDisplayTextFormatter"/> and overrides only
/// <see cref="KeyDisplayTextFormatter.GetComboText(KeyCombo)"/>, so the uninitialized-combo guard and the
/// chord sequencing come from the base.
/// </para>
/// </remarks>
public sealed class KeyGestureDisplayTextFormatter : KeyDisplayTextFormatter
{
	private const int MaxCacheEntryCount = 64;

	private static readonly KeyConverter s_keyConverter = new();
	private static readonly ModifierKeysConverter s_modifierKeysConverter = new();

	// The rendered text of a gesture, keyed by the key, the modifiers, and the culture it was rendered for.
	// A host renders the same bindings on every menu or toolbar pass, and formatting a gesture allocates a
	// KeyGesture and, for a combo WPF rejects, costs an exception; the memo turns those into one render. The
	// cache is cleared once it reaches MaxCacheEntryCount entries so a host that switches the current culture
	// per document cannot grow it without limit; the keys within one culture are few and cheap to rebuild, so
	// clearing beats tracking usage. The key space is bounded by the key codes, the modifier combinations, and
	// the cultures in use. Two CultureInfo instances that name the same culture are equal and hash-equal, so the
	// tuple key dedups them even when the formatter reads a fresh CurrentCulture on each call.
	private readonly ConcurrentDictionary<(Key Key, ModifierKeys Modifiers, CultureInfo Culture), string> _displayTextCache = new();

	private readonly CultureInfo? _culture;

	/// <summary>
	/// Initializes a new instance of the <see cref="KeyGestureDisplayTextFormatter"/> class.
	/// </summary>
	/// <param name="culture">
	/// The culture used to render the gesture; when <see langword="null"/>, the current culture is used.
	/// </param>
	public KeyGestureDisplayTextFormatter(CultureInfo? culture = null)
	{
		_culture = culture;
	}

	/// <summary>
	/// Gets a formatter that renders with the current culture.
	/// </summary>
	public static new KeyGestureDisplayTextFormatter Default { get; } = new();

	/// <inheritdoc/>
	protected override string GetComboText(KeyCombo keyCombo)
	{
		Key key = KeyCodeMapper.ToWpfKey(keyCombo.Key);
		ModifierKeys modifiers = KeyCodeMapper.ToWpfModifierKeys(keyCombo.Modifiers);
		CultureInfo culture = _culture ?? CultureInfo.CurrentCulture;

		// The read and the store are separate instead of GetOrAdd so the common hit costs no closure
		// allocation; a lost race only recomputes the same text.
		var cacheKey = (key, modifiers, culture);

		if (_displayTextCache.TryGetValue(cacheKey, out string? displayText))
			return displayText;

		displayText = FormatGesture(key, modifiers, culture);

		if (_displayTextCache.Count >= MaxCacheEntryCount)
			_displayTextCache.Clear();

		_displayTextCache.TryAdd(cacheKey, displayText);

		return displayText;
	}

	private static string FormatGesture(Key key, ModifierKeys modifiers, CultureInfo culture)
	{
		try
		{
			return new KeyGesture(key, modifiers).GetDisplayStringForCulture(culture);
		}
		catch (NotSupportedException)
		{
			// WPF's validating constructor rejects gestures the key model allows (a letter or
			// main-row digit without Ctrl/Alt/Windows). Render them with the same text WPF's own
			// converters produce.
			return FormatUnsupportedGesture(key, modifiers, culture);
		}
	}

	private static string FormatUnsupportedGesture(Key key, ModifierKeys modifiers, CultureInfo culture)
	{
		string keyText = s_keyConverter.ConvertTo(null, culture, key, typeof(string)) as string ?? key.ToString();
		string modifierText = s_modifierKeysConverter.ConvertTo(null, culture, modifiers, typeof(string)) as string ?? string.Empty;

		return modifierText.Length == 0 ? keyText : $"{modifierText}+{keyText}";
	}
}
