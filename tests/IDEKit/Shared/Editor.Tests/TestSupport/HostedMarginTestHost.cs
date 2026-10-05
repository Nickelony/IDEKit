#if AVALONIAEDIT
using Avalonia.Controls;
using AvaloniaEdit;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using System.Windows;
using ICSharpCode.AvalonEdit;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

/// <summary>
/// Hosts an editor together with a left margin in a test host window and asserts the layout precondition every margin
/// scenario depends on: the hosted editor has realized its visual lines.
/// </summary>
/// <remarks>
/// Centralizes the environment precondition (the host window plus the realized layout) that the margin suites otherwise repeat at each scenario.
/// Disposing the scope closes the host window.
/// </remarks>
internal sealed class HostedMarginTestHost : IDisposable
{
	private readonly HostWindow _window;

	/// <summary>
	/// Initializes a new instance of the <see cref="HostedMarginTestHost"/> class, adds <paramref name="margin"/> to the editor's left margins,
	/// shows the editor, and asserts that the layout has realized at least one visual line.
	/// </summary>
	/// <param name="editor">The editor to host.</param>
	/// <param name="margin">The margin to add to the editor's left margins.</param>
#if AVALONIAEDIT
	public HostedMarginTestHost(TextEditor editor, Control margin)
#else
	public HostedMarginTestHost(TextEditor editor, UIElement margin)
#endif
	{
		_window = TestHost.ShowEditorWithMargin(editor, margin);
		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);
	}

	/// <summary>Closes the host window.</summary>
	public void Dispose() => _window.Dispose();
}
