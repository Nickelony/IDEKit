using System;
using System.Collections.Generic;
using System.Linq;

namespace Nickelony.Repository.Tests;

/// <summary>
/// A directed graph of named nodes in which each edge keeps the evidence that proves it, and the groups of nodes that depend on each other.
/// </summary>
internal sealed class DependencyGraph
{
	private readonly Dictionary<string, SortedSet<string>> _edges = new(StringComparer.Ordinal);
	private readonly Dictionary<(string From, string To), string> _evidence = [];

	/// <summary>The number of distinct edges, so a guard can tell an empty graph from one without a cycle.</summary>
	internal int EdgeCount => _edges.Values.Sum(targets => targets.Count);

	/// <summary>Adds an edge, and keeps the first evidence given for it.</summary>
	internal void AddEdge(string from, string to, string evidence)
	{
		if (!_edges.TryGetValue(from, out SortedSet<string>? targets))
		{
			targets = new SortedSet<string>(StringComparer.Ordinal);
			_edges[from] = targets;
		}

		targets.Add(to);
		_evidence.TryAdd((from, to), evidence);
	}

	/// <summary>The groups of nodes that depend on each other, each in alphabetical order.</summary>
	internal IEnumerable<IReadOnlyList<string>> Cycles()
	{
		int index = 0;

		Stack<string> stack = new();
		HashSet<string> onStack = new(StringComparer.Ordinal);

		Dictionary<string, int> indices = new(StringComparer.Ordinal);
		Dictionary<string, int> lowLinks = new(StringComparer.Ordinal);

		List<IReadOnlyList<string>> found = [];

		void Visit(string node)
		{
			indices[node] = lowLinks[node] = index++;
			stack.Push(node);
			onStack.Add(node);

			foreach (string next in _edges.GetValueOrDefault(node) ?? [])
			{
				if (!indices.TryGetValue(next, out int value))
				{
					Visit(next);
					lowLinks[node] = Math.Min(lowLinks[node], lowLinks[next]);
				}
				else if (onStack.Contains(next))
				{
					lowLinks[node] = Math.Min(lowLinks[node], value);
				}
			}

			if (lowLinks[node] != indices[node])
				return;

			List<string> component = [];
			string member;

			do
			{
				member = stack.Pop();
				onStack.Remove(member);
				component.Add(member);
			} while (member != node);

			if (component.Count > 1)
				found.Add([.. component.Order(StringComparer.Ordinal)]);
		}

		foreach (string node in _edges.Keys.Order(StringComparer.Ordinal).ToList())
		{
			if (!indices.ContainsKey(node))
				Visit(node);
		}

		return found;
	}

	/// <summary>
	/// The nodes of a graph without cycles in layers, from the nodes that depend on no other node up to the nodes
	/// that depend on the most. A node sits one layer above the highest node it depends on, and the nodes of a layer are in alphabetical order.
	/// An edge that closes a cycle counts for nothing, because <see cref="Cycles"/> reports the cycle.
	/// </summary>
	internal IReadOnlyList<IReadOnlyList<string>> Layers(IEnumerable<string> nodes)
	{
		Dictionary<string, int> layers = new(StringComparer.Ordinal);
		HashSet<string> visiting = new(StringComparer.Ordinal);

		int LayerOf(string node)
		{
			if (layers.TryGetValue(node, out int known))
				return known;

			if (!visiting.Add(node))
				return -1;

			int layer = 0;

			foreach (string next in _edges.GetValueOrDefault(node) ?? [])
				layer = Math.Max(layer, LayerOf(next) + 1);

			visiting.Remove(node);
			layers[node] = layer;

			return layer;
		}

		return
		[
			.. nodes.GroupBy(LayerOf).OrderBy(group => group.Key).Select(group => (IReadOnlyList<string>)[.. group.Order(StringComparer.Ordinal)]),
		];
	}

	/// <summary>Names the evidence of each edge inside a group, in alphabetical order.</summary>
	internal string Describe(IReadOnlyList<string> cycle)
	{
		IEnumerable<string> proofs = _evidence
			.Where(pair => cycle.Contains(pair.Key.From) && cycle.Contains(pair.Key.To))
			.Select(pair => pair.Value)
			.Order(StringComparer.Ordinal);

		return string.Join("; ", proofs);
	}
}
