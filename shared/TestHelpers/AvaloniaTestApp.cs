using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;

namespace Nickelony.IDEKit.Testing;

/// <summary>
/// The Avalonia <see cref="Application"/> the headless test host runs, carrying the Fluent theme and the
/// AvaloniaEdit control themes the templated editor controls need to realize their templates.
/// </summary>
/// <remarks>
/// The headless session constructs this type once per dispatched test, so the themes are applied to every
/// hosted control without the suite touching a global application instance. The AvaloniaEdit theme is
/// loaded from the engine assembly's resource; without it a hosted <c>TextEditor</c> never realizes its
/// template and its text view never validates visual lines.
/// </remarks>
public sealed class AvaloniaTestApp : Application
{
	private const string AvaloniaEditThemeUri = "avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml";

	/// <inheritdoc/>
	public override void Initialize()
	{
		Styles.Add(new FluentTheme());

		var editorThemeUri = new Uri(AvaloniaEditThemeUri);
		Styles.Add(new StyleInclude(editorThemeUri) { Source = editorThemeUri });
	}
}
