namespace Nickelony.LanguageServer.Provider;

/// <summary>Describes the outcome of a tracked-document path rename.</summary>
internal enum DocumentRenameOutcome
{
	/// <summary>The paths identify the same file on the current host, so no path changed and no request mirrors the call.</summary>
	PathsEquivalent = 0,

	/// <summary>The source path was not tracked, so no path changed and no request mirrors the call.</summary>
	SourceUntracked = 1,

	/// <summary>The destination path was already tracked, so no path changed and no request mirrors the call.</summary>
	DestinationTracked = 2,

	/// <summary>The rename landed; the carried request mirrors it to the server as a close/open pair when the server copy was open.</summary>
	Renamed = 3,
}
