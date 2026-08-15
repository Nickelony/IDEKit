using System.Text;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>
/// Enables the code-pages provider once for the test process.
/// </summary>
/// <remarks>
/// <see cref="WorkspaceTextCodec"/> no longer registers the provider itself, so the Windows-1252
/// coverage in this assembly performs the same startup step a host does before its first
/// Windows-1252 document.
/// </remarks>
[TestClass]
public sealed class WorkspaceCodePagesInitializer
{
	[AssemblyInitialize]
	public static void Initialize(TestContext _) =>
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
}
