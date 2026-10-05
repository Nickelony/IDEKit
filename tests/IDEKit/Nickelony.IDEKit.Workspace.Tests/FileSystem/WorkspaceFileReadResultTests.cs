using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

[TestClass]
public sealed class WorkspaceFileReadResultTests
{
	private static readonly FileStamp s_stamp = new(Exists: true, Length: 3, LastWriteTimeUtc: DateTime.UnixEpoch, ContentHash: "hash");

	[TestMethod]
	public void ResolveContent_NullCandidates_ThrowsForEveryKindOfRead()
	{
		var bytes = WorkspaceFileReadResult.FromBytes(s_stamp, "A"u8.ToArray());
		var decoded = WorkspaceFileReadResult.FromContent("A", default, s_stamp);

		Assert.ThrowsExactly<ArgumentNullException>(() => bytes.ResolveContent(default, null!, out _));
		Assert.ThrowsExactly<ArgumentNullException>(() => decoded.ResolveContent(default, null!, out _));
	}

	[TestMethod]
	public void ResolveContent_ARawBytesReadDecodesWithTheCandidatesAndAnInvalidListIsAnArgumentError()
	{
		var bytes = WorkspaceFileReadResult.FromBytes(s_stamp, new byte[] { 0xE9 });

		string content = bytes.ResolveContent(default, [TextEncoding.Utf8, TextEncoding.Windows1252], out TextFileFormat format);

		Assert.AreEqual("\u00E9", content);
		Assert.AreEqual(TextEncoding.Windows1252, format.Encoding);
		Assert.ThrowsExactly<ArgumentException>(() => bytes.ResolveContent(default, [], out _));
	}

	[TestMethod]
	public void ResolveContent_ADecodedReadIgnoresTheCandidatesAndReportsItsOwnFormat()
	{
		var format = new TextFileFormat(TextEncoding.Utf16LittleEndian, true, LineTerminatorStyle.CrLf);
		var decoded = WorkspaceFileReadResult.FromContent("A", format, s_stamp);

		string content = decoded.ResolveContent(default, [], out TextFileFormat resolved);

		Assert.AreEqual("A", content);
		Assert.AreEqual(format, resolved);
	}
}
