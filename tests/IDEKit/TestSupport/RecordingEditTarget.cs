using Nickelony.IDEKit.Core.Editing;

namespace Nickelony.IDEKit.Testing;

/// <summary>An <see cref="ITextEditTarget"/> test double that records the applied edits without updating the editor document.</summary>
/// <remarks>
/// Use <see cref="FakeEditTarget"/> when the edit-target contract must hold.
/// </remarks>
internal sealed class RecordingEditTarget : ITextEditTarget
{
	private readonly List<TextEditOperation> _operations = [];

	/// <param name="text">The target content reported by <see cref="Text"/>; empty by default.</param>
	public RecordingEditTarget(string text = "") => Text = text;

	/// <summary>The number of times <see cref="Apply"/> was called.</summary>
	public int ApplyCalls { get; private set; }

	/// <summary>The operations of every applied batch, in application order.</summary>
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
