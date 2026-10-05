using Nickelony.KeyBindings.OverrideEntries;

namespace Nickelony.KeyBindings;

/// <summary>Checks that the overrides a store returned have no unset list, entry, identifier, binding or stroke.</summary>
internal static class KeyBindingOverrideValidation
{
	/// <summary>Throws when the overrides contain a <see langword="null"/> list, entry, serialized identifier, binding or stroke.</summary>
	/// <param name="overrides">The overrides to check.</param>
	/// <param name="parameterName">The name of the parameter that supplied the overrides.</param>
	/// <exception cref="ArgumentException">A list, entry, serialized identifier, binding or stroke is <see langword="null"/>.</exception>
	internal static void ThrowIfMalformed(KeyBindingOverrides overrides, string parameterName)
	{
		if (overrides.Entries is null)
			throw new ArgumentException("The loaded overrides must have a non-null entry list.", parameterName);

		foreach (KeyBindingOverrideEntry entry in overrides.Entries)
		{
			if (entry is null)
				throw new ArgumentException("The loaded overrides must not contain null entries.", parameterName);

			if (entry.SerializedId is null)
				throw new ArgumentException("The loaded overrides must not contain an entry with a null serialized identifier.", parameterName);

			if (entry.Bindings is null)
				throw new ArgumentException($"Override entry '{entry.SerializedId}' must have a non-null binding list.", parameterName);

			foreach (KeyBindingOverrideChord binding in entry.Bindings)
			{
				if (binding is null)
					throw new ArgumentException($"Override entry '{entry.SerializedId}' must not contain null binding entries.", parameterName);

				if (binding.Strokes is null)
				{
					throw new ArgumentException(
						$"A binding of override entry '{entry.SerializedId}' must have a non-null stroke list.",
						parameterName
					);
				}

				foreach (KeyBindingOverrideStroke stroke in binding.Strokes)
				{
					if (stroke is null)
					{
						throw new ArgumentException(
							$"A binding of override entry '{entry.SerializedId}' must not contain null strokes.",
							parameterName
						);
					}
				}
			}
		}
	}
}
