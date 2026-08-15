using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Completion;
using System.Reflection;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

public sealed partial class TextCompletionItemTests
{
	private static TextCompletionItem CreateFullyPopulatedItem()
	{
		return new TextCompletionItem("label")
		{
			InsertText = "inserted",
			Documentation = " description ",
			DocumentationKind = TextMarkupKind.PlainText,
			Detail = " detail ",
			Priority = 2.5,
			Kind = TextCompletionItemKind.Method,
			FilterText = "filtered",
			SortText = "0001",
			IsPreselected = true,
			TextEdit = new TextCompletionTextEdit(new TextRange(2, 4), new TextRange(2, 10), "replacement"),
			InsertTextFormat = TextCompletionInsertTextFormat.Snippet,
			Tags = [TextCompletionTag.Deprecated],
			CommitCharacters = ["(", ","],
			AdditionalTextEdits = [new TextCompletionTextEdit(new TextRange(0, 0), newText: "import")],
			RequestDocumentVersion = 1,
			RequestGeneration = 2,
			ResolveCallback = static _ => Task.FromResult(new TextCompletionItem("resolved"))
		};
	}

	private static void AssertItemsHaveSamePayload(TextCompletionItem expected, TextCompletionItem actual, params string[] excludedProperties)
	{
		foreach (PropertyInfo property in typeof(TextCompletionItem).GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			if (excludedProperties.Contains(property.Name))
				continue;

			object? expectedValue = property.GetValue(expected);
			object? actualValue = property.GetValue(actual);

			if (expectedValue is System.Collections.IEnumerable expectedSequence and not string
				&& actualValue is System.Collections.IEnumerable actualSequence and not string)
			{
				// Collection properties are compared element-wise so the helper verifies the copied
				// content instead of the copy's list-instance identity.
				CollectionAssert.AreEqual(
					expectedSequence.Cast<object?>().ToList(),
					actualSequence.Cast<object?>().ToList(),
					$"The copy must preserve '{property.Name}'.");
				continue;
			}

			Assert.AreEqual(
				expectedValue,
				actualValue,
				$"The copy must preserve '{property.Name}'.");
		}
	}
}
