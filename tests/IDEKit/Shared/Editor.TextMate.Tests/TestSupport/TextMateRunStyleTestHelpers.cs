using System.Diagnostics;
using System.Runtime.CompilerServices;
using TextMateSharp.Grammars;
using TextMateSharp.Model;
using TextMateSharp.Registry;
#if AVALONIAEDIT
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using Brush = Avalonia.Media.IBrush;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using System.Windows.Media;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests;

#else

namespace Nickelony.IDEKit.AvalonEdit.TextMate.Tests;

#endif

internal static class TextMateRunStyleTestHelpers
{
	internal static TextMateTokenTheme CreateTheme(params TextMateTokenThemeRule[] rules) => new() { Rules = rules };

	internal static IGrammar CreateGrammar(string languageId)
	{
		var registryOptions = new RegistryOptions(ThemeName.DarkPlus);
		return new Registry(registryOptions).LoadGrammar(registryOptions.GetScopeByLanguageId(languageId));
	}

	/// <summary>Returns the resolved foreground in uppercase <c>#AARRGGBB</c> form.</summary>
	/// <param name="style">The style whose foreground is read.</param>
	/// <returns>The foreground color in uppercase <c>#AARRGGBB</c> form.</returns>
	/// <remarks>
	/// The toolkit's own color <c>ToString</c> is not used: one binding spells the value as hex while the other substitutes a known color name,
	/// so the helper formats the channels itself and the shared expectations read the same in both bindings.
	/// </remarks>
	internal static string GetForegroundColor(TextRunStyle style)
	{
		Assert.IsNotNull(style.Foreground);

		Color color = GetBrushColor(style.Foreground);
		return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
	}

	/// <summary>
	/// Returns the color of a solid brush; the binding creates its shared brushes with different types, so the cast is contained here.
	/// </summary>
	/// <param name="brush">The brush to read.</param>
	/// <returns>The brush color.</returns>
	internal static Color GetBrushColor(Brush brush)
	{
		Assert.IsTrue(TextMateRenderingTestHelpers.TryGetBrushColor(brush, out Color color));
		return color;
	}

	/// <summary>Repaints the editor's view and returns its first visual line.</summary>
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

	/// <summary>Determines whether the visual line contains an element painted with the given foreground.</summary>
	/// <param name="visualLine">The visual line to inspect.</param>
	/// <param name="color">The expected foreground color.</param>
	/// <returns><see langword="true"/> when a painted element carries the color.</returns>
	internal static bool HasThemedElement(VisualLine visualLine, Color color)
	{
		return visualLine.Elements.Any(element =>
			TextMateRenderingTestHelpers.TryGetBrushColor(element.TextRunProperties.ForegroundBrush, out Color brushColor) && brushColor == color
		);
	}

	/// <summary>Waits for the model's background pass to tokenize a line.</summary>
	/// <param name="model">The model whose background pass is awaited.</param>
	/// <param name="lineIndex">The zero-based index of the line to wait for.</param>
	/// <remarks>
	/// The wait is driven by the model's token-change notification, so it returns as soon as the model publishes the line instead of polling.
	/// TextMateSharp caps the time it spends on one line and re-queues a line whose batch ran out of budget, so the wait only guards
	/// against a stalled tokenizer, and the timeout is generous so a slow CI box cannot flake red.
	/// </remarks>
	internal static void WaitForTokenization(TMModel model, int lineIndex)
	{
		var signal = TokenizationSignal.ForModel(model);
		var stopwatch = Stopwatch.StartNew();

		while (model.GetLineTokens(lineIndex) is null || model.IsLineInvalid(lineIndex))
		{
			TimeSpan remaining = s_tokenizationTimeout - stopwatch.Elapsed;

			if (remaining <= TimeSpan.Zero || !signal.Wait(remaining))
			{
				Assert.Fail(
					$"The model did not tokenize line {lineIndex}: "
						+ $"invalid={model.IsLineInvalid(lineIndex)} tokenized={model.GetLineTokens(lineIndex) is not null}."
				);
			}
		}
	}

	/// <summary>Waits for the model's background pass to tokenize every line of the tracked document.</summary>
	/// <param name="model">The model whose background pass is awaited.</param>
	/// <param name="lineList">The line list that defines the awaited line range.</param>
	/// <remarks>
	/// Tests must not call <c>ForceTokenization</c> while the model's own tokenizer thread runs: TextMateSharp's tokenizer is not safe
	/// to drive from two threads at once. Lines the model leaves invalid because its per-line budget was exceeded
	/// are re-queued by the model itself, so the wait converges. The wait is driven by the model's token-change notification rather than polling;
	/// the timeout only guards against a stalled tokenizer. A walk that the model cuts short by its time budget can leave the next line holding stale
	/// tokens while every line reads valid, so the wait returns only after the lines stay valid through two quiet periods without a notification.
	/// </remarks>
	internal static void WaitForTokenization(TMModel model, TextMateDocumentLineList lineList)
	{
		var signal = TokenizationSignal.ForModel(model);
		var stopwatch = Stopwatch.StartNew();

		int quietPeriods = 0;

		while (quietPeriods < RequiredQuietPeriods)
		{
			TimeSpan remaining = s_tokenizationTimeout - stopwatch.Elapsed;

			if (!IsFullyTokenized(model, lineList))
			{
				quietPeriods = 0;

				if (remaining <= TimeSpan.Zero || !signal.Wait(remaining))
					Assert.Fail($"The model did not finish tokenizing the document ({lineList.GetNumberOfLines()} lines).");
			}
			else if (remaining <= TimeSpan.Zero)
			{
				Assert.Fail($"The model kept publishing tokens for the document ({lineList.GetNumberOfLines()} lines).");
			}
			else
			{
				quietPeriods = signal.Wait(s_quietPeriod) ? 0 : quietPeriods + 1;
			}
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

	private static readonly TimeSpan s_tokenizationTimeout = TimeSpan.FromSeconds(30.0);

	private static readonly TimeSpan s_quietPeriod = TimeSpan.FromMilliseconds(15.0);

	private const int RequiredQuietPeriods = 2;

	/// <summary>Signals a bounded wait from a model's token-change notifications.</summary>
	/// <remarks>
	/// A model stops its tokenizer thread when its last listener is removed, and an invalidation
	/// does not restart a stopped thread, so the listener must stay attached for the model's lifetime rather than be removed after each wait.
	/// <see cref="ConditionalWeakTable{TKey,TValue}"/> keeps exactly one listener per model and lets it be collected together with the model.
	/// </remarks>
	private sealed class TokenizationSignal : IModelTokensChangedListener
	{
		private const int MaxPendingNotifications = 1024;

		private static readonly ConditionalWeakTable<TMModel, TokenizationSignal> s_signals = [];

		private readonly SemaphoreSlim _notifications = new(0);

		private TokenizationSignal(TMModel model) => model.AddModelTokensChangedListener(this);

		internal static TokenizationSignal ForModel(TMModel model) => s_signals.GetValue(model, static target => new TokenizationSignal(target));

		internal bool Wait(TimeSpan timeout) => _notifications.Wait(timeout);

		void IModelTokensChangedListener.ModelTokensChanged(ModelTokensChangedEvent e)
		{
			// Cap the count so a burst cannot overflow the semaphore's maximum; the waiter drains it.
			if (_notifications.CurrentCount < MaxPendingNotifications)
				_notifications.Release();
		}
	}
}
