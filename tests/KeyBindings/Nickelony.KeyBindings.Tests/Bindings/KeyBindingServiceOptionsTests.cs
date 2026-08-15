using Nickelony.IDEKit.Testing;
using Nickelony.KeyBindings.Testing;

namespace Nickelony.KeyBindings.Tests;

/// <summary>
/// Pins the <see cref="KeyBindingServiceOptions.DisplayTextFormatter"/> seam at the option boundary: the
/// default carries the canonical formatter, a host override through a <c>with</c> expression replaces it for
/// the service, and a null assignment is rejected instead of reaching the render path. The service honoring
/// an overridden formatter is covered end to end by <c>GetDisplayText_UsesConfiguredFormatter</c>.
/// </summary>
[TestClass]
public sealed class KeyBindingServiceOptionsTests
{
	[TestMethod]
	public void Default_UsesTheCanonicalFormatter()
		=> Assert.AreSame(KeyDisplayTextFormatter.Default, KeyBindingServiceOptions.Default.DisplayTextFormatter);

	[TestMethod]
	public void DisplayTextFormatter_NullAssignment_Throws()
		=> Assert.ThrowsExactly<ArgumentNullException>(
			() => KeyBindingServiceOptions.Default with { DisplayTextFormatter = null! });

	[TestMethod]
	public void DisplayTextFormatter_WithExpression_ReplacesTheDefault()
	{
		var formatter = new StubFormatter();

		KeyBindingServiceOptions options = KeyBindingServiceOptions.Default with { DisplayTextFormatter = formatter };

		Assert.AreSame(formatter, options.DisplayTextFormatter);
		Assert.AreSame(KeyDisplayTextFormatter.Default, KeyBindingServiceOptions.Default.DisplayTextFormatter);
	}

	[TestMethod]
	public void Logger_DefaultsToNull()
		=> Assert.IsNull(KeyBindingServiceOptions.Default.Logger);

	/// <summary>
	/// Pins the <see cref="KeyBindingServiceOptions.Logger"/> seam end to end: an entry the service logs
	/// during construction reaches the logger supplied through the options.
	/// </summary>
	[TestMethod]
	public void Logger_PassedToTheService_ReceivesItsDiagnostics()
	{
		var logger = new CapturingLogger();
		KeyBindingOverrides overrides = KeyBindingTestFixtures.CreateOverrides(
			nameof(TestCommand.Exit), ("X", (int)KeyModifierSet.Control));

		KeyBindingService<TestCommand> service = new(
			KeyBindingTestFixtures.CreateCatalog(),
			new TestOverridesStore(overrides),
			KeyBindingServiceOptions.Default with { Logger = logger });

		// The host-reserved override was ignored with a diagnostic, which proves the logger from the
		// options expression reached the service.
		Assert.AreEqual(1, logger.Entries.Count(entry => entry.EventId.Id == 4000));
		Assert.AreEqual(new KeyCombo(KeyCode.F4, KeyModifierSet.Alt), service.GetBindings(TestCommand.Exit)[0]);
	}

	/// <summary>A distinct formatter instance so the host override can be observed by reference.</summary>
	private sealed class StubFormatter : KeyDisplayTextFormatter
	{
		protected override string GetKeyText(KeyCode key) => $"<{key}>";
	}
}
