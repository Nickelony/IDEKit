#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
#endif

/// <summary>
/// Groups the test-only read seams of the <see cref="TextCompletionController"/> so the controller's own
/// member list stays free of test-only surface.
/// </summary>
/// <remarks>
/// The type is <see langword="internal"/> and reachable only through <c>InternalsVisibleTo</c>, matching the
/// test-hooks pattern the repository uses for shipping types with test-only seams (for example
/// <c>WorkspaceDocumentStore.TestHooks</c>). Production callers never read it.
/// </remarks>
internal sealed class TextCompletionControllerTestHooks
{
	private readonly Func<bool> _isTooltipUpdatePending;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextCompletionControllerTestHooks"/> class.
	/// </summary>
	/// <param name="isTooltipUpdatePending">
	/// The accessor that reports whether a debounced tooltip update is waiting for its delay.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="isTooltipUpdatePending"/> is <see langword="null"/>.</exception>
	internal TextCompletionControllerTestHooks(Func<bool> isTooltipUpdatePending)
	{
		ArgumentNullException.ThrowIfNull(isTooltipUpdatePending);

		_isTooltipUpdatePending = isTooltipUpdatePending;
	}

	/// <summary>
	/// Gets a value indicating whether a debounced tooltip update is waiting for its delay. Lets a test
	/// observe the tooltip debounce behaviorally without reflecting the presenter's private fields.
	/// </summary>
	internal bool IsTooltipUpdatePending => _isTooltipUpdatePending();
}
