#if AVALONIAEDIT
using Avalonia.Media;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows.Media;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>Pins the validation of the host-side code-action menu theme, which rejects a missing brush where it is assigned.</summary>
[STATestClass]
public sealed class CodeActionMenuThemeTests
{
	[TestMethod]
	public void PresentationOptions_NullMembers_ThrowAtAssignment()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => CodeActionPresentationOptions.Default with { MenuTheme = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => CodeActionPresentationOptions.Default with { MenuOptions = null! });
	}

	[TestMethod]
	public void Theme_NullBrushes_ThrowAtAssignment()
	{
		// The theme self-validates, so the failure site is the offending object initializer instead of the
		// presenter constructor (matching the options records).
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new CodeActionMenuTheme
			{
				BorderBrush = null!,
				Background = Brushes.Black,
				Foreground = Brushes.White,
			}
		);

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new CodeActionMenuTheme
			{
				BorderBrush = Brushes.Gray,
				Background = null!,
				Foreground = Brushes.White,
			}
		);

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new CodeActionMenuTheme
			{
				BorderBrush = Brushes.Gray,
				Background = Brushes.Black,
				Foreground = null!,
			}
		);
	}
}
