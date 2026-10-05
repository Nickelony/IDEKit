using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Nickelony.Samples.Avalonia;

/// <summary>The Avalonia application of the sample: the Fluent theme and the AvaloniaEdit control themes, then the main window.</summary>
internal sealed class SampleApplication : Application
{
	private const string EditorThemeUri = "avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml";

	public override void Initialize()
	{
		Styles.Add(new FluentTheme());

		var editorThemeUri = new Uri(EditorThemeUri);
		Styles.Add(new StyleInclude(editorThemeUri) { Source = editorThemeUri });

		// The Fluent defaults size tabs and list rows for touch, and an editor window wants them compact.
		Styles.Add(
			new Style(selector => selector.OfType<TabItem>())
			{
				Setters =
				{
					new Setter(TemplatedControl.FontSizeProperty, 14.0),
					new Setter(Layoutable.MinHeightProperty, 30.0),
					new Setter(TemplatedControl.PaddingProperty, new Thickness(10, 2)),
				},
			}
		);

		Styles.Add(
			new Style(selector => selector.OfType<ListBoxItem>())
			{
				Setters = { new Setter(TemplatedControl.PaddingProperty, new Thickness(8, 3)), new Setter(Layoutable.MinHeightProperty, 0.0) },
			}
		);

		// The tree template reads its row height from a resource, so the explorer rows shrink through it.
		Resources["TreeViewItemMinHeight"] = 24.0;
	}

	public override async void OnFrameworkInitializationCompleted()
	{
		base.OnFrameworkInitializationCompleted();

		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			SampleWorkbench workbench = await SampleWorkbench.CreateAsync(
				SampleFiles.FromCommandLine(desktop.Args ?? []),
				SampleServerOptions.FromEnvironment()
			);

			desktop.MainWindow = new SampleWindow(workbench);
			desktop.MainWindow.Show();
		}
	}
}
