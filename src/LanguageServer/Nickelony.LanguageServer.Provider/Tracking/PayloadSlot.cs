namespace Nickelony.LanguageServer.Provider;

/// <summary>Names the payload cache that one feature keeps in every tracked document.</summary>
/// <typeparam name="TCache">The cache type the feature stores in the slot.</typeparam>
/// <remarks>
/// A feature declares one static slot and reads and writes its payloads through the tracker with it. A slot compares by reference, so two features
/// never share a cache, and a tracked document creates the cache of a slot on first use.
/// </remarks>
internal sealed class PayloadSlot<TCache>
	where TCache : class, IPayloadCache, new() { }
