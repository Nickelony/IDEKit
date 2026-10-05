using System;
using System.Collections.Generic;
using System.Linq;

namespace Nickelony.Repository.Tests;

/// <summary>
/// A list of entries a guard tolerates, each with a one-line reason. An entry that matches nothing is reported, so a list shrinks when the code
/// it excuses changes, and a new offender forces a decision instead of passing unnoticed.
/// </summary>
/// <remarks>
/// An entry that ends in <c>*</c> is a prefix entry and covers every offender whose key starts with the text before the star.
/// </remarks>
internal sealed class AllowList
{
	private readonly Dictionary<string, string> _reasons = new(StringComparer.Ordinal);
	private readonly HashSet<string> _matched = new(StringComparer.Ordinal);
	private readonly List<string> _prefixes = [];

	internal AllowList(string name, params (string Entry, string Reason)[] entries)
	{
		Name = name;

		foreach ((string entry, string reason) in entries)
		{
			if (string.IsNullOrWhiteSpace(reason) || reason.Contains('\n', StringComparison.Ordinal))
				throw new ArgumentException($"The entry '{entry}' of the list '{name}' needs a one-line reason.", nameof(entries));

			if (!_reasons.TryAdd(entry, reason))
				throw new ArgumentException($"The list '{name}' holds the entry '{entry}' twice.", nameof(entries));

			if (entry.EndsWith('*'))
				_prefixes.Add(entry);
		}
	}

	internal string Name { get; }

	/// <summary>The entries of the list, for a check that matches an entry by a rule other than equality.</summary>
	internal IEnumerable<string> Entries => _reasons.Keys;

	/// <summary>The reason of each entry, for a check of the reasons themselves.</summary>
	internal IEnumerable<KeyValuePair<string, string>> Reasons => _reasons;

	/// <summary>
	/// Reports whether the list holds the entry or a prefix entry that covers it, and records the match so the entry is not reported as unused.
	/// </summary>
	internal bool Allows(string entry)
	{
		if (_reasons.ContainsKey(entry))
		{
			_matched.Add(entry);
			return true;
		}

		string? prefix = _prefixes.FirstOrDefault(candidate => entry.StartsWith(candidate[..^1], StringComparison.Ordinal));

		if (prefix is null)
			return false;

		_matched.Add(prefix);
		return true;
	}

	/// <summary>The entries that no check matched.</summary>
	internal IEnumerable<string> Unmatched() =>
		_reasons.Keys.Where(entry => !_matched.Contains(entry)).OrderBy(entry => entry, StringComparer.Ordinal);

	/// <summary>Writes the message that tells an author how to resolve an offender the list does not hold.</summary>
	internal string Rejection(string subject, string guard) =>
		$"{subject} Resolve it in the code, or add it to the '{Name}' list of {guard} with a one-line reason.";

	/// <summary>Writes one message for each entry that matched nothing.</summary>
	internal IEnumerable<string> UnmatchedMessages(string guard) =>
		Unmatched().Select(entry => $"The '{Name}' list of {guard} holds '{entry}', which matches nothing. Delete the entry.");
}
