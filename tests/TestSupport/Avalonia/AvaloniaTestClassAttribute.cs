namespace Nickelony.Testing;

/// <summary>Marks a test class whose test methods run on the Avalonia UI thread through <see cref="AvaloniaToolkitTestHost"/>.</summary>
/// <remarks>
/// A test class annotates itself with this attribute instead of <c>[TestClass]</c>, because every control must be constructed and laid
/// out on the Avalonia UI thread the headless session owns.
/// </remarks>
internal sealed class AvaloniaTestClassAttribute : Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute
{
	/// <inheritdoc/>
	public override TestMethodAttribute? GetTestMethodAttribute(TestMethodAttribute? testMethodAttribute) =>
		new AvaloniaTestMethodAttribute(testMethodAttribute ?? new TestMethodAttribute());
}
