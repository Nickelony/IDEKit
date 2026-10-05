#if AVALONIAEDIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.SignatureHelp;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.SignatureHelp;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>
/// Covers the signature-help popup over a shown editor: where it anchors, the closes it owns and reports, the typed text it survives, and the refusal
/// while its window is inactive.
/// </summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class SignatureHelpPresenterTests
{
	private const string Text = "first(line)\nsecond(line)\nthird(line)";

	private static readonly SignatureHelpInformation s_answer = new([new SignatureInformation("first(line)")]);

	[TestMethod]
	public void Show_AnchorsAtTheOffset()
	{
		using var host = new PresenterHost();

		host.Presenter.Show(s_answer, 0);
		Rect atStart = host.Anchor;

		host.Presenter.Show(s_answer, Text.IndexOf("third", StringComparison.Ordinal));

		Assert.IsTrue(host.Presenter.IsOpen);
		Assert.AreNotEqual(atStart, host.Anchor);
	}

	[TestMethod]
	public void TypedText_LeavesThePopupOpen()
	{
		using var host = new PresenterHost();

		host.Presenter.Show(s_answer, 6);
		host.Editor.TextArea.PerformTextInput("x");

		// Typing goes on inside the call the popup describes.
		Assert.IsTrue(host.Presenter.IsOpen);
		Assert.AreEqual(0, host.DismissedCount);
	}

	[TestMethod]
	public void Escape_ClosesThePopupAndRaisesDismissed()
	{
		using var host = new PresenterHost();

		host.Presenter.Show(s_answer, 6);

		Assert.IsTrue(host.PressEscape());
		Assert.IsFalse(host.Presenter.IsOpen);
		Assert.AreEqual(1, host.DismissedCount);
	}

	[TestMethod]
	public void FocusLeavingTheTextArea_ClosesThePopupAndRaisesDismissed()
	{
		using var host = new PresenterHost();

		Assert.IsTrue(host.Editor.TextArea.Focus());
		host.Presenter.Show(s_answer, 6);

		Assert.IsTrue(host.OtherControl.Focus());
		DispatcherTestHelpers.PumpUntilIdle();

		Assert.IsFalse(host.Presenter.IsOpen);
		Assert.AreEqual(1, host.DismissedCount);
	}

	[TestMethod]
	public void WindowDeactivating_ClosesThePopupAndRaisesDismissed()
	{
		using var host = new PresenterHost();

		host.Presenter.Show(s_answer, 6);
		TestWindowActivation.Deactivate(host.Window);

		Assert.IsFalse(host.Presenter.IsOpen);
		Assert.AreEqual(1, host.DismissedCount);
	}

	[TestMethod]
	public void Hide_ClosesThePopupWithoutRaisingDismissed()
	{
		using var host = new PresenterHost();

		host.Presenter.Show(s_answer, 6);
		host.Presenter.Hide();

		// The close handlers left with the popup, so a later key is the editor's again and a deactivation reports nothing.
		Assert.IsFalse(host.PressEscape());
		TestWindowActivation.Deactivate(host.Window);

		Assert.IsFalse(host.Presenter.IsOpen);
		Assert.AreEqual(0, host.DismissedCount);
	}

	[TestMethod]
	public void Show_WhileTheWindowIsInactive_ShowsNothing()
	{
		using var host = new PresenterHost(activate: false);

		host.Presenter.Show(s_answer, 6);

		Assert.IsFalse(host.Presenter.IsOpen);
		Assert.AreEqual(0, host.DismissedCount);
	}

	[TestMethod]
	public void Show_WhileTheWindowIsInactive_BuildsNoContent()
	{
		using var host = new PresenterHost(activate: false);

		host.Presenter.Show(s_answer, 6);

		Assert.AreEqual(0, host.BuildCount);
	}

	[TestMethod]
	public void Constructor_WithPresentation_StartsTheThemeAndFramesThePopupWithIt()
	{
		SignatureHelpPopupTheme theme = SignatureHelpPopupTheme.Default with { MaxWidth = 432.0 };

		using var host = new PresenterHost(presentation: new SignatureHelpPresentationOptions { Theme = theme });

		host.Presenter.Show(s_answer, 6);

		Assert.AreEqual(theme, host.Presenter.Theme);
		Assert.AreEqual(432.0, ((Border)host.Presenter.Popup.Popup.Child!).MaxWidth);
	}

	[TestMethod]
	public void Constructor_WithoutPresentation_StartsTheDefaultTheme()
	{
		using var host = new PresenterHost();
		Assert.AreEqual(SignatureHelpPresentationOptions.Default.Theme, host.Presenter.Theme);
	}

	[TestMethod]
	public void PresentationOptions_NullTheme_ThrowsAtAssignment() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => SignatureHelpPresentationOptions.Default with { Theme = null! });

	[TestMethod]
	public void Theme_Assigned_RestylesAnOpenPopup()
	{
		using var host = new PresenterHost();

		host.Presenter.Show(s_answer, 6);
		host.Presenter.Theme = SignatureHelpPopupTheme.Default with { MaxWidth = 321.0 };

		Assert.AreEqual(321.0, ((Border)host.Presenter.Popup.Popup.Child!).MaxWidth);
	}

	[TestMethod]
	public void ViewScrolling_MovesTheOpenPopupWithItsText()
	{
		string text = string.Join("\n", Enumerable.Range(0, 80).Select(static line => $"call{line}(line)"));
		using var host = new PresenterHost(text: text);

		host.Presenter.Show(s_answer, text.IndexOf("call5(", StringComparison.Ordinal) + 6);
		Rect beforeTheScroll = host.Anchor;

		host.Editor.ScrollToLine(60);
		DispatcherTestHelpers.PumpFrames(TimeSpan.FromMilliseconds(100));

		Assert.IsTrue(host.Presenter.IsOpen);
		Assert.AreNotEqual(beforeTheScroll, host.Anchor);
	}

	[TestMethod]
	public void Show_ContentBuilderReturnsNull_HidesThePopup()
	{
		using var host = new PresenterHost();
		host.Presenter.Show(s_answer, 6);

		host.ShowsContent = false;
		host.Presenter.Show(s_answer, 6);

		Assert.IsFalse(host.Presenter.IsOpen);
		Assert.AreEqual(0, host.DismissedCount);
	}

	[TestMethod]
	public void Popup_CarriesTheChromeItsToolkitNeedsToRenderTheContent()
	{
		using var host = new PresenterHost();

		host.Presenter.Show(s_answer, 6);

#if AVALONIAEDIT
		Assert.AreSame(host.Editor.TextArea, ((ILogical)host.Presenter.Popup.Popup).LogicalParent);
#else
		Assert.IsTrue(host.Presenter.Popup.Popup.AllowsTransparency);
		Assert.IsTrue(host.Presenter.Popup.Popup.StaysOpen);
#endif
	}

	[TestMethod]
	public void Theme_InvalidValues_ThrowAtAssignment()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new SignatureHelpPopupTheme { BorderBrush = null! });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SignatureHelpPopupTheme { CornerRadius = double.NaN });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SignatureHelpPopupTheme { Padding = new Thickness(-1) });
	}

	/// <summary>A signature-help presenter over the editor of a popup host, which counts the presenter's dismissals.</summary>
	private sealed class PresenterHost : IDisposable
	{
		private readonly PopupTestHost _host;

		public PresenterHost(bool activate = true, string text = Text, SignatureHelpPresentationOptions? presentation = null)
		{
			_host = new PopupTestHost(text, activate);

			Presenter = new SignatureHelpPresenter(
				_host.Editor.TextArea,

				_ =>
				{
					BuildCount++;
					return ShowsContent ? new TextBlock { Text = "content" } : null;
				},

				presentation
			);

			Presenter.Dismissed += (_, _) => DismissedCount++;
		}

		public TextEditor Editor => _host.Editor;

		public TextBox OtherControl => _host.OtherControl;

		public HostWindow Window => _host.Window;

		public bool PressEscape() => _host.PressEscape();

		public SignatureHelpPresenter Presenter { get; }

		public bool ShowsContent { get; set; } = true;

		public int DismissedCount { get; private set; }

		public int BuildCount { get; private set; }

		public Rect Anchor => PopupTestHost.AnchorOf(Presenter.Popup);

		public void Dispose()
		{
			Presenter.Dispose();
			_host.Dispose();
		}
	}
}
