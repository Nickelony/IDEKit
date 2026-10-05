namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Identifies the file-system change type reported to the language server.</summary>
/// <remarks>
/// The numeric values match the protocol's <c>FileChangeType</c> values and are serialized as numbers. A value outside the defined range stays
/// representable as an unnamed enum value.
/// </remarks>
public enum FileChangeType
{
	Created = 1,

	Changed = 2,

	Deleted = 3,
}
