#if AVALONIAEDIT
using Avalonia;
using Avalonia.Controls;
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>Shows an editor with a second focusable control beside it in a window, which the feature popup tests open their popups over.</summary>
/// <remarks>
/// The window is active unless the host is created inactive, because a feature popup refuses to show over an inactive window.
/// </remarks>
internal sealed class PopupTestHost : IDisposable
{
	public PopupTestHost(string text, bool activate = true)
	{
		Editor = TestHost.CreateEditor(text);
		Editor.Height = 200;
		OtherControl = new TextBox();
		Panel = new StackPanel { Children = { Editor, OtherControl } };
		Window = TestHost.ShowInHostWindow(Panel);
		PressEscape = TestPopupEvents.WatchEscape(Editor.TextArea);

		if (activate)
			TestWindowActivation.Activate(Window);
		else if (Window.IsActive)
			TestWindowActivation.Deactivate(Window);
	}

	public TextEditor Editor { get; }

	/// <summary>A control that takes the keyboard focus away from the editor.</summary>
	public TextBox OtherControl { get; }

	public HostWindow Window { get; }

	/// <summary>The panel that holds the editor and the other control, which a test removes the editor from.</summary>
	public StackPanel Panel { get; }

	/// <summary>Presses Esc in the editor and reports whether a popup took the key before the editor's own key handling.</summary>
	public Func<bool> PressEscape { get; }

	/// <summary>Returns the rectangle a popup is placed below, in the coordinate space of its text area.</summary>
	public static Rect AnchorOf(FeaturePopupPresenter popup)
	{
#if AVALONIAEDIT
		return popup.Popup.PlacementRect!.Value;
#else
		return popup.Popup.PlacementRectangle;
#endif
	}

	public void Dispose() => Window.Dispose();
}
