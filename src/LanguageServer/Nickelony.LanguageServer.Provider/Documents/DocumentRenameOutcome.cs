namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Describes the outcome of a tracked-document path rekey.
/// </summary>
public enum DocumentRenameOutcome
{
	/// <summary>
	/// Both paths identify the same file on the current host, so nothing was rekeyed and no request mirrors the call.
	/// </summary>
	PathsEquivalent = 0,

	/// <summary>
	/// The source path was not tracked, so nothing was rekeyed and no request mirrors the call.
	/// </summary>
	SourceUntracked = 1,

	/// <summary>
	/// The destination path was already tracked, so nothing was rekeyed and no request mirrors the call.
	/// </summary>
	DestinationTracked = 2,

	/// <summary>
	/// The record was rekeyed; the carried request holds the close/open pair that mirrors the move to the server.
	/// </summary>
	Renamed = 3
}
