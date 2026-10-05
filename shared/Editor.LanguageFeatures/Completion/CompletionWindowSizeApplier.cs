using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
#if AVALONIAEDIT
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Editing;
#else
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Editing;
#endif
#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

#endif
/// <summary>
/// Sizes a completion window to its items, reusing the measured display-string widths across the open and in-place refreshes of one window.
/// </summary>
internal sealed class CompletionWindowSizeApplier
{
	private readonly TextArea _textArea;
	private readonly CompletionWindowOptions _options;
	private readonly Func<ICompletionData, (string Text, string? Detail)>? _getDisplayInfo;
	private readonly Func<ICompletionData, double>? _measureItemWidth;

	// The measured display-string widths of the current completion window, reused across its open and every
	// in-place refresh so a refresh does not re-measure strings it has already measured. It is dropped with the
	// window and rebuilt whenever the measurement context changed under it.
	private CompletionWindowSizing.CompletionWidthCache? _widthCache;

	/// <param name="textArea">The text area whose font configuration the measurement uses.</param>
	/// <param name="getDisplayInfo">An optional callback that supplies the display text and detail for an item.</param>
	/// <param name="measureItemWidth">An optional callback that replaces the default width measurement of an item.</param>
	/// <param name="options">The sizing options.</param>
	internal CompletionWindowSizeApplier(
		TextArea textArea,
		Func<ICompletionData, (string Text, string? Detail)>? getDisplayInfo,
		Func<ICompletionData, double>? measureItemWidth,
		CompletionWindowOptions options
	)
	{
		_textArea = textArea;
		_options = options;
		_getDisplayInfo = getDisplayInfo;
		_measureItemWidth = measureItemWidth;
	}

	/// <summary>Drops the cached widths, because the window they were measured for is gone.</summary>
	internal void Reset() => _widthCache = null;

	/// <summary>Sets the content width of the window to the width its items require, within the configured limits.</summary>
	/// <param name="completionWindow">The window to size.</param>
	/// <param name="completionItems">The items the window shows.</param>
	internal void Apply(CompletionWindow completionWindow, ICompletionData[] completionItems)
	{
		// Cached widths are only valid for the measurement context they were taken in, so a context change (font,
		// size, flow direction, culture, or DPI) discards the cache and starts a fresh one.
		_widthCache =
			_widthCache is not null && _widthCache.Matches(_textArea) ? _widthCache : new CompletionWindowSizing.CompletionWidthCache(_textArea);

		double requiredWidth = CompletionWindowSizing.MeasureRequiredWidth(
			textArea: _textArea,
			options: _options,
			getDisplayInfo: _getDisplayInfo,
			measureItemWidth: _measureItemWidth,
			items: completionItems,
			widthCache: _widthCache.Widths
		);

		// MeasureRequiredWidth already floors the content width at MinContentWidth, so only the width
		// helper's clamp can bind here; the chrome/maximum relationship stays defined in the sizing helper.
		CompletionSeam.SetContentWidth(completionWindow, _options.GetWindowWidth(requiredWidth));
	}
}
