#if AVALONIAEDIT
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.Tests;
#else
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.Tests;
#endif

/// <summary>
/// Builds prepared edit batches for tests through the public kernel, so the tests construct batches
/// through the same path a host uses.
/// </summary>
internal static class PreparedBatch
{
	/// <summary>
	/// Builds a batch from the supplied operations, ordered for application by the kernel.
	/// </summary>
	/// <param name="operations">The operations to prepare.</param>
	/// <returns>The prepared batch; empty when the operations were rejected.</returns>
	public static PreparedTextEdits From(TextEditOperation[] operations)
		=> TextEditKernel.Prepare(operations).Edits;
}
