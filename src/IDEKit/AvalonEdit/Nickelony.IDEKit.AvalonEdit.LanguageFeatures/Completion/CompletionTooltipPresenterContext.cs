using System.Windows.Controls;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.Core.Threading;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

/// <summary>Groups the accessors the tooltip presenter reads from its owning completion presenter.</summary>
/// <param name="Dispatcher">The editor dispatcher the tooltip pipeline runs on.</param>
/// <param name="GetActiveWindow">The accessor for the currently tracked completion window, or <see langword="null"/> when none is tracked.</param>
/// <param name="SetTooltipState">The callback that reports tooltip visibility and content to the completion presentation state.</param>
/// <param name="TryGetTooltip">The accessor for a window's tooltip, or <see langword="null"/> when the running editor does not expose one.</param>
internal sealed record CompletionTooltipPresenterContext(
	IUiDispatcher Dispatcher,
	Func<CompletionWindow?> GetActiveWindow,
	Action<object?, bool> SetTooltipState,
	Func<CompletionWindow, ToolTip?> TryGetTooltip
);
