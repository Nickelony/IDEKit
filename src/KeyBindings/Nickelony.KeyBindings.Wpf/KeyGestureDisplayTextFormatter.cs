using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Input;

namespace Nickelony.KeyBindings.Wpf;

/// <summary>Renders key strokes with the display strings WPF's <see cref="KeyGesture"/> converter produces.</summary>
/// <remarks>
/// <para>
/// The text comes from WPF's gesture converter: letters and main-row digits render as characters, and the remaining keys render as WPF's key text,
/// so <see cref="KeyCode.Slash"/> renders as <c>OemQuestion</c> rather than the US glyph. A chord is written as its strokes separated by commas,
/// such as <c>Ctrl+K, Ctrl+S</c> for two strokes.
/// </para>
/// <para>
/// The configured culture is passed to WPF's converter, but WPF's key and modifier converters return
/// the same text for en-US, de-DE, ja-JP, and fr-FR, the cultures checked.
/// </para>
/// <para>
/// WPF rejects a letter or main-row digit without Ctrl, Alt, or the Windows key as a gesture (Shift alone is not sufficient). Those strokes,
/// which the key model still allows, render as the modifier-and-key text WPF's own converters produce, such as <c>Shift+S</c>, instead of throwing.
/// </para>
/// </remarks>
public sealed class KeyGestureDisplayTextFormatter : KeyDisplayTextFormatter
{
	private static readonly KeyConverter s_keyConverter = new();
	private static readonly ModifierKeysConverter s_modifierKeysConverter = new();

	// The rendered text of a gesture, keyed by key, modifiers, and culture. Formatting allocates a
	// KeyGesture and, for a stroke WPF rejects, costs an exception, so the memo keeps one render per
	// key/modifier/culture. The key space is bounded, so the cache needs no eviction; equal-named
	// cultures dedup by value.
	private readonly ConcurrentDictionary<(Key Key, ModifierKeys Modifiers, CultureInfo Culture), string> _displayTextCache = new();

	private readonly CultureInfo? _culture;

	/// <param name="culture">The culture that renders the gesture; when <see langword="null"/>, the current culture is used.</param>
	public KeyGestureDisplayTextFormatter(CultureInfo? culture = null) => _culture = culture;

	/// <summary>A formatter that renders each stroke with the culture that is current at the time of the call.</summary>
	public static KeyGestureDisplayTextFormatter CurrentCulture { get; } = new();

	/// <inheritdoc/>
	protected override string GetStrokeText(KeyStroke keyStroke)
	{
		Key key = KeyCodeConversion.ToWpfKey(keyStroke.Key);
		ModifierKeys modifiers = KeyCodeConversion.ToWpfModifierKeys(keyStroke.Modifiers);
		CultureInfo culture = _culture ?? CultureInfo.CurrentCulture;

		// The read and the store are separate instead of GetOrAdd so the common hit costs no closure
		// allocation; a lost race only recomputes the same text.
		(Key key, ModifierKeys modifiers, CultureInfo culture) cacheKey = (key, modifiers, culture);

		if (_displayTextCache.TryGetValue(cacheKey, out string? displayText))
			return displayText;

		displayText = FormatGesture(key, modifiers, culture);

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
			// WPF rejects this gesture; render its key and modifier text through the WPF converters instead.
			return FormatUnsupportedGesture(key, modifiers, culture);
		}
	}

	private static string FormatUnsupportedGesture(Key key, ModifierKeys modifiers, CultureInfo culture)
	{
		string keyText = s_keyConverter.ConvertTo(context: null, culture, value: key, destinationType: typeof(string)) as string ?? key.ToString();

		string modifierText =
			s_modifierKeysConverter.ConvertTo(context: null, culture, value: modifiers, destinationType: typeof(string)) as string ?? string.Empty;

		return modifierText.Length == 0 ? keyText : $"{modifierText}+{keyText}";
	}
}
