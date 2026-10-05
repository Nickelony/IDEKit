#if AVALONIAEDIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Hover;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Hover;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Hover;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Hover;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>
/// Covers the hover popup over a shown editor: where it anchors, the closes it owns, the refusal while its window is inactive, and the chrome
/// its toolkit needs.
/// </summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class HoverPresenterTests
{
	private const string Text = "first line\nsecond line\nthird line";

	private static readonly int s_thirdLineOffset = Text.IndexOf("third", StringComparison.Ordinal);

	[TestMethod]
	public void Show_HoverWithARange_AnchorsAtTheRangeStartAndNotAtTheOffset()
	{
		using var host = new PresenterHost();

		host.Presenter.Show(new HoverInformation("hover") { Range = new TextRange(0, 5) }, null, s_thirdLineOffset);
		Rect atRange = host.Anchor;

		host.Presenter.Show(new HoverInformation("hover"), null, 0);
		Rect atRangeStart = host.Anchor;

		host.Presenter.Show(new HoverInformation("hover"), null, s_thirdLineOffset);
		Rect atOffset = host.Anchor;

		Assert.IsTrue(host.Presenter.IsOpen);
		Assert.AreEqual(atRangeStart, atRange);
		Assert.AreNotEqual(atOffset, atRange);
	}

	[TestMethod]
	public void Show_DiagnosticOnly_AnchorsAtTheDiagnosticStart()
	{
		using var host = new PresenterHost();

		host.Presenter.Show(null, new Diagnostic(DiagnosticSeverity.Warning, "warning", new TextRange(0, 5)), s_thirdLineOffset);
		Rect atDiagnostic = host.Anchor;

		host.Presenter.Show(new HoverInformation("hover"), null, 0);

		Assert.AreEqual(host.Anchor, atDiagnostic);
	}

	[TestMethod]
	public void Show_DiagnosticOfAMovedUnderline_AnchorsWhereItsTextIsNow()
	{
		using var host = new PresenterHost();

		var provider = new ThirdWordProvider();

		using DiagnosticsController diagnostics = DiagnosticsControllerFactory.Create(
			textArea: host.Editor.TextArea,
			documentId: "hover-anchor.txt",
			provider,
			source: null
		);

		DispatcherTestHelpers.PumpUntil(() => diagnostics.Current.Count == 1);

		Diagnostic reported = diagnostics.Current[0];
		host.Presenter.Show(null, reported, reported.Range.Offset);
		Rect atReportedRange = host.Anchor;

		// A line typed above moves the underline before the next pull answers, and the diagnostic the controller finds carries that move.
		host.Editor.Document.Insert(0, "zero\n");
		host.Editor.UpdateLayout();

		int movedOffset = host.Editor.Text.IndexOf("third", StringComparison.Ordinal);
		Diagnostic? found = diagnostics.FindAt(movedOffset + 1);

		Assert.IsNotNull(found);

		host.Presenter.Show(null, found, movedOffset + 1);
		Rect atFound = host.Anchor;

		host.Presenter.Show(null, new Diagnostic(DiagnosticSeverity.Warning, "warning", new TextRange(movedOffset, 5)), 0);

		Assert.AreEqual(host.Anchor, atFound);
		Assert.AreNotEqual(atReportedRange, atFound);
	}

	[TestMethod]
	public void Show_NothingToShow_HidesThePopup()
	{
		using var host = new PresenterHost();

		host.Presenter.Show(new HoverInformation("hover"), null, 0);
		host.Presenter.Show(null, null, null);

		Assert.IsFalse(host.Presenter.IsOpen);
	}

	[TestMethod]
	public void TypedText_HidesThePopup()
	{
		using var host = new PresenterHost();

		host.Presenter.Show(new HoverInformation("hover"), null, 0);
		host.Editor.TextArea.PerformTextInput("x");

		Assert.IsFalse(host.Presenter.IsOpen);
	}

	[TestMethod]
	public void TextRemoved_AsABackspaceRemovesIt_HidesThePopup()
	{
		using var host = new PresenterHost();

		host.Presenter.Show(new HoverInformation("hover"), null, 0);
		host.Editor.Document.Remove(0, 1);

		Assert.IsFalse(host.Presenter.IsOpen);
	}

	[TestMethod]
	public void TextInsertedWithoutTyping_AsAPasteOrAnotherViewInsertsIt_HidesThePopup()
	{
		using var host = new PresenterHost();

		host.Presenter.Show(new HoverInformation("hover"), null, 0);
		host.Editor.Document.Insert(0, "pasted ");

		Assert.IsFalse(host.Presenter.IsOpen);
	}

	[TestMethod]
	public void DocumentSwapped_HidesThePopup()
	{
		using var host = new PresenterHost();

		host.Presenter.Show(new HoverInformation("hover"), null, 0);
		host.Editor.Document = new TextDocument(Text);

		Assert.IsFalse(host.Presenter.IsOpen);

		// A show over the new document follows that document's changes.
		host.Presenter.Show(new HoverInformation("hover"), null, 0);
		host.Editor.Document.Insert(0, "x");

		Assert.IsFalse(host.Presenter.IsOpen);
	}

	[TestMethod]
	public void TextAreaLeavingTheTree_ClosesThePopup()
	{
		using var host = new PresenterHost();

		host.Presenter.Show(new HoverInformation("hover"), null, 0);
		host.Panel.Children.Remove(host.Editor);
		DispatcherTestHelpers.PumpUntilIdle();

		// The text area did not hold the focus, so only the unload of the text area, or the toolkit closing the popup, closes it.
		Assert.IsFalse(host.Presenter.IsOpen);
	}

	[TestMethod]
	public void Hide_ClosesThePopupAndLeavesTheKeysToTheEditor()
	{
		using var host = new PresenterHost();

		host.Presenter.Show(new HoverInformation("hover"), null, 0);
		host.Presenter.Hide();

		Assert.IsFalse(host.Presenter.IsOpen);
		Assert.IsFalse(host.PressEscape());
	}

	[TestMethod]
	public void Show_WhileTheWindowIsInactive_BuildsNoContent()
	{
		int builds = 0;

		using var host = new PresenterHost(
			activate: false,

			buildContent: (_, _) =>
			{
				builds++;
				return new TextBlock { Text = "content" };
			}
		);

		host.Presenter.Show(new HoverInformation("hover"), null, 0);

		Assert.AreEqual(0, builds);
	}

	[TestMethod]
	public void Show_InATextAreaInsideAPopup_FollowsTheWindowThatHostsThePopup()
	{
		TextEditor editor = TestHost.CreateEditor(Text);
		editor.Height = 100;
		editor.Width = 300;

		var anchor = new TextBlock { Text = "anchor" };
		var outerPopup = new Popup { PlacementTarget = anchor, Child = editor };

		using HostWindow window = TestHost.ShowInHostWindow(new StackPanel { Children = { anchor, outerPopup } });
		TestWindowActivation.Activate(window);

		outerPopup.IsOpen = true;
		DispatcherTestHelpers.PumpUntilIdle();

		using var presenter = new HoverPresenter(editor.TextArea, static (_, _) => new TextBlock { Text = "content" });
		presenter.Show(new HoverInformation("hover"), null, 0);

		// The window that hosts the popup decides. On a desktop the popup is a window of its own that is never active; the headless platform draws
		// it in the window's overlay layer, so this pins the result and the walk up from a popup root runs only on a desktop.
		Assert.IsTrue(presenter.IsOpen);

		presenter.Dispose();
		outerPopup.IsOpen = false;
	}

	[TestMethod]
	public void Escape_ClosesThePopupAndHandlesTheKey()
	{
		using var host = new PresenterHost();

		host.Presenter.Show(new HoverInformation("hover"), null, 0);

		Assert.IsTrue(host.PressEscape());
		Assert.IsFalse(host.Presenter.IsOpen);
	}

	[TestMethod]
	public void FocusLeavingTheTextArea_ClosesThePopup()
	{
		using var host = new PresenterHost();

		Assert.IsTrue(host.Editor.TextArea.Focus());
		host.Presenter.Show(new HoverInformation("hover"), null, 0);

		Assert.IsTrue(host.OtherControl.Focus());
		DispatcherTestHelpers.PumpUntilIdle();

		Assert.IsFalse(host.Presenter.IsOpen);
	}

	[TestMethod]
	public void WindowDeactivating_ClosesThePopup()
	{
		using var host = new PresenterHost();

		host.Presenter.Show(new HoverInformation("hover"), null, 0);
		TestWindowActivation.Deactivate(host.Window);

		Assert.IsFalse(host.Presenter.IsOpen);
	}

	[TestMethod]
	public void Show_WhileTheWindowIsInactive_ShowsNothing()
	{
		using var host = new PresenterHost(activate: false);

		// A topmost popup of an inactive window would cover the application in the foreground.
		host.Presenter.Show(new HoverInformation("hover"), null, 0);

		Assert.IsFalse(host.Presenter.IsOpen);

		TestWindowActivation.Activate(host.Window);
		host.Presenter.Show(new HoverInformation("hover"), null, 0);

		Assert.IsTrue(host.Presenter.IsOpen);
	}

	[TestMethod]
	public void Popup_CarriesTheChromeItsToolkitNeedsToRenderTheContent()
	{
		using var host = new PresenterHost();

		host.Presenter.Show(new HoverInformation("hover"), null, 0);

#if AVALONIAEDIT
		// A popup outside the logical tree takes no control themes, so a code block of the content would render as an empty frame.
		Assert.AreSame(host.Editor.TextArea, ((ILogical)host.Presenter.Popup.Popup).LogicalParent);
#else
		// The popup is its own window, so it stays open until it is told to close, and draws the frame's shadow inside a transparent margin.
		Assert.IsTrue(host.Presenter.Popup.Popup.AllowsTransparency);
		Assert.IsTrue(host.Presenter.Popup.Popup.StaysOpen);
		Assert.AreNotEqual(new Thickness(0), ((FrameworkElement)host.Presenter.Popup.Popup.Child).Margin);
#endif
	}

	[TestMethod]
	public void Constructor_WithPresentation_StartsTheThemeAndFramesThePopupWithIt()
	{
		HoverPopupTheme theme = HoverPopupTheme.Default with { MaxWidth = 222.0 };

		using var host = new PresenterHost(presentation: new HoverPresentationOptions { Theme = theme });

		host.Presenter.Show(new HoverInformation("hover"), null, 0);

		Assert.AreEqual(theme, host.Presenter.Theme);
		Assert.AreEqual(222.0, ((Border)host.Presenter.Popup.Popup.Child!).MaxWidth);
	}

	[TestMethod]
	public void Constructor_WithoutPresentation_StartsTheDefaultTheme()
	{
		using var host = new PresenterHost();
		Assert.AreEqual(HoverPresentationOptions.Default.Theme, host.Presenter.Theme);
	}

	[TestMethod]
	public void PresentationOptions_NullTheme_ThrowsAtAssignment() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => HoverPresentationOptions.Default with { Theme = null! });

	[TestMethod]
	public void Theme_Assigned_RestylesAnOpenPopup()
	{
		using var host = new PresenterHost();

		host.Presenter.Show(new HoverInformation("hover"), null, 0);
		host.Presenter.Theme = HoverPopupTheme.Default with { MaxWidth = 123.0 };

		Assert.AreEqual(123.0, ((Border)host.Presenter.Popup.Popup.Child!).MaxWidth);
	}

	[TestMethod]
	public void Theme_InvalidValues_ThrowAtAssignment()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new HoverPopupTheme { Background = null! });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new HoverPopupTheme { BorderThickness = -1.0 });

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			new HoverPopupTheme { Padding = new Thickness(left: 1, top: -1, right: 1, bottom: 1) }
		);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new HoverPopupTheme { MaxWidth = 0.0 });
	}

	[TestMethod]
	public void Show_AfterDispose_ShowsNothing()
	{
		using var host = new PresenterHost();

		host.Presenter.Dispose();
		host.Presenter.Show(new HoverInformation("hover"), null, 0);

		Assert.IsFalse(host.Presenter.IsOpen);
	}

	/// <summary>Reports a warning on the word "third" of the text it is asked about.</summary>
	private sealed class ThirdWordProvider : IDiagnosticsProvider
	{
		public Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(DiagnosticsRequest request, CancellationToken cancellationToken = default)
		{
			int offset = request.DocumentText.IndexOf("third", StringComparison.Ordinal);

			return Task.FromResult<IReadOnlyList<Diagnostic>>(
				offset < 0 ? [] : [new Diagnostic(DiagnosticSeverity.Warning, "warning", new TextRange(offset, 5))]
			);
		}
	}

	/// <summary>A hover presenter over the editor of a popup host.</summary>
	private sealed class PresenterHost : IDisposable
	{
		private readonly PopupTestHost _host;

		public PresenterHost(
			bool activate = true,
			Func<HoverInformation?, Diagnostic?, TextBlock?>? buildContent = null,
			HoverPresentationOptions? presentation = null
		)
		{
			_host = new PopupTestHost(Text, activate);

			Presenter = new HoverPresenter(
				_host.Editor.TextArea,
				buildContent ?? (static (_, _) => new TextBlock { Text = "content" }),
				presentation
			);
		}

		public TextEditor Editor => _host.Editor;

		public TextBox OtherControl => _host.OtherControl;

		public HostWindow Window => _host.Window;

		public StackPanel Panel => _host.Panel;

		public bool PressEscape() => _host.PressEscape();

		public HoverPresenter Presenter { get; }

		public Rect Anchor => PopupTestHost.AnchorOf(Presenter.Popup);

		public void Dispose()
		{
			Presenter.Dispose();
			_host.Dispose();
		}
	}
}
