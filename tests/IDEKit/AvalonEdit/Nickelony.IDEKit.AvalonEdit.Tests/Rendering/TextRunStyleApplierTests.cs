using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

/// <summary>
/// The AvalonEdit binding of the shared <see cref="TextRunStyleApplierTestsBase"/> run-style scenarios; it
/// supplies the WPF font-family, typeface-construction, and null-argument-assertion hooks.
/// </summary>
[STATestClass]
[TestCategory(TestCategories.InteractiveWindow)]
public sealed class TextRunStyleApplierTests : TextRunStyleApplierTestsBase
{
	protected override string GetFontFamilyName(FontFamily fontFamily) => fontFamily.Source;

	protected override Typeface CreateNamedTypeface(string fontFamilyName) => new(fontFamilyName);

	protected override void AssertNullTypefaceRejected(VisualLineElement element)
		=> Assert.ThrowsExactly<ArgumentNullException>(
			() => TextRunStyleApplier.Apply(element, CreateRedStyle(), null!));

	protected override void AssertNullBaseTypefaceRejected()
		=> Assert.ThrowsExactly<ArgumentNullException>(
			() => TextRunStyleApplier.CreateTypeface(null!, CreateRedStyle()));
}
