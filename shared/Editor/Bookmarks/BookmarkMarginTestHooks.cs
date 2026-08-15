#if AVALONIAEDIT
using Avalonia;
using Avalonia.Input;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
#else
using Nickelony.IDEKit.AvalonEdit.Rendering;
using System.Windows;
using PointerPressedEventArgs = System.Windows.Input.MouseButtonEventArgs;
#endif

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Bookmarks;
#else
namespace Nickelony.IDEKit.AvalonEdit.Bookmarks;
#endif

/// <summary>
/// Groups the test-only hooks of the <see cref="BookmarkMargin"/> so the margin's own member list stays free
/// of test-only surface.
/// </summary>
/// <remarks>
/// The type is <see langword="internal"/> and reachable only through <c>InternalsVisibleTo</c>, matching the
/// test-hooks pattern the repository uses for shipping types with test-only seams (for example
/// <c>WorkspaceDocumentStore.TestHooks</c>). The property stays mutable because a test installs the resolver
/// after the margin was constructed. Production callers never set it.
/// </remarks>
internal sealed class BookmarkMarginTestHooks
{
	/// <summary>
	/// Gets or sets an optional resolver for the click position used by the margin's click handler
	/// (<see cref="LineStatusIconMarginBase"/>), instead of the event's own position, or
	/// <see langword="null"/> in production.
	/// </summary>
	/// <remarks>
	/// A synthetic event cannot carry a deterministic position, so a test that exercises the routed click
	/// path sets this resolver to a deterministic point.
	/// </remarks>
	internal Func<PointerPressedEventArgs, Point>? ClickPositionResolver { get; set; }
}
