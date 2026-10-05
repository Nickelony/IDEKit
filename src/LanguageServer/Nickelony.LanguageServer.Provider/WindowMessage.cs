namespace Nickelony.LanguageServer.Provider;

/// <summary>A message that the language server asks the host to show to the user, with the actions the user can answer it with.</summary>
/// <remarks>
/// The constructor copies the actions into a read-only snapshot. Record equality compares the kind and the text, and the actions by reference.
/// </remarks>
public sealed record WindowMessage
{
	/// <param name="kind">How severe the message is.</param>
	/// <param name="text">The message text.</param>
	/// <param name="actions">The titles of the actions the user can choose from, or <see langword="null"/> when the message offers none.</param>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="text"/> is blank, or <paramref name="actions"/> contains a blank title.</exception>
	public WindowMessage(WindowMessageKind kind, string text, IReadOnlyList<string>? actions = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(text);

		if (actions is not null && actions.Any(string.IsNullOrWhiteSpace))
			throw new ArgumentException("The actions contain a null or blank title.", nameof(actions));

		Kind = kind;
		Text = text;
		Actions = actions is { Count: > 0 } ? Array.AsReadOnly([.. actions]) : Array.AsReadOnly<string>([]);
	}

	/// <summary>How severe the message is.</summary>
	public WindowMessageKind Kind { get; }

	/// <summary>The message text.</summary>
	public string Text { get; }

	/// <summary>The titles of the actions the user can choose from, or an empty list when the message offers none.</summary>
	public IReadOnlyList<string> Actions { get; }
}

/// <summary>Classifies how severe a <see cref="WindowMessage"/> is.</summary>
/// <remarks>
/// The members follow the language server protocol's message types. A host that switches over the kind keeps a default branch, because a server
/// message of an unknown type reads as <see cref="Log"/>.
/// </remarks>
public enum WindowMessageKind
{
	/// <summary>The message reports an error.</summary>
	Error,

	/// <summary>The message reports a warning.</summary>
	Warning,

	/// <summary>The message informs the user.</summary>
	Information,

	/// <summary>The message is a log line, the least intrusive kind.</summary>
	Log,
}
