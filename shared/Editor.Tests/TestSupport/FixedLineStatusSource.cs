#if AVALONIAEDIT
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Bookmarks;
using Nickelony.IDEKit.Core.Notifications;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.Tests;
#else
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Bookmarks;
using Nickelony.IDEKit.Core.Notifications;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.Tests;
#endif

/// <summary>
/// A line-status source test double with fixed line numbers that also supports bookmark toggling
/// and optional change notifications, so the bookmark and change-marker margin tests share one double.
/// </summary>
internal sealed class FixedLineStatusSource : IBookmarkSource, IChangeNotificationSource
{
	private readonly Func<TextDocument?> _documentProvider;
	private readonly List<int> _lineNumbers;

	/// <summary>
	/// Initializes a new instance of the <see cref="FixedLineStatusSource"/> class.
	/// </summary>
	/// <param name="documentProvider">
	/// Provides the document the fixed line numbers resolve against, or <see langword="null"/> when no
	/// document is current; reads report no lines in that state.
	/// </param>
	/// <param name="lineNumbers">The one-based line numbers the source reports.</param>
	/// <param name="notifiesChanges">
	/// Whether <see cref="ToggleBookmark"/> raises <see cref="Changed"/>. Defaults to <see langword="true"/>.
	/// </param>
	public FixedLineStatusSource(
		Func<TextDocument?> documentProvider,
		IReadOnlyList<int> lineNumbers,
		bool notifiesChanges = true)
	{
		_documentProvider = documentProvider;
		_lineNumbers = [.. lineNumbers];
		NotifiesChanges = notifiesChanges;
	}

	/// <summary>
	/// Gets a value indicating whether this source raises <see cref="Changed"/> when its bookmarks change.
	/// </summary>
	public bool NotifiesChanges { get; }

	/// <summary>
	/// Gets the number of times <see cref="ToggleBookmark"/> was called.
	/// </summary>
	public int ToggleCalls { get; private set; }

	/// <summary>
	/// Gets the line number the last <see cref="ToggleBookmark"/> call resolved.
	/// </summary>
	public int ToggledLineNumber { get; private set; }

	/// <inheritdoc/>
	public event EventHandler? Changed;

	/// <inheritdoc/>
	public IReadOnlyList<int> GetMarkedLineNumbers()
	{
		TextDocument? document = _documentProvider();

		// A missing document yields no marks, mirroring the production sources: the read runs inside a
		// margin's render pass and must not throw.
		if (document is null)
			return [];

		// Out-of-range configuration is dropped instead of being reported by the render pass, mirroring
		// how BookmarkCoordinator.RestoreBookmarks ignores line numbers outside the document.
		return [.. _lineNumbers.Where(lineNumber => lineNumber >= 1 && lineNumber <= document.LineCount)];
	}

	/// <inheritdoc/>
	public void ToggleBookmark(int offset)
	{
		ToggleCalls++;

		// A toggle needs a document, like the production coordinator, which rejects a provider that
		// returns null; only the render-pass read stays tolerant.
		TextDocument document = _documentProvider()
			?? throw new InvalidOperationException("The test source has no document to toggle a bookmark in.");

		ToggledLineNumber = document.GetLineByOffset(offset).LineNumber;

		if (NotifiesChanges)
			Changed?.Invoke(this, EventArgs.Empty);
	}
}
