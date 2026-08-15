namespace Nickelony.IDEKit.Testing;

/// <summary>
/// Marks a test class whose test methods run on the Avalonia UI thread through
/// <see cref="AvaloniaTestHost"/>, mirroring the way the WPF suites use <c>STATestClass</c>.
/// </summary>
/// <remarks>
/// A test class in the Avalonia mirror suites annotates itself with this attribute instead of
/// <c>[TestClass]</c>, because every editor control must be constructed and laid out on the Avalonia UI
/// thread the headless session owns.
/// </remarks>
internal sealed class AvaloniaTestClassAttribute : TestClassAttribute
{
	/// <inheritdoc/>
	public override TestMethodAttribute? GetTestMethodAttribute(TestMethodAttribute? testMethodAttribute)
		=> new AvaloniaTestMethodAttribute(testMethodAttribute ?? new TestMethodAttribute());
}
