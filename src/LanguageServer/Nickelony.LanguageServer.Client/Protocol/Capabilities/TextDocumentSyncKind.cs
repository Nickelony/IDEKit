namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Describes the text-document synchronization mode negotiated with the language server.
/// </summary>
/// <remarks>
/// The numeric values match the protocol values and are serialized as numbers. A value outside the
/// defined range stays representable as an unnamed enum value.
/// </remarks>
public enum TextDocumentSyncKind
{
	/// <summary>
	/// No document synchronization is supported.
	/// </summary>
	None = 0,

	/// <summary>
	/// Each change sends the full document content.
	/// </summary>
	Full = 1,

	/// <summary>
	/// Each change sends an incremental range edit.
	/// </summary>
	Incremental = 2
}
