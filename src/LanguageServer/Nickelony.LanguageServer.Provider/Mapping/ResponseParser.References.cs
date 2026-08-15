using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Provider;

internal static partial class ResponseParser
{
	/// <summary>
	/// Parses reference locations from an LSP references response.
	/// </summary>
	/// <remarks>
	/// Entries with an unresolvable URI or an unusable range are dropped individually; the remaining
	/// references stay usable, so a malformed entry under-reports the reference list instead of failing it.
	/// </remarks>
	/// <param name="response">The references response payload, or <see langword="null"/> when unavailable.</param>
	/// <returns>The resolved reference locations, or an empty list when none are available.</returns>
	internal static IReadOnlyList<TextReferenceLocation> ParseReferenceLocations(IReadOnlyList<ReferenceLocationPayload>? response)
	{
		if (response is not { Count: > 0 })
			return [];

		var locations = new List<TextReferenceLocation>();

		for (int i = 0; i < response.Count; i++)
		{
			ReferenceLocationPayload referenceLocation = response[i];

			if (!LanguageServerPaths.TryGetLocalPath(referenceLocation.Uri, out string filePath)
				|| !ProtocolRangeConversion.TryGetTextPositionRange(referenceLocation.Range, out TextPositionRange range))
			{
				continue;
			}

			locations.Add(new TextReferenceLocation(filePath, range));
		}

		return locations;
	}
}
