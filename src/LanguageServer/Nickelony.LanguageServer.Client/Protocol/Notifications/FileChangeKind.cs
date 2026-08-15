namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Identifies the file-system change kind reported to the language server.
/// </summary>
/// <remarks>
/// The numeric values match the protocol's <c>FileChangeType</c> values and are serialized as numbers.
/// A value outside the defined range stays representable as an unnamed enum value.
/// </remarks>
public enum FileChangeKind
{
	/// <summary>
	/// A file or directory was created.
	/// </summary>
	Created = 1,

	/// <summary>
	/// A file or directory changed in place.
	/// </summary>
	Changed = 2,

	/// <summary>
	/// A file or directory was deleted.
	/// </summary>
	Deleted = 3
}
