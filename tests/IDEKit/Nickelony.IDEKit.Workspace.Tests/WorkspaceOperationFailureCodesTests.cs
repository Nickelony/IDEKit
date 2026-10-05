using System.Text;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>Covers the classification of an exception onto the failure code a workspace operation reports.</summary>
[TestClass]
public sealed class WorkspaceOperationFailureCodesTests
{
	[TestMethod]
	public void ClassifyReadFailure_MapsTheExceptionToItsCodeOrTheCallersFallback()
	{
		(Exception Exception, string Expected)[] cases =
		[
			(new UnauthorizedAccessException(), WorkspaceOperationFailureCodes.AccessDenied),
			(new DecoderFallbackException(), WorkspaceOperationFailureCodes.InvalidEncoding),
			(new IOException(), "Fallback"),
			(new InvalidOperationException(), "Fallback"),
		];

		foreach ((Exception exception, string expected) in cases)
			Assert.AreEqual(expected, WorkspaceOperationFailureCodes.ClassifyReadFailure(exception, "Fallback"), exception.GetType().Name);
	}

	[TestMethod]
	public void ClassifyWriteFailure_MapsAnEncodingFailureToInvalidEncodingAndEverythingElseToWriteFailed()
	{
		(Exception Exception, string Expected)[] cases =
		[
			(new EncoderFallbackException(), WorkspaceOperationFailureCodes.InvalidEncoding),
			(new IOException(), WorkspaceOperationFailureCodes.WriteFailed),
			(new UnauthorizedAccessException(), WorkspaceOperationFailureCodes.WriteFailed),
		];

		foreach ((Exception exception, string expected) in cases)
			Assert.AreEqual(expected, WorkspaceOperationFailureCodes.ClassifyWriteFailure(exception), exception.GetType().Name);
	}

	[TestMethod]
	public void ClassifyDirectoryOperationFailure_MapsAPermissionFailureToAccessDeniedAndEverythingElseToTheOperationCode()
	{
		(Exception Exception, string Expected)[] cases =
		[
			(new UnauthorizedAccessException(), WorkspaceOperationFailureCodes.AccessDenied),
			(new IOException(), "OperationCode"),
			(new InvalidOperationException(), "OperationCode"),
		];

		foreach ((Exception exception, string expected) in cases)
		{
			Assert.AreEqual(
				expected,
				WorkspaceOperationFailureCodes.ClassifyDirectoryOperationFailure(exception, "OperationCode"),
				exception.GetType().Name
			);
		}
	}
}
