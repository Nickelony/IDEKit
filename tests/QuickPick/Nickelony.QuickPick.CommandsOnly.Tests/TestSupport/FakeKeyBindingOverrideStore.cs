using Nickelony.KeyBindings;

namespace Nickelony.QuickPick.CommandsOnly.Tests;

/// <summary>An overrides store that starts empty and accepts every snapshot, for a host that persists no rebinding.</summary>
internal sealed class FakeKeyBindingOverrideStore : IKeyBindingOverrideStore
{
	public KeyBindingOverrides Load() => new();
	public bool Save(KeyBindingOverrides snapshot) => true;
}
