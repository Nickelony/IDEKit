using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

namespace Nickelony.Testing;

/// <summary>
/// The Avalonia <see cref="Application"/> the headless test host runs, carrying the Fluent theme that templated controls need to realize
/// their templates.
/// </summary>
/// <remarks>
/// <para>
/// The headless session constructs this type for the tests it dispatches, so the theme applies to every hosted control without the suite touching
/// a global application instance.
/// </para>
/// <para>
/// A family whose controls need more than the Fluent theme, such as a real renderer or an engine control theme,
/// compiles its own <c>AvaloniaTestApp</c> in place of this one.
/// </para>
/// </remarks>
public sealed class AvaloniaTestApp : Application
{
	/// <summary>Builds the app builder the headless session uses.</summary>
	public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<AvaloniaTestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());

	/// <inheritdoc/>
	public override void Initialize() => Styles.Add(new FluentTheme());
}
