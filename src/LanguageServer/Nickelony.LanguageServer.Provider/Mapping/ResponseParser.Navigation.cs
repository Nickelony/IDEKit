using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Navigation;

namespace Nickelony.LanguageServer.Provider;

internal static partial class ResponseParser
{
	/// <summary>
	/// Parses a definition location from an LSP definition response.
	/// </summary>
	/// <param name="response">The definition response payload, or <see langword="null"/> when unavailable.</param>
	/// <returns>
	/// The resolved definition location, or <see langword="null"/> when the response does not contain a usable
	/// local file URI or the target carries no usable range.
	/// </returns>
	/// <remarks>
	/// A response that offers several targets resolves to its first usable target: the shared definition model
	/// carries a single location, so any further targets are not represented. A target that does not resolve to a
	/// local path or whose range cannot be converted is skipped rather than ending the search, so a non-local
	/// library target ahead of a valid local one no longer suppresses the local result.
	/// </remarks>
	internal static TextDefinitionLocation? ParseDefinitionLocation(DefinitionResponse? response)
	{
		if (response is null)
			return null;

		IReadOnlyList<DefinitionTargetPayload> targets = response.Targets;

		for (int i = 0; i < targets.Count; i++)
		{
			DefinitionTargetPayload target = targets[i];

			if (!LanguageServerPaths.TryGetLocalPath(target.Uri, out string filePath)
				|| !ProtocolRangeConversion.TryGetTextPositionRange(target.TargetRange, out TextPositionRange targetRange))
			{
				continue;
			}

			TextPositionRange? selectionRange = ProtocolRangeConversion.TryGetTextPositionRange(target.SelectionRange, out TextPositionRange selection)
				? selection
				: null;

			return new(targetRange, filePath, selectionRange);
		}

		return null;
	}
}
