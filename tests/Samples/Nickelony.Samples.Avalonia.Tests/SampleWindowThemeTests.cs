using Avalonia;
using Avalonia.Styling;

namespace Nickelony.Samples.Avalonia.Tests;

/// <summary>Covers the theme of the window chrome, which the Avalonia window sets on the application as a requested theme variant.</summary>
[TestClass]
public sealed class SampleWindowThemeTests
{
	[TestMethod]
	public async Task ToggleTheme_SwitchesTheVariantOfTheWindowChrome()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			window =>
			{
				window.Workbench.SetTheme("Dark");
				Assert.AreEqual(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);

				window.Workbench.SetTheme("Light");
				Assert.AreEqual(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);

				return Task.CompletedTask;
			}
		);
	}
}
