namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// A sidecar file-operations double that delegates to the real file system and fails one operation on
/// demand, so <see cref="SidecarLineFile"/>'s failure mapping is pinned without a platform-specific
/// file-system failure.
/// </summary>
internal sealed class FaultingSidecarFileOperations : ISidecarFileOperations
{
	private readonly ISidecarFileOperations _inner = SidecarFileOperations.Default;

	/// <summary>
	/// Gets or sets the message of the <see cref="IOException"/> that
	/// <see cref="Move"/> throws instead of replacing the file; <see langword="null"/> moves normally.
	/// </summary>
	internal string? MoveFailureMessage { get; set; }

	/// <summary>
	/// Gets or sets the message of the <see cref="IOException"/> that
	/// <see cref="ReadAllLines"/> throws instead of reading the file; <see langword="null"/> reads
	/// normally.
	/// </summary>
	internal string? ReadFailureMessage { get; set; }

	public bool FileExists(string path) => _inner.FileExists(path);

	public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

	public void WriteAllText(string path, string contents) => _inner.WriteAllText(path, contents);

	public void Move(string sourcePath, string destinationPath)
	{
		if (MoveFailureMessage is not null)
			throw new IOException(MoveFailureMessage);

		_inner.Move(sourcePath, destinationPath);
	}

	public void Delete(string path) => _inner.Delete(path);

	public string[] ReadAllLines(string path)
		=> ReadFailureMessage is not null
			? throw new IOException(ReadFailureMessage)
			: _inner.ReadAllLines(path);
}
