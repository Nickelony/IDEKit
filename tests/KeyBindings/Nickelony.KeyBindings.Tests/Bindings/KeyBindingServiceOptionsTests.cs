using Nickelony.KeyBindings.OverrideEntries;
using Nickelony.KeyBindings.Testing;
using Nickelony.Testing;

namespace Nickelony.KeyBindings.Tests;

/// <summary>
/// Pins the <see cref="KeyBindingServiceOptions.DisplayTextFormatter"/> seam: the default carries the canonical formatter, a <c>with</c> expression
/// replaces it on the options, and a <see langword="null"/> assignment is rejected.
/// </summary>
[TestClass]
public sealed class KeyBindingServiceOptionsTests
{
	[TestMethod]
	public void Default_UsesTheCanonicalFormatter() =>
		Assert.AreSame(KeyDisplayTextFormatter.Default, KeyBindingServiceOptions.Default.DisplayTextFormatter);

	[TestMethod]
	public void DisplayTextFormatter_NullAssignment_Throws() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => KeyBindingServiceOptions.Default with { DisplayTextFormatter = null! });

	[TestMethod]
	public void DisplayTextFormatter_WithExpression_ReplacesTheDefault()
	{
		var formatter = new StubFormatter();

		KeyBindingServiceOptions options = KeyBindingServiceOptions.Default with { DisplayTextFormatter = formatter };

		Assert.AreSame(formatter, options.DisplayTextFormatter);
		Assert.AreSame(KeyDisplayTextFormatter.Default, KeyBindingServiceOptions.Default.DisplayTextFormatter);
	}

	/// <summary>
	/// Pins the no-options path: a service constructed without options or a logger uses the canonical formatter, and the diagnostic
	/// for an uncataloged command is discarded, so such a host still renders canonical text and never throws on a reportable override.
	/// </summary>
	[TestMethod]
	public void ServiceWithoutOptions_UsesTheCanonicalFormatterAndDiscardsDiagnostics()
	{
		var overrides = new KeyBindingOverrides();

		overrides.Entries.Add(
			new KeyBindingOverrideEntry
			{
				SerializedId = "NotACommand",
				Bindings = [new KeyBindingOverrideChord { Strokes = [new KeyBindingOverrideStroke { KeyName = "Q", Modifiers = 0 }] }],
			}
		);

		var service = new KeyBindingService<TestCommand>(KeyBindingTestHelpers.CreateCatalog(), new RecordingOverrideStore(overrides));

		Assert.AreEqual("Control+S", service.GetDisplayText(TestCommand.Save));
	}

	/// <summary>A distinct formatter instance so the host override can be observed by reference.</summary>
	private sealed class StubFormatter : KeyDisplayTextFormatter
	{
		protected override string GetKeyText(KeyCode key) => $"<{key}>";
	}
}
