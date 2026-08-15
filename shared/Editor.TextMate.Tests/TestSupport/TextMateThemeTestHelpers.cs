#if AVALONIAEDIT
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using Brush = Avalonia.Media.IBrush;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.TextMate.Highlighting;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;
using System.Windows.Media;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
#endif
using System.Diagnostics;
using System.Runtime.CompilerServices;
using TextMateSharp.Grammars;
using TextMateSharp.Model;
using TextMateSharp.Registry;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests;
#else
namespace Nickelony.IDEKit.AvalonEdit.TextMate.Tests;
#endif

internal static class TextMateThemeTestHelpers
{
	internal static TextMateTokenTheme CreateTheme(params TextMateTokenThemeRule[] rules)
		=> new() { Rules = rules };

	internal static IGrammar CreateGrammar(string languageId)
	{
		var registryOptions = new RegistryOptions(ThemeName.DarkPlus);
		return new Registry(registryOptions).LoadGrammar(registryOptions.GetScopeByLanguageId(languageId));
	}

	/// <summary>
	/// Gets the resolved foreground in uppercase <c>#AARRGGBB</c> form. The toolkit's own color
	/// <c>ToString</c> is not used: one binding spells the value as hex while the other substitutes a
	/// known color name, so the helper formats the channels itself and the shared expectations read the
	/// same in both bindings.
	/// </summary>
	/// <param name="style">The style whose foreground is read.</param>
	/// <returns>The foreground color in uppercase <c>#AARRGGBB</c> form.</returns>
	internal static string GetForegroundColor(TextRunStyle style)
	{
		Assert.IsNotNull(style.Foreground);

		Color color = GetBrushColor(style.Foreground);
		return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
	}

	/// <summary>
	/// Gets the color of a solid brush; the binding creates its shared brushes with different types, so the
	/// cast is contained here.
	/// </summary>
	/// <param name="brush">The brush to read.</param>
	/// <returns>The brush color.</returns>
	internal static Color GetBrushColor(Brush brush)
	{
		Assert.IsTrue(TryGetBrushColor(brush, out Color color));
		return color;
	}

	/// <summary>
	/// Tries to read the color of a brush that is a plain solid color; the binding's solid-brush type
	/// differs, so the runtime test is contained here.
	/// </summary>
	/// <param name="brush">The brush to read, or <see langword="null"/>.</param>
	/// <param name="color">The brush color when the brush is a solid color; otherwise, the default color.</param>
	/// <returns><see langword="true"/> when the brush is a solid-color brush.</returns>
	internal static bool TryGetBrushColor(Brush? brush, out Color color)
	{
#if AVALONIAEDIT
		if (brush is ImmutableSolidColorBrush solidBrush)
		{
			color = solidBrush.Color;
			return true;
		}
#else
		if (brush is SolidColorBrush solidBrush)
		{
			color = solidBrush.Color;
			return true;
		}
#endif

		color = default;
		return false;
	}

	/// <summary>
	/// Asserts that two typefaces are the same cached value. The binding's typeface type is a reference type,
	/// where reference identity is checkable, and a value type, where value equality stands in for it.
	/// </summary>
	/// <param name="expected">The expected typeface.</param>
	/// <param name="actual">The actual typeface.</param>
	internal static void AssertSameTypeface(Typeface expected, Typeface actual)
	{
#if AVALONIAEDIT
		Assert.AreEqual(expected, actual);
#else
		Assert.AreSame(expected, actual);
#endif
	}

	/// <summary>
	/// Repaints the editor's view and returns its first visual line.
	/// </summary>
	/// <param name="editor">The editor whose view is repainted.</param>
	/// <returns>The first visual line after the repaint.</returns>
	internal static VisualLine GetPaintedVisualLine(TextEditor editor)
	{
		editor.UpdateLayout();
		editor.TextArea.TextView.Redraw();
		TestHost.PumpDispatcher(editor.Dispatcher, DispatcherPriority.Background);

		Assert.IsTrue(editor.TextArea.TextView.VisualLinesValid);

		return editor.TextArea.TextView.VisualLines.First();
	}

	/// <summary>
	/// Determines whether the visual line contains an element painted with the given foreground.
	/// </summary>
	/// <param name="visualLine">The visual line to inspect.</param>
	/// <param name="color">The expected foreground color.</param>
	/// <returns><see langword="true"/> when a painted element carries the color.</returns>
	internal static bool HasThemedElement(VisualLine visualLine, Color color)
		=> visualLine.Elements.Any(element =>
			TryGetBrushColor(element.TextRunProperties.ForegroundBrush, out Color brushColor)
			&& brushColor == color);

	/// <summary>
	/// Waits for the model's background pass to tokenize a line.
	/// </summary>
	/// <remarks>
	/// The wait is driven by the model's token-change notification, so it returns as soon as the model
	/// publishes the line instead of polling. The timeout doubles TextMateSharp's three-second per-line
	/// budget as a CI margin and only guards against a stalled tokenizer.
	/// </remarks>
	/// <param name="model">The model whose background pass is awaited.</param>
	/// <param name="lineIndex">The zero-based index of the line to wait for.</param>
	internal static void WaitForTokenization(TMModel model, int lineIndex)
	{
		TokenizationSignal signal = TokenizationSignal.ForModel(model);
		var stopwatch = Stopwatch.StartNew();

		while (model.GetLineTokens(lineIndex) is null || model.IsLineInvalid(lineIndex))
		{
			TimeSpan remaining = TokenizationTimeout - stopwatch.Elapsed;

			if (remaining <= TimeSpan.Zero || !signal.Wait(remaining))
			{
				Assert.Fail(
					$"The model did not tokenize line {lineIndex}: " +
					$"invalid={model.IsLineInvalid(lineIndex)} tokenized={model.GetLineTokens(lineIndex) is not null}.");
			}
		}
	}

	/// <summary>
	/// Waits for the model's background pass to tokenize every line of the tracked document.
	/// </summary>
	/// <remarks>
	/// Tests must not call <c>ForceTokenization</c> while the model's own tokenizer thread runs:
	/// TextMateSharp's tokenizer is not safe to drive from two threads at once. Lines the model
	/// leaves invalid because its per-line budget was exceeded are re-queued by the model itself, so
	/// the wait converges. The wait is driven by the model's token-change notification rather than
	/// polling; the timeout only guards against a stalled tokenizer.
	/// </remarks>
	/// <param name="model">The model whose background pass is awaited.</param>
	/// <param name="lineList">The line list that defines the awaited line range.</param>
	internal static void WaitForTokenization(TMModel model, TextMateDocumentLineList lineList)
	{
		TokenizationSignal signal = TokenizationSignal.ForModel(model);
		var stopwatch = Stopwatch.StartNew();

		while (!IsFullyTokenized(model, lineList))
		{
			TimeSpan remaining = TokenizationTimeout - stopwatch.Elapsed;

			if (remaining <= TimeSpan.Zero || !signal.Wait(remaining))
				Assert.Fail($"The model did not finish tokenizing the document ({lineList.GetNumberOfLines()} lines).");
		}
	}

	private static bool IsFullyTokenized(TMModel model, TextMateDocumentLineList lineList)
	{
		for (int i = 0; i < lineList.GetNumberOfLines(); i++)
		{
			if (model.GetLineTokens(i) is null || model.IsLineInvalid(i))
				return false;
		}

		return true;
	}

	private static readonly TimeSpan TokenizationTimeout = TimeSpan.FromSeconds(6.0);

	/// <summary>
	/// Signals a bounded wait from a model's token-change notifications.
	/// </summary>
	/// <remarks>
	/// A model stops its tokenizer thread when its last listener is removed, and an invalidation does not
	/// restart a stopped thread, so the listener must stay attached for the model's lifetime rather than be
	/// removed after each wait. <see cref="ConditionalWeakTable{TKey,TValue}"/> keeps exactly one listener
	/// per model and lets it be collected together with the model.
	/// </remarks>
	private sealed class TokenizationSignal : IModelTokensChangedListener
	{
		private const int MaxPendingNotifications = 1024;

		private static readonly ConditionalWeakTable<TMModel, TokenizationSignal> s_signals = new();

		private readonly SemaphoreSlim _notifications = new(0);

		private TokenizationSignal(TMModel model)
		{
			model.AddModelTokensChangedListener(this);
		}

		internal static TokenizationSignal ForModel(TMModel model)
			=> s_signals.GetValue(model, static target => new TokenizationSignal(target));

		internal bool Wait(TimeSpan timeout)
			=> _notifications.Wait(timeout);

		void IModelTokensChangedListener.ModelTokensChanged(ModelTokensChangedEvent e)
		{
			// Cap the count so a burst cannot overflow the semaphore's maximum; the waiter drains it.
			if (_notifications.CurrentCount < MaxPendingNotifications)
				_notifications.Release();
		}
	}
}
