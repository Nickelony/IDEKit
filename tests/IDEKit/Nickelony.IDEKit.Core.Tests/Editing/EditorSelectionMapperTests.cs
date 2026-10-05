namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class EditorSelectionMapperTests
{
	// The original text is "alpha\nbeta gamma\ndelta"; the edit replaces "beta" (offsets 6 to 10) with "BETA GROUP", which adds six characters.
	private const string EditedText = "alpha\nBETA GROUP gamma\ndelta";

	[TestMethod]
	public void Map_CaretBeforeTheEdit_StaysPut()
	{
		EditorSelection mapped = MapReplacement(new EditorSelection(2, new TextRange(2, 0)), caretLineNumber: 1);
		Assert.AreEqual(new EditorSelection(2, new TextRange(2, 0)), mapped);
	}

	[TestMethod]
	public void Map_CaretAfterTheEdit_ShiftsByTheLengthDelta()
	{
		EditorSelection mapped = MapReplacement(new EditorSelection(12, new TextRange(12, 0)), caretLineNumber: 2);
		Assert.AreEqual(new EditorSelection(18, new TextRange(18, 0)), mapped);
	}

	[TestMethod]
	public void Map_CaretAtTheStartOfTheReplacedRange_StaysBeforeTheReplacement()
	{
		EditorSelection mapped = MapReplacement(new EditorSelection(6, new TextRange(6, 0)), caretLineNumber: 2);
		Assert.AreEqual(new EditorSelection(6, new TextRange(6, 0)), mapped);
	}

	[TestMethod]
	public void Map_CaretAtTheEndOfTheReplacedRange_MovesBehindTheReplacement()
	{
		EditorSelection mapped = MapReplacement(new EditorSelection(10, new TextRange(10, 0)), caretLineNumber: 2);
		Assert.AreEqual(new EditorSelection(16, new TextRange(16, 0)), mapped);
	}

	[TestMethod]
	public void Map_SelectionAfterTheEdit_ShiftsWithTheCaret()
	{
		EditorSelection mapped = MapReplacement(new EditorSelection(16, new TextRange(11, 5)), caretLineNumber: 2);
		Assert.AreEqual(new EditorSelection(22, new TextRange(17, 5)), mapped);
	}

	[TestMethod]
	public void Map_SelectionEndingAtTheStartOfTheReplacedRange_KeepsItsPlace()
	{
		EditorSelection mapped = MapReplacement(new EditorSelection(2, new TextRange(2, 4)), caretLineNumber: 1);
		Assert.AreEqual(new EditorSelection(2, new TextRange(2, 4)), mapped);
	}

	[TestMethod]
	public void Map_SelectionStartingAtTheEndOfTheReplacedRange_ShiftsWithTheEdit()
	{
		EditorSelection mapped = MapReplacement(new EditorSelection(10, new TextRange(10, 3)), caretLineNumber: 2);
		Assert.AreEqual(new EditorSelection(16, new TextRange(16, 3)), mapped);
	}

	[TestMethod]
	public void Map_CaretInsideTheSelection_KeepsItsPlaceInsideIt()
	{
		EditorSelection mapped = MapReplacement(new EditorSelection(13, new TextRange(11, 5)), caretLineNumber: 2);
		Assert.AreEqual(new EditorSelection(19, new TextRange(17, 5)), mapped);
	}

	[TestMethod]
	public void Map_CaretOutsideTheSelection_IsClampedIntoIt()
	{
		EditorSelection mapped = MapReplacement(new EditorSelection(0, new TextRange(11, 5)), caretLineNumber: 1);
		Assert.AreEqual(new EditorSelection(17, new TextRange(17, 5)), mapped);
	}

	[TestMethod]
	public void Map_CaretInsideTheReplacedRange_MovesToTheEndOfItsLine()
	{
		// The edited line 2 is "BETA GROUP gamma", which ends at offset 22.
		EditorSelection mapped = MapReplacement(new EditorSelection(8, new TextRange(8, 0)), caretLineNumber: 2);
		Assert.AreEqual(new EditorSelection(22, new TextRange(22, 0)), mapped);
	}

	[TestMethod]
	public void Map_SelectionOverlappingTheReplacedRange_MovesToTheEndOfTheCaretLine()
	{
		EditorSelection mapped = MapReplacement(new EditorSelection(2, new TextRange(2, 6)), caretLineNumber: 1);
		Assert.AreEqual(new EditorSelection(5, new TextRange(5, 0)), mapped);
	}

	[TestMethod]
	public void Map_EmptySelectionInsideTheReplacedRange_DoesNotForceTheFallback()
	{
		// The caret is outside the replaced range; an empty selection cannot be replaced, so its position is mapped like any offset.
		EditorSelection mapped = MapReplacement(new EditorSelection(2, new TextRange(8, 0)), caretLineNumber: 1);
		Assert.AreEqual(new EditorSelection(8, new TextRange(8, 0)), mapped);
	}

	[TestMethod]
	public void Map_LineOfTheCaretGone_MovesToTheEndOfTheText()
	{
		// The edit replaces "b\nc" with "X", so the edited text "a\nX" has no line 3.
		PreparedTextEdits edits = Prepare(new TextEditOperation(new TextRange(2, 3), "X", editIndex: 0));

		EditorSelection mapped = EditorSelectionMapper.Map(
			edits,
			before: new EditorSelection(4, new TextRange(4, 0)),
			caretLineNumber: 3,
			editedText: new StringTextSnapshot("a\nX")
		);

		Assert.AreEqual(new EditorSelection(3, new TextRange(3, 0)), mapped);
	}

	[TestMethod]
	public void Map_SeveralEdits_KeepsAPositionBetweenThemAndFallsBackInsideOne()
	{
		// "0123456789ABCDEF": the lower edit deletes "23" and the higher edit replaces "AB" (offsets 10 and 11) with "ZZZZ".
		PreparedTextEdits edits = Prepare(
			new TextEditOperation(new TextRange(10, 2), "ZZZZ", editIndex: 1),
			new TextEditOperation(new TextRange(2, 2), string.Empty, editIndex: 0)
		);

		var edited = new StringTextSnapshot("01456789ZZZZCDEF");

		EditorSelection between = EditorSelectionMapper.Map(
			edits,
			before: new EditorSelection(6, new TextRange(6, 0)),
			caretLineNumber: 1,
			editedText: edited
		);

		EditorSelection inside = EditorSelectionMapper.Map(
			edits,
			before: new EditorSelection(11, new TextRange(11, 0)),
			caretLineNumber: 1,
			editedText: edited
		);

		Assert.AreEqual(new EditorSelection(4, new TextRange(4, 0)), between);
		Assert.AreEqual(new EditorSelection(16, new TextRange(16, 0)), inside);
	}

	[TestMethod]
	public void Map_MappedPositionPastTheEditedText_IsClampedToItsLength()
	{
		PreparedTextEdits edits = Prepare(new TextEditOperation(new TextRange(0, 0), "XXXX", editIndex: 0));

		EditorSelection mapped = EditorSelectionMapper.Map(
			edits,
			before: new EditorSelection(3, new TextRange(3, 0)),
			caretLineNumber: 1,
			editedText: new StringTextSnapshot("XXXXa")
		);

		Assert.AreEqual(new EditorSelection(5, new TextRange(5, 0)), mapped);
	}

	[TestMethod]
	public void Map_NullArgument_Throws()
	{
		PreparedTextEdits edits = Prepare();

		var text = new StringTextSnapshot("a");

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			EditorSelectionMapper.Map(edits: null!, before: default, caretLineNumber: 1, editedText: text)
		);

		Assert.ThrowsExactly<ArgumentNullException>(() => EditorSelectionMapper.Map(edits, before: default, caretLineNumber: 1, editedText: null!));
	}

	[TestMethod]
	public void Map_CaretLineNumberBelowOne_Throws()
	{
		PreparedTextEdits edits = Prepare();

		var text = new StringTextSnapshot("a");

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			EditorSelectionMapper.Map(edits, before: default, caretLineNumber: 0, editedText: text)
		);
	}

	private static EditorSelection MapReplacement(EditorSelection before, int caretLineNumber)
	{
		PreparedTextEdits edits = Prepare(new TextEditOperation(new TextRange(6, 4), "BETA GROUP", editIndex: 0));
		return EditorSelectionMapper.Map(edits, before, caretLineNumber, editedText: new StringTextSnapshot(EditedText));
	}

	private static PreparedTextEdits Prepare(params TextEditOperation[] operations) => TextEditKernel.Prepare(operations).Edits;
}
