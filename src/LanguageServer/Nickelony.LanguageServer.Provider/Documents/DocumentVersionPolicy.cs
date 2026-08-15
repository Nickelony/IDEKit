namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Defines the version-acceptance rules shared by the document payload caches and the parse-to-store fences.
/// </summary>
/// <remarks>
/// The rules are protocol-neutral: they only compare a tracked document version with the version a payload was
/// produced for, so any language package can rely on one policy instead of re-deriving its own. This is
/// framework-internal machinery, so the type is <see langword="internal"/>.
/// </remarks>
internal static class DocumentVersionPolicy
{
	/// <summary>
	/// Determines whether a payload produced for <paramref name="payloadVersion"/> may be applied to a
	/// document tracked at <paramref name="documentVersion"/>.
	/// </summary>
	/// <param name="documentVersion">The tracked document version, which is always positive.</param>
	/// <param name="payloadVersion">The version the payload was produced for, or <c>0</c> when unknown.</param>
	/// <returns>
	/// <see langword="true"/> when the payload version is unknown (<c>0</c>) or matches the tracked document version;
	/// otherwise, <see langword="false"/>.
	/// </returns>
	public static bool IsPayloadCurrent(int documentVersion, int payloadVersion)
		=> payloadVersion == 0 || payloadVersion == documentVersion;

	/// <summary>
	/// Tries to accept an incoming document version relative to the current cached version.
	/// </summary>
	/// <param name="currentVersion">The version currently cached.</param>
	/// <param name="incomingVersion">The incoming version to evaluate.</param>
	/// <param name="acceptedVersion">The version that should remain cached after evaluation.</param>
	/// <returns><see langword="true"/> when the incoming version is acceptable; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// Version <c>0</c> means that the producer did not provide a comparable version and therefore does not advance the
	/// cached version; a negative incoming version is treated like an unknown one. A positive incoming version equal
	/// to or newer than the positive cached version is accepted.
	/// </remarks>
	public static bool TryAccept(int currentVersion, int incomingVersion, out int acceptedVersion)
	{
		acceptedVersion = currentVersion;

		if (incomingVersion > 0 && currentVersion > 0 && incomingVersion < currentVersion)
			return false;

		if (incomingVersion > 0)
			acceptedVersion = incomingVersion;

		return true;
	}
}
