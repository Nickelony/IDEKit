using Nickelony.IDEKit.Core.Text;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

internal static class ReferenceResponseParser
{
	/// <summary>Parses reference locations from an LSP references response.</summary>
	/// <param name="response">The references response payload, or <see langword="null"/> when unavailable.</param>
	/// <returns>The resolved reference locations, or an empty list when none are available.</returns>
	/// <remarks>
	/// Entries with an unresolvable URI or an unusable range are dropped individually; the remaining references stay usable, so a malformed entry
	/// under-reports the reference list instead of failing it.
	/// </remarks>
	internal static IReadOnlyList<ReferenceLocation> Parse(IReadOnlyList<LocationPayload>? response)
	{
		if (response is not { Count: > 0 })
			return [];

		var locations = new List<ReferenceLocation>();

		for (int i = 0; i < response.Count; i++)
		{
			LocationPayload referenceLocation = response[i];

			if (
				!DocumentUriConversion.TryGetLocalPath(referenceLocation.Uri, out string filePath)
				|| !ProtocolRangeConversion.TryGetTextPositionRange(referenceLocation.Range, out TextPositionRange range)
			)
			{
				continue;
			}

			locations.Add(new ReferenceLocation(filePath, range));
		}

		return locations;
	}
}
