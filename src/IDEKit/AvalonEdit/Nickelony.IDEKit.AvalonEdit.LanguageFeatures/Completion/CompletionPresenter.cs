using System.Windows.Controls;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Microsoft.Extensions.Logging;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

public sealed partial class CompletionPresenter
{
	private CompletionTooltipPresenter? _tooltipPresenter;

	/// <summary>Whether the running editor assembly exposes the completion tooltip this presenter styles and resolves descriptions for.</summary>
	/// <remarks>
	/// The value is constant for the process, and the property reports whether the tooltip is reachable through the binding's tooltip access;
	/// a host can query it before relying on description tooltips. When the tooltip is unavailable, description tooltips degrade
	/// (one warning is logged) while every other feature keeps working. AvaloniaEdit has no tooltip pipeline, so the binding declares no such member.
	/// </remarks>
	public static bool IsTooltipSupported => CompletionTooltipResolver.IsTooltipAvailable;

	/// <summary>The chrome of the completion tooltip; it starts as <see cref="CompletionPresentationOptions.TooltipTheme"/>.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The caller is not on the presenter's dispatcher thread.</exception>
	/// <remarks>
	/// A host that switches themes assigns the new one: the next window's tooltip takes it, and a window that is open keeps its chrome.
	/// </remarks>
	public CompletionTooltipTheme TooltipTheme
	{
		get => _tooltipPresenter?.Theme ?? CompletionTooltipTheme.Default;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			_dispatcher.VerifyAccess();

			if (_tooltipPresenter is not null)
				_tooltipPresenter.Theme = value;
		}
	}

	/// <summary>Indicates whether a debounced tooltip update is waiting for its delay.</summary>
	internal bool IsTooltipUpdatePending => _tooltipPresenter?.IsUpdatePending ?? false;

	/// <summary>Cancels any pending completion tooltip update.</summary>
	/// <exception cref="InvalidOperationException">The caller is not on the presenter's dispatcher thread.</exception>
	public void CancelTooltipUpdate()
	{
		if (_isDisposed)
			return;

		_dispatcher.VerifyAccess();

		CancelTooltipUpdateCore();
	}

	// The presenter creates its resolve debouncer on construction and arms it on the first selection,
	// so debounced description updates work even when the host schedules no requests.
	private partial void InitializeTooltip(CompletionPresentationOptions presentation, CompletionPresenterHooks? hooks, ILogger logger)
	{
		_tooltipPresenter = new CompletionTooltipPresenter(
			context: new CompletionTooltipPresenterContext(
				Dispatcher: _dispatcher,
				GetActiveWindow: () => WindowPresenter.ActiveWindow,
				SetTooltipState,
				TryGetTooltip: static window => CompletionTooltipResolver.TryGetTooltip(window, out ToolTip? tooltip) ? tooltip : null
			),

			theme: presentation.TooltipTheme,
			hooks,
			resolveDelay: presentation.TooltipResolveDelay,
			logger
		);
	}

	private partial void ApplyTooltipTheme(CompletionWindow completionWindow) => _tooltipPresenter?.ApplyTheme(completionWindow);

	private partial void CloseTooltip(CompletionWindow completionWindow) => _tooltipPresenter?.CloseTooltip(completionWindow);

	private partial void CancelTooltipUpdateCore() => _tooltipPresenter?.CancelUpdate();

	private partial void DetachTooltip() => _tooltipPresenter?.Detach();

	private partial void DisposeTooltip() => _tooltipPresenter?.Dispose();
}
