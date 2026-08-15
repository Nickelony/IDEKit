using System.Collections.ObjectModel;

namespace Nickelony.IDEKit.IntelliSense;

/// <summary>
/// Captures caller-supplied payload collections into owned read-only snapshots.
/// </summary>
/// <remarks>
/// Payload types that expose a list reject <see langword="null"/> elements so a host never reads one.
/// These helpers hold that rule and the owned-snapshot construction in one place instead of repeating
/// the validation loop and the read-only copy in every payload. A site whose exception type or message
/// is part of its own contract keeps its own validation.
/// </remarks>
internal static class PayloadCollections
{
	/// <summary>
	/// Gets the shared empty snapshot for <typeparamref name="T"/> so every payload that carries no
	/// elements observes one instance.
	/// </summary>
	/// <typeparam name="T">The element type.</typeparam>
	/// <returns>The shared empty read-only snapshot.</returns>
	internal static ReadOnlyCollection<T> Empty<T>()
		where T : class
		=> EmptyCache<T>.Value;

	/// <summary>
	/// Rejects a <see langword="null"/> element in a caller-supplied collection.
	/// </summary>
	/// <typeparam name="T">The element type.</typeparam>
	/// <param name="values">The collection to validate, or <see langword="null"/> for none.</param>
	/// <param name="collectionName">The collection name used in the exception message.</param>
	/// <param name="paramName">The parameter name used in the exception.</param>
	/// <exception cref="ArgumentException">An element is <see langword="null"/>.</exception>
	internal static void ValidateElements<T>(IReadOnlyList<T>? values, string collectionName, string paramName)
		where T : class
	{
		if (values is not { Count: > 0 })
			return;

		for (int i = 0; i < values.Count; i++)
		{
			if (values[i] is null)
			{
				throw new ArgumentException(
					$"The {collectionName} collection contains a null element at index {i}.",
					paramName);
			}
		}
	}

	/// <summary>
	/// Validates and captures a caller-supplied collection into an owned read-only snapshot.
	/// </summary>
	/// <typeparam name="T">The element type.</typeparam>
	/// <param name="values">The collection to capture, or <see langword="null"/> for none.</param>
	/// <param name="collectionName">The collection name used in the exception message.</param>
	/// <param name="paramName">The parameter name used in the exception.</param>
	/// <returns>
	/// The owned snapshot; the shared empty snapshot when <paramref name="values"/> is
	/// <see langword="null"/> or empty.
	/// </returns>
	/// <exception cref="ArgumentException">An element is <see langword="null"/>.</exception>
	internal static ReadOnlyCollection<T> Capture<T>(IReadOnlyList<T>? values, string collectionName, string paramName)
		where T : class
	{
		if (values is not { Count: > 0 })
			return Empty<T>();

		ValidateElements(values, collectionName, paramName);

		return Array.AsReadOnly<T>([.. values]);
	}

	private static class EmptyCache<T>
		where T : class
	{
		internal static readonly ReadOnlyCollection<T> Value = Array.AsReadOnly<T>([]);
	}
}
