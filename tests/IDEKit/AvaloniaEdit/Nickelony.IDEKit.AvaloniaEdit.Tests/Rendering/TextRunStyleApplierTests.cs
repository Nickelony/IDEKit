using Avalonia.Media;
using AvaloniaEdit.Rendering;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

/// <summary>
/// The AvaloniaEdit binding of the shared <see cref="TextRunStyleApplierTestsBase"/> run-style scenarios; it
/// supplies the mirror's font-family and typeface-construction hooks, and reports the null-argument cases
/// inconclusive because Avalonia's <c>Typeface</c> is a value type.
/// </summary>
[AvaloniaTestClass]
public sealed class TextRunStyleApplierTests : TextRunStyleApplierTestsBase
{
	protected override string GetFontFamilyName(FontFamily fontFamily) => fontFamily.Name;

	protected override Typeface CreateNamedTypeface(string fontFamilyName) => new(new FontFamily(fontFamilyName));

	protected override void AssertNullTypefaceRejected(VisualLineElement element)
		=> Assert.Inconclusive(
			"Avalonia.Media.Typeface is a value type, so there is no null typeface argument to reject; "
			+ "the mirror's Apply overload takes the typeface by value and carries no null check for it.");

	protected override void AssertNullBaseTypefaceRejected()
		=> Assert.Inconclusive(
			"Avalonia.Media.Typeface is a value type, so there is no null base-typeface argument to reject; "
			+ "the mirror's CreateTypeface takes the base typeface by value and carries no null check.");
}
