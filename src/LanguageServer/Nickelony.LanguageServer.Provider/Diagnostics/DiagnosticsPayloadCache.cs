using System.Collections.ObjectModel;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Caches the diagnostics a server published for one tracked document, with the shared-diagnostic projection of the cached entries.
/// </summary>
internal sealed class DiagnosticsPayloadCache : IPayloadCache
{
	// Cached projection of Entries.Items. The projection only changes when the entry cache stores a new payload (TryStore replaces Items with
	// a fresh snapshot array, or with a shared empty array when the payload is empty) or when a caller adopts the payload's own projection,
	// so the source reference is a reliable invalidation key.
	private IReadOnlyList<DiagnosticEntry>? _projectedSource;
	private ReadOnlyCollection<Diagnostic>? _projection;

	/// <summary>
	/// The version-fenced cache of the diagnostic entries, including the content snapshot the diagnostic offsets refer to and each diagnostic's raw
	/// protocol code element.
	/// </summary>
	internal VersionFencedCache<DiagnosticEntry> Entries { get; } = new();

	/// <summary>
	/// Returns the shared-diagnostic projection of the cached entries, reusing the previous projection while the cached payload is unchanged.
	/// </summary>
	/// <returns>The cached diagnostics in entry order, backed by a read-only collection.</returns>
	internal ReadOnlyCollection<Diagnostic> GetProjection()
	{
		IReadOnlyList<DiagnosticEntry> items = Entries.Items;

		if (!ReferenceEquals(_projectedSource, items) || _projection is null)
		{
			_projection = DiagnosticEntry.ProjectDiagnostics(items);
			_projectedSource = items;
		}

		return _projection;
	}

	/// <summary>
	/// Adopts a projection a caller already computed for the entries it stored, so the cached read serves that same owned snapshot
	/// instead of projecting the stored entries a second time.
	/// </summary>
	/// <param name="projection">The projection of the entries that are now cached.</param>
	/// <remarks>
	/// The caller stores the same entries the projection was built from and <see cref="VersionFencedCache{TItem}"/> snapshots them shallowly,
	/// so the adopted projection is order- and instance-equivalent to a projection of the cached entries.
	/// </remarks>
	internal void AdoptProjection(ReadOnlyCollection<Diagnostic> projection)
	{
		_projection = projection;
		_projectedSource = Entries.Items;
	}

	/// <inheritdoc/>
	public void Clear() => Entries.Clear();

	/// <inheritdoc/>
	public void ResetVersionStamp() => Entries.ResetVersionStamp();
}
