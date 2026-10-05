using System.Reflection;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Markup;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

/// <summary>Creates a <see cref="CompletionItem"/> with every property set and compares the payload of two items.</summary>
internal static class CompletionItemTestHelpers
{
	/// <summary>Creates an item whose every public property holds a non-default value.</summary>
	internal static CompletionItem CreateFullyPopulatedItem()
	{
		return new CompletionItem("label")
		{
			InsertText = "inserted",
			Documentation = " description ",
			Detail = " detail ",
			Priority = 2.5,
			Kind = CompletionItemKind.Method,
			FilterText = "filtered",
			SortText = "0001",
			IsPreselected = true,
			TextEdit = new CompletionTextEdit(new TextRange(2, 4), new TextRange(2, 10), "replacement"),
			InsertTextFormat = CompletionInsertTextFormat.Snippet,
			Tags = [CompletionItemTag.Deprecated],
			CommitCharacters = ["(", ","],
			AdditionalTextEdits = [new CompletionTextEdit(new TextRange(0, 0), newText: "import")],
			RequestDocumentVersion = 1,
			RequestGeneration = 2,
			ResolveCallback = static _ => Task.FromResult(new CompletionItem("resolved")),
		};
	}

	/// <summary>
	/// Asserts that every public property of the two items is equal, element by element for a collection, except the excluded ones.
	/// </summary>
	/// <param name="expected">The item that holds the expected values.</param>
	/// <param name="actual">The item to compare with it.</param>
	/// <param name="excludedProperties">The names of the properties that the comparison leaves out.</param>
	internal static void AssertItemsHaveSamePayload(CompletionItem expected, CompletionItem actual, params string[] excludedProperties)
	{
		foreach (PropertyInfo property in typeof(CompletionItem).GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			if (excludedProperties.Contains(property.Name))
				continue;

			object? expectedValue = property.GetValue(expected);
			object? actualValue = property.GetValue(actual);

			if (
				expectedValue is System.Collections.IEnumerable expectedSequence and not string
				&& actualValue is System.Collections.IEnumerable actualSequence and not string
			)
			{
				// Collection properties are compared element-wise so the helper verifies the copied
				// content instead of the copy's list-instance identity.
				Assert.AreSequenceEqual(
					[.. expectedSequence.Cast<object?>()],
					[.. actualSequence.Cast<object?>()],
					$"The copy must preserve '{property.Name}'."
				);

				continue;
			}

			Assert.AreEqual(expectedValue, actualValue, $"The copy must preserve '{property.Name}'.");
		}
	}
}
