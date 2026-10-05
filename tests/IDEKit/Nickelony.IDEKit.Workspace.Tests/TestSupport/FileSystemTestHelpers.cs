using System.Security.Cryptography;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>Builds the primitives and the hashes that the tests of the local file-system collaborators share.</summary>
internal static class FileSystemTestHelpers
{
	internal const int SharingViolation = unchecked((int)0x80070020);
	internal const int LockViolation = unchecked((int)0x80070021);
	internal const int UnableToRemoveReplaced = unchecked((int)0x80070497);
	internal const int UnableToMoveReplacement = unchecked((int)0x80070498);
	internal const int UnableToMoveReplacement2 = unchecked((int)0x80070499);
	internal const int GenericIOFailure = unchecked((int)0x80131620);

	/// <summary>The local operations, with the given operations substituted.</summary>
	internal static WorkspaceFileSystemPrimitives Primitives(
		Action<string, string>? replace = null,
		Action<string, string>? moveFile = null,
		Action<string, string>? moveDirectory = null,
		Action<string>? deleteFile = null
	)
	{
		WorkspaceFileSystemPrimitives local = WorkspaceFileSystemPrimitives.Default;

		return new WorkspaceFileSystemPrimitives(
			Replace: replace ?? local.Replace,
			MoveFile: moveFile ?? local.MoveFile,
			MoveDirectory: moveDirectory ?? local.MoveDirectory,
			DeleteFile: deleteFile ?? local.DeleteFile
		);
	}

	/// <summary>The hash that a stamp records for the bytes.</summary>
	internal static string HashOf(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
