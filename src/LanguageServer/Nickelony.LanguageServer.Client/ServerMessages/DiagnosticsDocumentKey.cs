using Nickelony.Infrastructure;

namespace Nickelony.LanguageServer.Client;

/// <summary>Identifies one document for diagnostics coalescing together with the identity rule that applies to it.</summary>
/// <param name="IsLocalPath"><see langword="true"/> when <paramref name="Value"/> is a normalized local path rather than an opaque URI.</param>
/// <param name="Value">The normalized local path for a file URI, or the raw URI text otherwise.</param>
/// <remarks>
/// The identity and its comparer are shared by the pending-diagnostics coalescing of the server-message dispatcher,
/// the diagnostics coalescer and each subscriber's pending-payload dictionary, so they coalesce the same logical document
/// the same way. Carrying the key itself, rather than only its <see cref="Value"/>, lets each subscriber apply the same identity as the dispatcher
/// and the coalescer, so two case variants of one path on a case-insensitive host cannot occupy two subscriber slots.
/// </remarks>
internal readonly record struct DiagnosticsDocumentKey(bool IsLocalPath, string Value)
{
	/// <summary>
	/// The comparer that applies the identity rule matching the key kind: the platform's local-path identity for a normalized local path,
	/// and ordinal comparison for any other URI.
	/// </summary>
	/// <remarks>
	/// A non-file URI names an opaque document, so case-only differences are significant there even on a host whose file system is case-insensitive;
	/// applying the local-path identity to it would merge two distinct documents.
	/// </remarks>
	internal static IEqualityComparer<DiagnosticsDocumentKey> Comparer { get; } = new DiagnosticsDocumentKeyComparer();

	/// <summary>
	/// Builds the identity for one payload URI: a local file URI collapses to its normalized local path and keeps the platform's local-path identity,
	/// while any other URI keeps its raw text and compares ordinally.
	/// </summary>
	/// <param name="uri">The payload URI.</param>
	/// <returns>The document identity together with the rule that applies to it.</returns>
	/// <remarks>
	/// Identity comparison happens in <see cref="Comparer"/>, so the key itself stays free of case-folding work.
	/// </remarks>
	internal static DiagnosticsDocumentKey FromUri(string uri) =>
		DocumentUriConversion.TryGetLocalPath(uri, out string filePath) ? new(true, filePath) : new(false, uri);

	private sealed class DiagnosticsDocumentKeyComparer : IEqualityComparer<DiagnosticsDocumentKey>
	{
		/// <inheritdoc/>
		public bool Equals(DiagnosticsDocumentKey left, DiagnosticsDocumentKey right)
		{
			return left.IsLocalPath == right.IsLocalPath
				&& (
					left.IsLocalPath
						? LocalPathRules.DefaultComparer.Equals(left.Value, right.Value)
						: string.Equals(left.Value, right.Value, StringComparison.Ordinal)
				);
		}

		/// <inheritdoc/>
		public int GetHashCode(DiagnosticsDocumentKey key)
		{
			return HashCode.Combine(
				key.IsLocalPath,
				key.IsLocalPath ? LocalPathRules.DefaultComparer.GetHashCode(key.Value) : StringComparer.Ordinal.GetHashCode(key.Value)
			);
		}
	}
}
