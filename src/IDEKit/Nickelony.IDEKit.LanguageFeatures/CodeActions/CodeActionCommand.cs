namespace Nickelony.IDEKit.LanguageFeatures.CodeActions;

/// <summary>Names a command that the provider which offered a code action runs once the action's edit is applied.</summary>
/// <remarks>
/// The library never inspects <see cref="Arguments"/>; they belong to the provider that offered the action and come back to it unchanged.
/// The constructor copies the arguments into a read-only snapshot, and record equality compares the title, the name and the arguments element-wise
/// with <see cref="object.Equals(object?, object?)"/>.
/// </remarks>
public sealed record CodeActionCommand
{
	/// <param name="title">The human-readable command title.</param>
	/// <param name="name">The identifier of the command, as the provider that offered the action knows it.</param>
	/// <param name="arguments">
	/// The opaque arguments in call order, or <see langword="null"/> when the command takes none; an entry may be <see langword="null"/>.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="title"/> or <paramref name="name"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="title"/> or <paramref name="name"/> is blank.</exception>
	public CodeActionCommand(string title, string name, IReadOnlyList<object?>? arguments = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(title);
		ArgumentException.ThrowIfNullOrWhiteSpace(name);

		Title = title;
		Name = name;
		Arguments = arguments is { Count: > 0 } ? Array.AsReadOnly([.. arguments]) : CollectionSnapshots.Empty<object?>();
	}

	/// <summary>The human-readable command title.</summary>
	public string Title { get; }

	/// <summary>The identifier of the command.</summary>
	public string Name { get; }

	/// <summary>The opaque arguments in call order, or an empty list when the command takes none.</summary>
	public IReadOnlyList<object?> Arguments { get; }

	/// <summary>Determines whether the supplied command has the same title, name and arguments.</summary>
	/// <param name="other">The command to compare with.</param>
	/// <returns><see langword="true"/> when the values are equal; otherwise, <see langword="false"/>.</returns>
	public bool Equals(CodeActionCommand? other)
	{
		if (other is null)
			return false;

		if (ReferenceEquals(this, other))
			return true;

		if (!string.Equals(Title, other.Title, StringComparison.Ordinal) || !string.Equals(Name, other.Name, StringComparison.Ordinal))
			return false;

		return Arguments.Count == other.Arguments.Count && Arguments.SequenceEqual(other.Arguments);
	}

	/// <summary>Serves as the hash function for <see cref="CodeActionCommand"/>.</summary>
	/// <returns>A hash code over the title, the name and the arguments.</returns>
	public override int GetHashCode()
	{
		var hashCode = new HashCode();

		hashCode.Add(Title, StringComparer.Ordinal);
		hashCode.Add(Name, StringComparer.Ordinal);

		for (int i = 0; i < Arguments.Count; i++)
			hashCode.Add(Arguments[i]);

		return hashCode.ToHashCode();
	}
}
