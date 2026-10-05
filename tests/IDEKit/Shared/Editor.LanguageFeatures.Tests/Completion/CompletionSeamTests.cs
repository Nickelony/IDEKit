#if AVALONIAEDIT
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionWindowTestHelpers;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionWindowTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>Covers the completion seam members that both bindings declare: the activation policy, the DPI factor, and the formatted text.</summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class CompletionSeamTests
{
	[TestMethod]
	public void EnsureNonActivatable_CalledTwiceBeforeShow_LeavesTheWindowOpenAndClosable()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor("sample");

		using (hostWindow)
		{
			var completionWindow = new CompletionWindow(editor.TextArea);

			// Calling twice must stay harmless: the WPF hook installs once, and the Avalonia popup needs no hook.
			CompletionSeam.EnsureNonActivatable(completionWindow);
			CompletionSeam.EnsureNonActivatable(completionWindow);

			completionWindow.Show();

			try
			{
				Assert.IsTrue(IsShown(completionWindow));
			}
			finally
			{
				completionWindow.Close();
			}
		}
	}

	[TestMethod]
	public void CreateWindow_AppliesTheThemeAndTheHeightCapToTheElementThatDrawsTheFrame()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor("sample");

		using (hostWindow)
		{
			CompletionWindowTheme theme = CompletionWindowTheme.Default with { BorderThickness = 3.0 };

			CompletionWindow completionWindow = CompletionSeam.CreateWindow(editor.TextArea, theme, maxHeight: 123.0);

			var frame = GetFrameElement(completionWindow);

			Assert.AreEqual(123.0, frame.MaxHeight);
			Assert.AreEqual(new Thickness(3.0), frame.BorderThickness);
			Assert.AreSame(theme.Background, frame.Background);
		}
	}

	[TestMethod]
	public void SetContentWidth_SizesTheElementThatDrawsTheContent()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor("sample");

		using (hostWindow)
		{
			var completionWindow = new CompletionWindow(editor.TextArea);

			CompletionSeam.SetContentWidth(completionWindow, 222.0);

			Assert.AreEqual(222.0, GetFrameElement(completionWindow).Width);
		}
	}

	[TestMethod]
	public void GetPixelsPerDip_ReturnsTheFactorTheBindingsMeasurementDependsOn()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor();

		using (hostWindow)
		{
			double factor = CompletionSeam.GetPixelsPerDip(editor.TextArea);
			Assert.AreEqual(GetExpectedPixelsPerDip(editor.TextArea), factor);
		}
	}

	[TestMethod]
	public void CreateFormattedText_LongerText_MeasuresWider()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor(configureEditor: static editor => TestHost.PinDesignFontSize(editor));

		using (hostWindow)
		{
			Typeface typeface = CreateTypeface(editor);
			double factor = CompletionSeam.GetPixelsPerDip(editor.TextArea);

			FormattedText shortText = CompletionSeam.CreateFormattedText(textArea: editor.TextArea, text: "ab", typeface, pixelsPerDip: factor);
			FormattedText longText = CompletionSeam.CreateFormattedText(textArea: editor.TextArea, text: "abababab", typeface, pixelsPerDip: factor);

			Assert.IsGreaterThan(0.0, shortText.WidthIncludingTrailingWhitespace);
			Assert.IsGreaterThan(shortText.WidthIncludingTrailingWhitespace, longText.WidthIncludingTrailingWhitespace);
		}
	}
#if !AVALONIAEDIT

	[TestMethod]
	public void GetPixelsPerDip_Wpf_FollowsTheRootDpiOfTheHostWindow()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor();

		using (hostWindow)
		{
			VisualTreeHelper.SetRootDpi(hostWindow, new DpiScale(1.5, 1.5));
			Assert.AreEqual(1.5, CompletionSeam.GetPixelsPerDip(editor.TextArea));
		}
	}

	[TestMethod]
	public void CreateFormattedText_Wpf_AppliesTheGivenDpiFactor()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor();

		using (hostWindow)
		{
			FormattedText formattedText = CompletionSeam.CreateFormattedText(
				textArea: editor.TextArea,
				text: "sample",
				typeface: CreateTypeface(editor),
				pixelsPerDip: 2.5
			);

			Assert.AreEqual(2.5, formattedText.PixelsPerDip);
		}
	}
#endif

	private static Typeface CreateTypeface(TextEditor editor)
	{
		return new Typeface(
			fontFamily: editor.TextArea.FontFamily,
			style: editor.TextArea.FontStyle,
			weight: editor.TextArea.FontWeight,
			stretch: editor.TextArea.FontStretch
		);
	}
}
