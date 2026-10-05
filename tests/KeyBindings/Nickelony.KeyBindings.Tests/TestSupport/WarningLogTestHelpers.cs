using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Nickelony.Testing;

namespace Nickelony.KeyBindings.Tests;

/// <summary>Asserts the warnings that the key binding service and its map builder log, by the names and values their messages carry.</summary>
internal static class WarningLogTestHelpers
{
	/// <summary>Asserts that the logger holds one warning with the event id, and that its formatted message carries every value.</summary>
	/// <param name="logger">The logger that recorded the entries.</param>
	/// <param name="eventId">The event id of the warning.</param>
	/// <param name="carriedValues">The names and values that the message prints, such as a command's serialized id or an invalid key name.</param>
	/// <remarks>
	/// The wording around the values is free to change, so a test pins what the warning tells the reader and not how it says it. A value must stand
	/// as a whole word in the message, so a one-letter key name is not found inside the words around it.
	/// </remarks>
	internal static void AssertSingleWarning(RecordingLogger logger, int eventId, params string[] carriedValues)
	{
		RecordingLogger.LogEntry entry = Assert.ContainsSingle(
			candidate => candidate.EventId.Id == eventId && candidate.Level == LogLevel.Warning,
			logger.Entries
		);

		foreach (string value in carriedValues)
		{
			string wholeWord = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(value)}(?![\p{{L}}\p{{N}}])";
			Assert.IsTrue(Regex.IsMatch(entry.Message, wholeWord), $"Warning {eventId} does not carry '{value}': {entry.Message}");
		}
	}
}
