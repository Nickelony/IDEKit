using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Converts the kind of a watched file change to the kind that the protocol names.</summary>
internal static class ProtocolFileChangeConversion
{
	/// <summary>Converts a file change kind to its protocol counterpart.</summary>
	/// <param name="kind">The change kind of the watching engine.</param>
	/// <returns>The protocol kind with the same meaning.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined value.</exception>
	internal static FileChangeType ToProtocol(FileChangeKind kind)
	{
		return kind switch
		{
			FileChangeKind.Created => FileChangeType.Created,
			FileChangeKind.Changed => FileChangeType.Changed,
			FileChangeKind.Deleted => FileChangeType.Deleted,
			_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The change kind is not a defined value."),
		};
	}
}
