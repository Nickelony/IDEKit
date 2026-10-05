using System.Windows;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;

namespace Nickelony.IDEKit.Testing;

/// <summary>Adds the AvalonEdit editor helpers to the WPF hosting helpers of <see cref="WpfToolkitTestHost"/>.</summary>
internal sealed class WpfTestHost : WpfToolkitTestHost
{
	private WpfTestHost() { }

	/// <summary>Creates a text editor with the given document text on the current thread.</summary>
	/// <returns>The created editor; owned by the caller.</returns>
	public static TextEditor CreateEditor(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		return new TextEditor { Document = new TextDocument(text) };
	}

	/// <summary>Creates a text editor with the given text and shows it in a non-activating host window.</summary>
	/// <param name="configureEditor">
	/// An optional callback that configures the editor before it is shown, for example to pin the design font size before layout runs.
	/// </param>
	/// <returns>The editor and its host window; dispose the host window to close it.</returns>
	public static (TextEditor Editor, HostWindow HostWindow) ShowHostedEditor(string text = "", Action<TextEditor>? configureEditor = null)
	{
		TextEditor editor = CreateEditor(text);
		configureEditor?.Invoke(editor);
		return (editor, ShowInHostWindow(editor));
	}

	/// <summary>Adds the margin to the editor's left margins and shows the editor in a host window.</summary>
	/// <returns>The host window; dispose it to close the window.</returns>
	public static HostWindow ShowEditorWithMargin(TextEditor editor, UIElement margin)
	{
		ArgumentNullException.ThrowIfNull(editor);
		ArgumentNullException.ThrowIfNull(margin);

		editor.TextArea.LeftMargins.Add(margin);
		return ShowInHostWindow(editor);
	}
}
