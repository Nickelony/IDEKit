#if AVALONIAEDIT
using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaEdit.CodeCompletion;
#else
using ICSharpCode.AvalonEdit.CodeCompletion;
using System.Windows.Controls;
using System.Windows.Threading;
#endif

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
#endif

/// <summary>
/// Groups the accessors the tooltip presenter reads from its owning completion controller.
/// </summary>
/// <param name="Dispatcher">The editor dispatcher the tooltip pipeline runs on.</param>
/// <param name="GetActiveWindow">
/// The accessor for the currently tracked completion window, or <see langword="null"/> when none is tracked.
/// </param>
/// <param name="SetTooltipState">
/// The callback that reports tooltip visibility and content to the completion presentation state.
/// </param>
/// <param name="TryGetTooltip">
/// The accessor for a window's tooltip, or <see langword="null"/> when the running editor does not expose one.
/// </param>
internal sealed record CompletionTooltipPresenterContext(
	Dispatcher Dispatcher,
	Func<CompletionWindow?> GetActiveWindow,
	Action<object?, bool> SetTooltipState,
	Func<CompletionWindow, ToolTip?> TryGetTooltip);
