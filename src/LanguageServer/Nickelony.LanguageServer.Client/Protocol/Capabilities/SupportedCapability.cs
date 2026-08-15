using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a capability that may be advertised as either a boolean or an object.
/// </summary>
/// <param name="IsSupported">Whether the capability is supported.</param>
[JsonConverter(typeof(SupportedCapabilityJsonConverter))]
internal readonly record struct SupportedCapability(bool IsSupported);
