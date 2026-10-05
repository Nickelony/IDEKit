using System.Diagnostics.CodeAnalysis;
using AvaloniaEdit.CodeCompletion;
using Microsoft.Extensions.Logging;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

public sealed partial class CompletionPresenter
{
	private const string InstanceHalfJustification =
		"The shared presenter and the shared tests reach the tooltip half through an instance, because the WPF half keeps tooltip state.";

	/// <summary>Indicates whether a debounced tooltip update is waiting for its delay.</summary>
	/// <remarks>
	/// Always <see langword="false"/>, because AvaloniaEdit's completion window publishes no tooltip member and the binding builds
	/// no tooltip pipeline.
	/// </remarks>
	[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = InstanceHalfJustification)]
	internal bool IsTooltipUpdatePending => false;

	[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = InstanceHalfJustification)]
	private partial void InitializeTooltip(CompletionPresentationOptions presentation, CompletionPresenterHooks? hooks, ILogger logger) { }

	[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = InstanceHalfJustification)]
	private partial void ApplyTooltipTheme(CompletionWindow completionWindow) { }

	[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = InstanceHalfJustification)]
	private partial void CloseTooltip(CompletionWindow completionWindow) { }

	[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = InstanceHalfJustification)]
	private partial void CancelTooltipUpdateCore() { }

	[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = InstanceHalfJustification)]
	private partial void DetachTooltip() { }

	[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = InstanceHalfJustification)]
	private partial void DisposeTooltip() { }
}
