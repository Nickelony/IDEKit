using Nickelony.KeyBindings.Testing;
using static Nickelony.KeyBindings.Testing.KeyBindingTestFixtures;

namespace Nickelony.KeyBindings.Tests;

/// <summary>
/// Covers the reset persistence channel and policy-free reset behaviour: a throwing store propagates and
/// leaves the runtime state untouched, and a reset removes a stored entry even for a host-reserved command.
/// </summary>
public partial class KeyBindingServiceTests
{
	/// <summary>
	/// Pins that <c>Reset</c> shares the documented persist-before-publish channel: an exception from the
	/// overrides store is the host's to handle, and the runtime state is left untouched.
	/// </summary>
	[TestMethod]
	public void Reset_StoreThrows_PropagatesAndKeepsState()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.Save), ("X", (int)KeyModifierSet.Control));
		int fired = 0;
		KeyBindingService<TestCommand> service = CreateService(overrides, _ => throw new InvalidOperationException("The store failed."));
		service.BindingsChanged += (_, _) => fired++;

		Assert.ThrowsExactly<InvalidOperationException>(() => service.Reset(TestCommand.Save));

		Assert.AreEqual(0, fired);
		Assert.AreEqual(new KeyCombo(KeyCode.X, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);
	}

	/// <summary>
	/// Pins that <c>ResetAll</c> shares the same channel as <c>Reset</c>.
	/// </summary>
	[TestMethod]
	public void ResetAll_StoreThrows_PropagatesAndKeepsState()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.Save), ("X", (int)KeyModifierSet.Control));
		int fired = 0;
		KeyBindingService<TestCommand> service = CreateService(overrides, _ => throw new InvalidOperationException("The store failed."));
		service.BindingsChanged += (_, _) => fired++;

		Assert.ThrowsExactly<InvalidOperationException>(() => service.ResetAll());

		Assert.AreEqual(0, fired);
		Assert.AreEqual(new KeyCombo(KeyCode.X, KeyModifierSet.Control), service.GetBindings(TestCommand.Save)[0]);
	}

	/// <summary>
	/// Pins that the reset path is not policy-checked: resetting a host-reserved command removes its stored
	/// override entry and persists once, while the runtime bindings were the catalog defaults all along
	/// because the host-reserved entry was ignored when it was loaded.
	/// </summary>
	[TestMethod]
	public void Reset_HostReservedCommand_RemovesTheStoredEntryAndPersists()
	{
		KeyBindingOverrides overrides = CreateOverrides(nameof(TestCommand.Exit), ("X", (int)KeyModifierSet.Control));
		KeyBindingOverrides? saved = null;
		int fired = 0;
		KeyBindingService<TestCommand> service = CreateService(overrides, snapshot => { saved = snapshot; return true; });
		service.BindingsChanged += (_, _) => fired++;

		// The ignored entry never changed the runtime bindings.
		Assert.AreEqual(new KeyCombo(KeyCode.F4, KeyModifierSet.Alt), service.GetBindings(TestCommand.Exit)[0]);

		KeyBindingOutcome outcome = service.Reset(TestCommand.Exit);

		Assert.AreEqual(KeyBindingOutcome.Succeeded, outcome);
		Assert.AreEqual(1, fired);
		Assert.IsNotNull(saved);
		Assert.IsEmpty(saved.Entries);
		Assert.AreEqual(new KeyCombo(KeyCode.F4, KeyModifierSet.Alt), service.GetBindings(TestCommand.Exit)[0]);
	}
}
