using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;

namespace Nickelony.IDEKit.Testing;

/// <summary>
/// The Avalonia <see cref="Application"/> the headless test host runs, carrying the Fluent theme and the AvaloniaEdit control themes the templated
/// editor controls need to realize their templates.
/// </summary>
/// <remarks>
/// <para>
/// The headless session constructs this type for the tests it dispatches, so the themes apply to every hosted control without the suite touching
/// a global application instance. The AvaloniaEdit theme is loaded from the engine assembly's resource; without it a hosted <c>TextEditor</c>
/// never realizes its template and its text view never validates visual lines.
/// </para>
/// <para>
/// <see cref="BuildAvaloniaApp"/> enables the Skia renderer on the headless platform
/// (<see cref="AvaloniaHeadlessPlatformOptions.UseHeadlessDrawing"/> is <see langword="false"/>), so mounted frames carry real pixels.
/// </para>
/// </remarks>
public sealed class AvaloniaTestApp : Application
{
	private const string AvaloniaEditThemeUri = "avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml";

	/// <summary>
	/// Builds the app builder the headless session uses: Skia rendering over the headless platform, so mounted frames carry pixels.
	/// </summary>
	public static AppBuilder BuildAvaloniaApp() =>
		AppBuilder.Configure<AvaloniaTestApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

	/// <inheritdoc/>
	public override void Initialize()
	{
		Styles.Add(new FluentTheme());

		var editorThemeUri = new Uri(AvaloniaEditThemeUri);
		Styles.Add(new StyleInclude(editorThemeUri) { Source = editorThemeUri });
	}
}
