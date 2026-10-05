using System.Text;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>Enables the code-pages provider once for the test process.</summary>
/// <remarks>
/// The library does not register the provider itself, so the code-page coverage in this assembly (Windows-1252, Windows-1250, Shift-JIS)
/// performs the same startup step a host does before its first code-page document.
/// </remarks>
[TestClass]
public sealed class WorkspaceCodePagesInitializer
{
	[AssemblyInitialize]
	public static void Initialize(TestContext _) => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
}
