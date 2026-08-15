using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Testing;

/// <summary>
/// An <see cref="ITextEditTarget"/> test double that records the applied edits without updating the
/// editor document.
/// </summary>
/// <remarks>
/// This double deliberately does not honor the edit-target contract's requirement to update the editor
/// document before returning, so it is used to pin the documented behavior for such targets (for
/// example caret offsets clamped against the stale editor document). Use
/// <see cref="ContractEditTarget"/> when the contract must hold.
/// </remarks>
internal sealed class RecordingEditTarget : ITextEditTarget
{
	private readonly List<TextEditOperation> _operations = [];

	/// <summary>
	/// Initializes a new instance of the <see cref="RecordingEditTarget"/> class.
	/// </summary>
	/// <param name="text">The target content reported by <see cref="Text"/>; empty by default.</param>
	public RecordingEditTarget(string text = "")
		=> Text = text;

	/// <summary>
	/// Gets the number of times <see cref="Apply"/> was called.
	/// </summary>
	public int ApplyCalls { get; private set; }

	/// <summary>
	/// Gets the operations of every applied batch, in application order.
	/// </summary>
	public IReadOnlyList<TextEditOperation> Operations => _operations;

	/// <inheritdoc/>
	public string Text { get; }

	/// <inheritdoc/>
	public void Apply(PreparedTextEdits edits)
	{
		ApplyCalls++;
		_operations.AddRange(edits.Operations);
	}
}
