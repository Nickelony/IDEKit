using System.Globalization;
using Microsoft.Extensions.Logging;
using Nickelony.KeyBindings.OverrideEntries;

namespace Nickelony.KeyBindings;

/// <summary>Converts a persisted override entry to the chords it declares and a chord back to the stored stroke shape.</summary>
/// <remarks>
/// The codec is the single home of the strict persisted grammar: it accepts only exact canonical <see cref="KeyCode"/> member names, never a numeric
/// or alias form, so tolerating typed input in <c>KeyChord.TryParse</c> does not loosen a stored document. A stroke that cannot be parsed is skipped,
/// and a diagnostic is logged when the caller asks for one. A binding with a skipped stroke is skipped whole, because a chord with a missing stroke
/// is not the declared chord.
/// </remarks>
internal sealed partial class KeyBindingOverrideCodec
{
	[LoggerMessage(
		EventId = 4002,
		EventName = "EmptyKeyName",
		Level = LogLevel.Warning,
		Message = "Key binding override for '{SerializedId}' has an empty key name. Skipping."
	)]
	private static partial void LogEmptyKeyName(ILogger logger, string serializedId);

	[LoggerMessage(
		EventId = 4003,
		EventName = "InvalidKeyName",
		Level = LogLevel.Warning,
		Message = "Key binding override for '{SerializedId}' has invalid key name '{KeyName}'. Skipping."
	)]
	private static partial void LogInvalidKeyName(ILogger logger, string serializedId, string keyName);

	[LoggerMessage(
		EventId = 4004,
		EventName = "UnknownModifierBits",
		Level = LogLevel.Warning,
		Message = "Key binding override for '{SerializedId}' has unknown modifier bits '{Modifiers}'. Skipping."
	)]
	private static partial void LogUnknownModifierBits(ILogger logger, string serializedId, string modifiers);

	private readonly ILogger _logger;

	/// <param name="logger">The logger for override diagnostics.</param>
	internal KeyBindingOverrideCodec(ILogger logger) => _logger = logger;

	internal static KeyBindingOverrideEntry CreateEntry(string serializedId, IEnumerable<KeyChord> bindings)
	{
		var entry = new KeyBindingOverrideEntry { SerializedId = serializedId };

		foreach (KeyChord binding in bindings)
			entry.Bindings.Add(CreateBinding(binding));

		return entry;
	}

	internal static KeyBindingOverrideChord CreateBinding(KeyChord chord)
	{
		var binding = new KeyBindingOverrideChord();

		foreach (KeyStroke stroke in chord.Strokes)
			binding.Strokes.Add(new KeyBindingOverrideStroke { KeyName = stroke.Key.ToString(), Modifiers = (int)stroke.Modifiers });

		return binding;
	}

	/// <summary>
	/// Parses an override entry into the chords that dispatch to its command, skipping binding entries that are empty, repeat a chord,
	/// or contain a stroke that cannot be parsed.
	/// </summary>
	/// <param name="entry">The entry to parse.</param>
	/// <param name="serializedId">The command the entry belongs to, for diagnostics.</param>
	/// <param name="logDiagnostics"><see langword="true"/> to log an offending stroke; <see langword="false"/> to parse silently.</param>
	/// <returns>The chords the entry declares, in stored order.</returns>
	internal List<KeyChord> ParseEntryBindings(KeyBindingOverrideEntry entry, string serializedId, bool logDiagnostics)
	{
		var parsed = new List<KeyChord>();
		var seen = new HashSet<KeyChord>();

		foreach (KeyBindingOverrideChord binding in entry.Bindings)
		{
			KeyChord? chord = ParseBinding(binding, serializedId, logDiagnostics);

			if (chord is not null && seen.Add(chord.Value))
				parsed.Add(chord.Value);
		}

		return parsed;
	}

	/// <summary>Parses a binding entry into the chord it declares, or <see langword="null"/> when the entry does not declare a valid chord.</summary>
	/// <param name="binding">The binding entry to parse.</param>
	/// <param name="serializedId">The command the entry belongs to, for diagnostics.</param>
	/// <param name="logDiagnostics"><see langword="true"/> to log the offending stroke; <see langword="false"/> to parse silently.</param>
	internal KeyChord? ParseBinding(KeyBindingOverrideChord binding, string serializedId, bool logDiagnostics)
	{
		if (binding.Strokes.Count == 0)
			return null;

		var strokes = new List<KeyStroke>(binding.Strokes.Count);

		foreach (KeyBindingOverrideStroke stroke in binding.Strokes)
		{
			KeyStroke? parsed = ParseStroke(stroke, serializedId, logDiagnostics);

			if (parsed is null || strokes.Contains(parsed.Value))
				return null;

			strokes.Add(parsed.Value);
		}

		return new KeyChord([.. strokes]);
	}

	/// <summary>
	/// Parses one stroke of a binding entry, or returns <see langword="null"/> when it is not a valid canonical key and modifier combination.
	/// </summary>
	/// <param name="stroke">The stroke to parse.</param>
	/// <param name="serializedId">The command the entry belongs to, for diagnostics.</param>
	/// <param name="logDiagnostics"><see langword="true"/> to log the offending value; <see langword="false"/> to parse silently.</param>
	private KeyStroke? ParseStroke(KeyBindingOverrideStroke stroke, string serializedId, bool logDiagnostics)
	{
		if (string.IsNullOrEmpty(stroke.KeyName))
		{
			if (logDiagnostics)
				LogEmptyKeyName(_logger, serializedId);

			return null;
		}

		// Only exact KeyCode member names are accepted; Enum.TryParse alone would also accept a numeric string such as "62".
		if (
			!Enum.TryParse(stroke.KeyName, out KeyCode key)
			|| !Enum.IsDefined<KeyCode>(key)
			|| !string.Equals(Enum.GetName(key), stroke.KeyName, StringComparison.Ordinal)
		)
		{
			if (logDiagnostics)
				LogInvalidKeyName(_logger, serializedId, stroke.KeyName);

			return null;
		}

		if (((KeyModifiers)stroke.Modifiers).HasUndefinedFlags())
		{
			if (logDiagnostics)
				LogUnknownModifierBits(_logger, serializedId, stroke.Modifiers.ToString(CultureInfo.InvariantCulture));

			return null;
		}

		return new KeyStroke(key, (KeyModifiers)stroke.Modifiers);
	}
}
