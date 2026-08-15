using Microsoft.Extensions.Logging;
using System.Globalization;

namespace Nickelony.KeyBindings;

/// <summary>
/// Reads and writes the persisted override entries: it parses an entry into the chords it declares and
/// writes a chord back into the stored stroke shape.
/// </summary>
/// <remarks>
/// The codec is the single home of the strict persisted grammar. It accepts only exact canonical
/// <see cref="KeyCode"/> member names and never a numeric or alias form, so tolerating typed input in
/// <c>KeyChord.TryParse</c> does not loosen what a stored document may contain. A stroke that cannot be
/// parsed is skipped with a diagnostic, and a binding with a skipped stroke is skipped as a whole,
/// because a chord with a missing stroke is not the declared chord.
/// </remarks>
internal sealed partial class KeyBindingOverrideCodec
{
	private const int DefinedModifierMask = (int)KeyModifierMasks.Defined;

	// Key-binding override-codec diagnostics occupy the package log event id block 4002-4004.
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

	/// <summary>
	/// Initializes a new instance of the <see cref="KeyBindingOverrideCodec"/> class.
	/// </summary>
	/// <param name="logger">The logger for override diagnostics.</param>
	/// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
	internal KeyBindingOverrideCodec(ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(logger);

		_logger = logger;
	}

	/// <summary>
	/// Creates a persisted override entry for a command's bindings.
	/// </summary>
	/// <param name="serializedId">The serialized identifier of the command.</param>
	/// <param name="bindings">The bindings to store.</param>
	/// <returns>The override entry.</returns>
	internal static KeyBindingOverrideEntry CreateEntry(string serializedId, IEnumerable<KeyChord> bindings)
	{
		var entry = new KeyBindingOverrideEntry { SerializedId = serializedId };

		foreach (KeyChord binding in bindings)
			entry.Bindings.Add(CreateBinding(binding));

		return entry;
	}

	/// <summary>
	/// Creates a persisted binding for a chord.
	/// </summary>
	/// <param name="chord">The chord to store.</param>
	/// <returns>The override binding.</returns>
	internal static KeyBindingOverrideBinding CreateBinding(KeyChord chord)
	{
		var binding = new KeyBindingOverrideBinding();

		foreach (KeyCombo stroke in chord.Strokes)
		{
			binding.Strokes.Add(new KeyBindingOverrideStroke
			{
				KeyName = stroke.Key.ToString(),
				Modifiers = (int)stroke.Modifiers
			});
		}

		return binding;
	}

	/// <summary>
	/// Parses an override entry into the chords that dispatch to its command, skipping binding entries
	/// that are empty, repeat a chord, or contain a stroke that cannot be parsed.
	/// </summary>
	/// <param name="entry">The entry to parse.</param>
	/// <param name="serializedId">The command the entry belongs to, for diagnostics.</param>
	/// <param name="logDiagnostics">
	/// <see langword="true"/> to log an offending stroke; <see langword="false"/> to parse silently.
	/// </param>
	/// <returns>The chords the entry declares, in stored order.</returns>
	internal List<KeyChord> ParseEntryBindings(KeyBindingOverrideEntry entry, string serializedId, bool logDiagnostics)
	{
		var parsed = new List<KeyChord>();
		var seen = new HashSet<KeyChord>();

		foreach (KeyBindingOverrideBinding binding in entry.Bindings)
		{
			KeyChord? chord = ParseBinding(binding, serializedId, logDiagnostics);

			if (chord is not null && seen.Add(chord.Value))
				parsed.Add(chord.Value);
		}

		return parsed;
	}

	/// <summary>
	/// Parses a binding entry into the chord it declares, or <see langword="null"/> when the entry does
	/// not declare a valid chord. An empty stroke list has no meaning, and a binding with a stroke that
	/// cannot be parsed is skipped as a whole, because a chord with a missing stroke is not the declared
	/// chord. A repeated stroke is skipped for the same reason.
	/// </summary>
	/// <param name="binding">The binding entry to parse.</param>
	/// <param name="serializedId">The command the entry belongs to, for diagnostics.</param>
	/// <param name="logDiagnostics">
	/// <see langword="true"/> to log the offending stroke; <see langword="false"/> to parse silently.
	/// </param>
	internal KeyChord? ParseBinding(KeyBindingOverrideBinding binding, string serializedId, bool logDiagnostics)
	{
		if (binding.Strokes.Count == 0)
			return null;

		var strokes = new List<KeyCombo>(binding.Strokes.Count);

		foreach (KeyBindingOverrideStroke stroke in binding.Strokes)
		{
			KeyCombo? parsed = ParseStroke(stroke, serializedId, logDiagnostics);

			if (parsed is null || strokes.Contains(parsed.Value))
				return null;

			strokes.Add(parsed.Value);
		}

		return new KeyChord(strokes);
	}

	/// <summary>
	/// Parses one stroke of a binding entry, or returns <see langword="null"/> when it is not a valid
	/// canonical key and modifier combination.
	/// </summary>
	/// <param name="stroke">The stroke to parse.</param>
	/// <param name="serializedId">The command the entry belongs to, for diagnostics.</param>
	/// <param name="logDiagnostics">
	/// <see langword="true"/> to log the offending value; <see langword="false"/> to parse silently.
	/// </param>
	private KeyCombo? ParseStroke(KeyBindingOverrideStroke stroke, string serializedId, bool logDiagnostics)
	{
		if (string.IsNullOrEmpty(stroke.KeyName))
		{
			if (logDiagnostics)
				LogEmptyKeyName(_logger, serializedId);

			return null;
		}

		// Only exact KeyCode member names are accepted; Enum.TryParse alone would also accept numeric
		// strings such as "62" and the alias names other frameworks use for the same value.
		if (!Enum.TryParse(stroke.KeyName, out KeyCode key) ||
			!Enum.IsDefined<KeyCode>(key) ||
			!string.Equals(Enum.GetName(key), stroke.KeyName, StringComparison.Ordinal))
		{
			if (logDiagnostics)
				LogInvalidKeyName(_logger, serializedId, stroke.KeyName);

			return null;
		}

		if ((stroke.Modifiers & ~DefinedModifierMask) != 0)
		{
			if (logDiagnostics)
				LogUnknownModifierBits(_logger, serializedId, stroke.Modifiers.ToString(CultureInfo.InvariantCulture));

			return null;
		}

		return new KeyCombo(key, (KeyModifierSet)stroke.Modifiers);
	}
}
