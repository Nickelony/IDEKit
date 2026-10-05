using System.Globalization;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
#if AVALONIAEDIT
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Editing;
#else
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Editing;
using System.Windows.Controls;
using System.Windows.Media;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

#endif

/// <summary>Measures the content-driven width of a completion window.</summary>
/// <remarks>
/// The default measurement renders the display
/// text with the text area's font configuration - family, style, weight, stretch, size, and foreground - and the text area's flow direction,
/// so the measured width matches a window that renders in the editor's font. The display text is the item's displayed content (a text-block content
/// resolves to its text when it carries any), falling back to the item's filter text when the content cannot be read as text. An item that supplies
/// an image also reserves <see cref="CompletionWindowOptions.ItemIconWidth"/> for its icon column, and one that supplies detail text reserves
/// <see cref="CompletionWindowOptions.ItemDetailSpacing"/> before it; an item that supplies neither is measured as its text alone. A host whose item
/// template uses a different font supplies <see cref="CompletionPresenterHooks.MeasureItemWidth"/>, and a host that changes the window font
/// in <see cref="CompletionPresenterHooks.ConfigureWindow"/> should do the same so the measured width keeps matching the rendered width.
/// </remarks>
internal static class CompletionWindowSizing
{
	/// <summary>
	/// Measures the required content width for the given completion items,
	/// never reporting less than <see cref="CompletionWindowOptions.MinContentWidth"/>.
	/// </summary>
	/// <param name="textArea">The text area whose font configuration the measurement uses.</param>
	/// <param name="options">The sizing options.</param>
	/// <param name="getDisplayInfo">An optional callback that supplies the display text and detail for an item.</param>
	/// <param name="measureItemWidth">
	/// An optional callback that measures an item's content width, replacing the default measurement (the display text, plus an icon column for items
	/// that supply an image, plus the detail gap when detail text is present). The returned width must be finite; a negative value cannot widen
	/// the window and is ignored.
	/// </param>
	/// <param name="items">The items to measure.</param>
	/// <param name="widthCache">
	/// An optional cache of measured display-string widths, reused across the open and in-place refreshes of one completion window.
	/// The caller must supply a fresh cache whenever the measurement context (the text area's font, size, flow direction, culture, and DPI) changes,
	/// because a cached width is only valid for the context it was measured in.
	/// </param>
	/// <returns>The required content width before the window chrome is added.</returns>
	/// <exception cref="InvalidOperationException">
	/// <paramref name="measureItemWidth"/> returned a non-finite width, which would silently turn the window width into an auto-sized value.
	/// </exception>
	/// <remarks>
	/// Items are measured until the content width the window can display is reached, so the window is sized by its widest
	/// items rather than by an arbitrary sample; identical display strings are measured at most once per measurement pass. The caller adds the chrome
	/// and clamps the result into the window width bounds.
	/// </remarks>
	internal static double MeasureRequiredWidth(
		TextArea textArea,
		CompletionWindowOptions options,
		Func<ICompletionData, (string Text, string? Detail)>? getDisplayInfo,
		Func<ICompletionData, double>? measureItemWidth,
		IReadOnlyList<ICompletionData> items,
		Dictionary<string, double>? widthCache = null
	)
	{
		double requiredWidth = options.MinContentWidth;

		if (items.Count == 0)
			return requiredWidth;

		// Once the measured content width reaches the width the completed window can display, no further
		// item can widen the window, so the scan stops instead of measuring every item on every open and
		// refresh.
		double contentWidthCap = options.MaxContentWidth;

		if (measureItemWidth is not null)
		{
			for (int i = 0; i < items.Count; i++)
			{
				double measuredWidth = measureItemWidth(items[i]);

				// A non-finite measurement would flow into the window width, where it is silently read as
				// auto-sizing and defeats the configured maximum; a negative measurement cannot widen the window.
				if (!double.IsFinite(measuredWidth))
					throw new InvalidOperationException("The MeasureItemWidth hook returned a non-finite item width.");

				requiredWidth = Math.Max(requiredWidth, measuredWidth);

				if (requiredWidth >= contentWidthCap)
					break;
			}

			return requiredWidth;
		}

		// The typeface and text cache are invariant for the whole measurement pass, so they are
		// resolved once instead of once per measured string.
		var context = new MeasurementContext(textArea, options, getDisplayInfo, textWidthCache: widthCache);

		for (int i = 0; i < items.Count; i++)
		{
			requiredWidth = Math.Max(requiredWidth, context.MeasureItemWidth(items[i]));

			if (requiredWidth >= contentWidthCap)
				break;
		}

		return requiredWidth;
	}

	private sealed class MeasurementContext(
		TextArea textArea,
		CompletionWindowOptions options,
		Func<ICompletionData, (string Text, string? Detail)>? getDisplayInfo,
		Dictionary<string, double>? textWidthCache
	)
	{
		private readonly Dictionary<string, double> _textWidthCache = textWidthCache ?? new(StringComparer.Ordinal);

		// The typeface follows the text area's configured font, so a bold, italic, or condensed font
		// measures with the same style it renders with.
		private readonly Typeface _typeface = new(
			fontFamily: textArea.FontFamily,
			style: textArea.FontStyle,
			weight: textArea.FontWeight,
			stretch: textArea.FontStretch
		);

		private readonly double _pixelsPerDip = CompletionSeam.GetPixelsPerDip(textArea);

		internal double MeasureItemWidth(ICompletionData completionData)
		{
			(string text, string? detail) = getDisplayInfo is not null ? getDisplayInfo(completionData) : (GetDisplayText(completionData), null);

			double width = MeasureTextWidth(text);

			// The icon column exists only when the item supplies an image, so an item without one is
			// measured exactly as wide as its text (and detail).
			if (completionData.Image is not null)
				width += options.ItemIconWidth;

			if (!string.IsNullOrWhiteSpace(detail))
				width += options.ItemDetailSpacing + MeasureTextWidth(detail);

			return width;
		}

		// The default measurement sizes the window for what the list displays. The completion-data contract
		// keeps the displayed content in Content (a string or a rich element) and the filter key in Text; the
		// package's own adapter renders a struck-through TextBlock for deprecated items, so a text-block
		// content resolves to its text. A text block whose text was set through Inlines reports an empty Text,
		// and any other content shape cannot be read as text, so each falls back to the filter text.
		private static string GetDisplayText(ICompletionData completionData)
		{
			return completionData.Content switch
			{
				string text => text,
				TextBlock { Text: { Length: > 0 } textBlockText } => textBlockText,
				_ => completionData.Text,
			};
		}

		private double MeasureTextWidth(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return 0.0;

			if (_textWidthCache.TryGetValue(text, out double cachedWidth))
				return cachedWidth;

			// The measurement inputs are the text area's own typeface, foreground, and flow direction, so a host
			// that changes them measures the same way it renders.
			FormattedText formattedText = CompletionSeam.CreateFormattedText(textArea, text, typeface: _typeface, pixelsPerDip: _pixelsPerDip);

			double width = formattedText.WidthIncludingTrailingWhitespace;
			_textWidthCache[text] = width;
			return width;
		}
	}

	/// <summary>
	/// A cache of measured display-string widths for one completion window, reused by the window's open and every in-place refresh so a string
	/// is measured at most once for a fixed measurement context.
	/// </summary>
	/// <remarks>
	/// The cache records the measurement context it was built for - the text area's font, size, flow direction, culture,
	/// and DPI - and <see cref="Matches"/> reports whether that context still describes the text area. A mismatch discards the cached
	/// widths instead of reusing them, which the caller does by building a fresh cache, so widths measured under a previous context never leak
	/// into a measurement.
	/// </remarks>
	internal sealed class CompletionWidthCache
	{
		private readonly string _contextKey;

		/// <param name="textArea">The text area whose measurement context the cache is built for.</param>
		internal CompletionWidthCache(TextArea textArea) => _contextKey = BuildContextKey(textArea);

		/// <summary>The measured display-string widths, keyed by display string.</summary>
		internal Dictionary<string, double> Widths { get; } = new(StringComparer.Ordinal);

		/// <summary>Reports whether the cache still describes the supplied text area's measurement context.</summary>
		/// <param name="textArea">The text area to compare against.</param>
		/// <returns><see langword="true"/> when the cached widths remain valid for the text area.</returns>
		internal bool Matches(TextArea textArea) => string.Equals(_contextKey, BuildContextKey(textArea), StringComparison.Ordinal);

		// The font family, style, weight, stretch, size, flow direction, culture, and DPI are the inputs the
		// default measurement feeds FormattedText, so any change makes a cached width stale.
		private static string BuildContextKey(TextArea textArea)
		{
			return string.Join(
				'\u001f',
				textArea.FontFamily.ToString(),
				textArea.FontStyle.ToString(),
				textArea.FontWeight.ToString(),
				textArea.FontStretch.ToString(),
				textArea.FontSize.ToString(CultureInfo.InvariantCulture),
				textArea.FlowDirection.ToString(),
				CultureInfo.CurrentUICulture.Name,
				CompletionSeam.GetPixelsPerDip(textArea).ToString(CultureInfo.InvariantCulture)
			);
		}
	}
}
