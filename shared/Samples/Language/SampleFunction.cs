namespace Nickelony.Samples;

/// <summary>One library function: its name, summary and parameters as <c>name: description</c> entries.</summary>
internal sealed record SampleFunction(string Name, string Summary, IReadOnlyList<string> Parameters);
