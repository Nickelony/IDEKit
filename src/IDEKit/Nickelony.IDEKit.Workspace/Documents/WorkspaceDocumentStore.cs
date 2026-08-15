using Nickelony.IDEKit.Core.Pathing;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Owns logical workspace document content, persistence state, and file-system operations.
/// </summary>
/// <remarks>
/// <para>
/// Document ids are normalized full paths. Identity comparison uses the <see cref="LocalPathComparisonPolicy"/>
/// supplied to the constructor; by default it is case-insensitive on Windows and macOS, and ordinal on
/// other platforms.
/// </para>
/// <para>
/// Public operations are safe to call concurrently. Document mutations and snapshot creation are
/// serialized under the store state lock, and disk operations use per-document disk gates.
/// </para>
/// <para>
/// Disposal waits for tracked open reservations and registered disk operations before it releases the
/// tracked documents. Reloads of dirty documents are not registered as active operations and are
/// handled as described on <see cref="DisposeAsync"/>.
/// </para>
/// </remarks>
public sealed partial class WorkspaceDocumentStore : IWorkspaceDocumentStore
{
	private readonly object _stateLock = new();
	private readonly IWorkspaceFileSystem _fileSystem;
	private readonly LocalPathComparisonPolicy _pathComparison;
	private readonly Dictionary<string, LogicalDocument> _documents;
	private readonly Dictionary<string, OpenReservation> _openReservations;
	private readonly Dictionary<string, DestinationReservation> _destinationReservations;
	private readonly List<DirectoryOperationReservation> _directoryOperations = [];
	private readonly HashSet<OperationRegistration> _activeOperations = [];
	private readonly CancellationTokenSource _lifetimeCancellation = new();
	private Task? _disposeTask;
	private bool _disposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkspaceDocumentStore"/> class.
	/// </summary>
	/// <param name="fileSystem">The file-system implementation used for reads, writes, moves, and deletes.</param>
	/// <param name="options">
	/// The options shared with the file system and the reload coordinator. The default follows the
	/// operating system: case-insensitive on Windows and macOS, and ordinal on other platforms.
	/// Supply the same value to every component of the layer when the file system's semantics differ
	/// from the operating system (for example, a case-sensitive volume on macOS). Consumers that
	/// track the same identities read <see cref="PathComparison"/> instead of repeating the value.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="fileSystem"/> is <see langword="null"/>.</exception>
	public WorkspaceDocumentStore(
		IWorkspaceFileSystem fileSystem,
		WorkspaceDocumentOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(fileSystem);

		_fileSystem = fileSystem;
		_pathComparison = (options ?? WorkspaceDocumentOptions.Default).PathComparison;
		_documents = new Dictionary<string, LogicalDocument>(_pathComparison.Comparer);
		_openReservations = new Dictionary<string, OpenReservation>(_pathComparison.Comparer);
		_destinationReservations = new Dictionary<string, DestinationReservation>(_pathComparison.Comparer);
		TestHooks = new WorkspaceDocumentStoreTestHooks(_stateLock);
	}

	/// <inheritdoc/>
	public LocalPathComparisonPolicy PathComparison => _pathComparison;
}
