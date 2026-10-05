using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Commit;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Snippets;
#if AVALONIAEDIT
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Snippets;
using Nickelony.IDEKit.AvaloniaEdit.Text;
#else
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Snippets;
using Nickelony.IDEKit.AvalonEdit.Text;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

#endif

/// <summary>Applies a completion commit plan to the text area's document: the replacements as one atomic document change, then the caret.</summary>
/// <remarks>
/// <para>
/// The <see cref="CompletionCommitPlanner"/> decides the replacement ranges, the conflict handling of the additional edits, and the caret offset;
/// this type runs the request's insertion adjustment before the planner and applies the plan. A plan with more than one replacement is applied
/// inside one document update, so the whole commit is one undo unit and a single undo restores the document.
/// </para>
/// <para>
/// A request whose adjusted insertion text still equals its snippet's expanded text, and whose snippet has a tabstop
/// besides the final one, applies the primary replacement through the editor's snippet inside that same update, after the other operations.
/// The editor then owns the caret and the selection, so the plan's caret offset is not applied, and <see cref="CompletionSnippetSession"/> ends
/// snippet mode when the caret leaves the inserted text or the document is replaced. An adjustment that changed the text leaves
/// the placeholder ranges stale, so that commit is plain text.
/// </para>
/// </remarks>
internal static class CompletionCommitEditApplier
{
	/// <summary>Applies the commit to the text area's document.</summary>
	/// <param name="request">The commit to apply.</param>
	/// <exception cref="InvalidOperationException">
	/// The text area has no document to apply the commit to, or the insertion adjustment returned an insertion without text.
	/// </exception>
	internal static void Apply(CompletionCommitRequest request)
	{
		// The text area's document can be cleared while the completion window is still open; failing with a
		// documented exception keeps the commit path from dereferencing a null document inside the engine.
		TextDocument document =
			request.TextArea.Document ?? throw new InvalidOperationException("The text area has no document to apply the completion commit to.");

		var planRequest = new CompletionCommitPlanRequest(
			Snapshot: new TextDocumentSnapshot(document),
			CompletionSegment: new TextRange(request.CompletionSegment.Offset, request.CompletionSegment.Length),
			InsertText: request.InsertText,
			CaretOffsetInInsertText: request.CaretOffsetInInsertText,
			AdditionalTextEdits: request.AdditionalTextEdits,
			PrimaryEdit: request.PrimaryEdit,
			IsOverstrikeMode: request.TextArea.OverstrikeMode,
			CaretOffset: request.TextArea.Caret.Offset
		);

		if (request.AdjustInsertion is { } adjustInsertion)
		{
			int replacementOffset = CompletionCommitPlanner.ResolvePrimaryRange(planRequest).Offset;

			CompletionInsertion adjusted = adjustInsertion(
				planRequest.Snapshot,
				replacementOffset,
				new CompletionInsertion(planRequest.InsertText, planRequest.CaretOffsetInInsertText)
			);

			// A default insertion carries no text, which the planner would report as a null argument of its own;
			// naming the hook keeps the host's error visible.
			string adjustedText =
				adjusted.Text ?? throw new InvalidOperationException("The completion insertion adjustment returned an insertion without text.");

			planRequest = planRequest with { InsertText = adjustedText, CaretOffsetInInsertText = adjusted.CaretOffset };
		}

		CompletionCommitPlan plan = CompletionCommitPlanner.Plan(planRequest);

		// The tabstop ranges describe the resolved text, so an adjustment that changed the text leaves them stale and the commit is plain text.
		if (
			request.Snippet is { } expansion
			&& string.Equals(planRequest.InsertText, expansion.Text, StringComparison.Ordinal)
			&& CompletionSnippetBuilder.Build(expansion) is { } snippet
		)
		{
			ApplyWithSnippet(textArea: request.TextArea, document, plan, snippet);
			return;
		}

		if (plan.Operations.Count == 1)
		{
			TextEditOperation operation = plan.Operations[0];
			document.Replace(operation.Range.Offset, operation.Range.Length, operation.NewText);
		}
		else if (plan.Operations.Count > 1)
		{
			using (document.RunUpdate())
			{
				foreach (TextEditOperation operation in plan.Operations)
					document.Replace(operation.Range.Offset, operation.Range.Length, operation.NewText);
			}
		}

		if (plan.CaretOffset is int caretOffset)
			request.TextArea.Caret.Offset = caretOffset;
	}

	// The other operations apply first, so no edit lands at the snippet's start or inside it while the editor's anchors stand there: an
	// insertion at the start would join the first tabstop. The operations run highest offset first, so those after the primary replacement lie
	// at or below its start and shift it by their net length. The snippet completes inside the update, as the editor's own insertion does,
	// before the update's end checks that the snippet still holds text.
	private static void ApplyWithSnippet(TextArea textArea, TextDocument document, CompletionCommitPlan plan, Snippet snippet)
	{
		using (document.RunUpdate())
		{
			TextEditOperation? primary = null;
			int shift = 0;

			foreach (TextEditOperation operation in plan.Operations)
			{
				if (operation.EditIndex == 0)
				{
					primary = operation;
					continue;
				}

				document.Replace(operation.Range.Offset, operation.Range.Length, operation.NewText);

				if (primary is not null)
					shift += operation.NewText.Length - operation.Range.Length;
			}

			if (primary is null)
				return;

			int startOffset = primary.Range.Offset + shift;

			if (primary.Range.Length > 0)
				document.Remove(startOffset, primary.Range.Length);

			var context = new InsertionContext(textArea, startOffset);
			snippet.Insert(context);

			int endOffset = context.InsertionPosition;

			context.RaiseInsertionCompleted(EventArgs.Empty);
			CompletionSnippetSession.Start(textArea, context, startOffset, endOffset);
		}
	}
}
