using Nickelony.IDEKit.Core.Editing;

namespace Nickelony.IDEKit.Testing;

/// <summary>
/// Builds prepared edit batches for tests through the public kernel, so the tests construct batches through the same path a host uses.
/// </summary>
internal static class PreparedBatchFactory
{
	/// <summary>Builds a batch from the supplied operations, ordered for application by the kernel.</summary>
	/// <param name="operations">The operations to prepare.</param>
	/// <returns>The prepared batch; empty when the operations were rejected.</returns>
	public static PreparedTextEdits Create(TextEditOperation[] operations) => TextEditKernel.Prepare(operations).Edits;
}
