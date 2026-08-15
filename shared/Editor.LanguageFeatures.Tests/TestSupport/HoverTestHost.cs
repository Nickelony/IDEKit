#if AVALONIAEDIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Hover;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Hover;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Hover;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Hover;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

/// <summary>
/// Hosts a hover controller over a test border and records every display decision and provider call, so
/// hover tests share one host instead of repeating the full hook set.
/// </summary>
/// <remarks>
/// The properties are mutable on purpose: a test starts from the defaults and swaps in the hook behavior it
/// needs, and the recorder lists observe what the controller reported. <see cref="DisplayCalls"/> records
/// every display decision including the hide requests, so neither a stale tooltip nor an unexpected hide can
/// pass unnoticed.
/// </remarks>
internal sealed class HoverTestHost : IDisposable
{
	/// <summary>
	/// Initializes a new instance of the <see cref="HoverTestHost"/> class and shows the owner border in a
	/// host window.
	/// </summary>
	public HoverTestHost()
	{
		Owner = new Border();
		HostWindow = TestHost.ShowInHostWindow(Owner);
	}

	/// <summary>
	/// Gets the element hover positions are resolved against.
	/// </summary>
	public Border Owner { get; }

	/// <summary>
	/// Gets the host window that owns the border; dispose the host to close it.
	/// </summary>
	public Window HostWindow { get; }

	/// <summary>
	/// Gets or sets the offset resolution hook. Defaults to the constant offset <c>5</c>.
	/// </summary>
	public Func<Point, int?> GetOffsetFromPoint { get; set; } = static _ => 5;

	/// <summary>
	/// Gets or sets the request-state hook. Defaults to a requesting state at offset <c>5</c> that allows a
	/// tooltip and has no diagnostic and no fallback.
	/// </summary>
	public Func<int, TextHoverEvaluationState> BuildEvaluationState { get; set; } = static _ => new TextHoverEvaluationState
	{
		ShouldRequestHover = true,
		RequestOffset = 5,
		CanShowHoverContent = true,
		CanShowDiagnosticFallback = false,
		DiagnosticInfo = null
	};

	/// <summary>
	/// Gets or sets the provider hook. Defaults to an immediately completing hover result without a diagnostic.
	/// </summary>
	public Func<int, CancellationToken, Task<TextHoverInfo?>> RequestHoverAsync { get; set; } =
		static (_, _) => Task.FromResult<TextHoverInfo?>(new TextHoverInfo("hover") { SymbolName = "symbol" });

	/// <summary>
	/// Gets or sets the optional request-offset hook, or <see langword="null"/> when the host does not remap.
	/// </summary>
	public Func<int, int?>? ResolveRequestOffset { get; set; }

	/// <summary>
#if AVALONIAEDIT
	/// Gets or sets the optional pointer-position hook, or <see langword="null"/> to reuse the request offset
	/// (Avalonia exposes no ambient mouse position to fall back to).
#else
	/// Gets or sets the optional pointer-position hook, or <see langword="null"/> to use the current mouse
	/// position.
#endif
	/// </summary>
	public Func<Point>? GetCurrentPointerPosition { get; set; }

	/// <summary>
	/// Gets or sets the optional context-version provider, or <see langword="null"/> when the host version
	/// never changes.
	/// </summary>
	public Func<int>? ContextVersionProvider { get; set; }

	/// <summary>
	/// Gets the display decisions the controller reported, in order, including hide requests
	/// (<see langword="null"/> content and no diagnostic).
	/// </summary>
	public List<(TextHoverInfo? HoverInfo, TextDiagnostic? DiagnosticInfo)> DisplayCalls { get; } = [];

	/// <summary>
	/// Gets the offsets the controller requested hover for, in order.
	/// </summary>
	public List<int> RequestOffsets { get; } = [];

	/// <summary>
	/// Gets the cancellation tokens the controller passed to the provider hook, in order.
	/// </summary>
	public List<CancellationToken> RequestTokens { get; } = [];

	/// <summary>
	/// Gets or sets an optional callback invoked after a display decision has been recorded, for example to
	/// throw and exercise the failure containment.
	/// </summary>
	public Action<TextHoverInfo?, TextDiagnostic?>? OnDisplay { get; set; }

	/// <summary>
	/// Creates a hover controller wired to this host's hooks.
	/// </summary>
	/// <param name="logger">The optional logger for controller failures.</param>
	/// <returns>The created controller.</returns>
	public TextHoverController CreateController(Microsoft.Extensions.Logging.ILogger? logger = null)
		=> new(
			Owner,
			new TextHoverControllerHooks
			{
				GetOffsetFromPoint = point => GetOffsetFromPoint(point),
				BuildEvaluationState = offset => BuildEvaluationState(offset),
				RequestHoverAsync = (offset, cancellationToken) =>
				{
					RequestOffsets.Add(offset);
					RequestTokens.Add(cancellationToken);
					return RequestHoverAsync(offset, cancellationToken);
				},
				ResolveRequestOffset = ResolveRequestOffset is null
					? null
					: offset => ResolveRequestOffset(offset),
				GetCurrentPointerPosition = GetCurrentPointerPosition,
				ContextVersionProvider = ContextVersionProvider,
				ShowTooltip = (hoverInfo, diagnosticInfo) =>
				{
					DisplayCalls.Add((hoverInfo, diagnosticInfo));
					OnDisplay?.Invoke(hoverInfo, diagnosticInfo);
				}
			},
			logger);

	/// <summary>
	/// Creates the mouse-hover event arguments the controller handles in these tests.
	/// </summary>
	/// <returns>The event arguments.</returns>
#if AVALONIAEDIT
	/// <remarks>
	/// Avalonia 12 removed the ambient pointer position (<c>Avalonia.Input.Mouse</c>), so the WPF helper's
	/// parameterless <c>MouseEventArgs</c> cannot be mirrored directly. The event args carry no presentation
	/// source, so <see cref="PointerEventArgs.GetPosition"/> resolves to the origin and the offset-resolution
	/// hook receives the default point; tests that need a position supply it through
	/// <see cref="TextHoverControllerHooks.GetCurrentPointerPosition"/> instead. Avalonia's <see cref="IPointer"/>
	/// is deliberately not implementable by user code, and the hover path reads only the event position and
	/// never the pointer, so the pointer argument is <see langword="null"/>.
	/// </remarks>
	public static PointerEventArgs CreateMouseEventArgs() => new(
		InputElement.PointerMovedEvent,
		source: null,
		pointer: null!,
		rootVisual: null,
		rootVisualPosition: default,
		timestamp: 0,
		properties: default,
		modifiers: KeyModifiers.None);
#else
	public static MouseEventArgs CreateMouseEventArgs() => new(Mouse.PrimaryDevice, 0)
	{
		RoutedEvent = Mouse.MouseMoveEvent
	};
#endif

	/// <inheritdoc/>
	public void Dispose() => HostWindow.Close();
}
