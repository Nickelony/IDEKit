using System.Collections.Frozen;
using System.Text;

namespace Nickelony.IDEKit.Processes;

/// <summary>
/// Describes an external process to launch.
/// </summary>
/// <remarks>
/// <para>
/// The record is immutable: <see cref="ArgumentList"/> and <see cref="EnvironmentVariables"/> are copied from
/// the caller's collections when the request is constructed, and a <see langword="null"/> collection is treated
/// as empty. Equality compares the properties, including the copied collections: argument lists compare by
/// sequence and environment variables compare as a dictionary, so insertion order does not matter. The output
/// encodings compare by their code page and decoder fallback rather than by instance identity, because only
/// their decoding behavior affects the captured text.
/// </para>
/// <para>
/// The record validates the constraints of a single property as it is set; the runner validates combinations of
/// properties before it launches anything, because a combination check depends on the final property values.
/// </para>
/// </remarks>
public sealed record ProcessRunRequest
{
	private static readonly IReadOnlyDictionary<string, string> s_emptyEnvironment = FrozenDictionary<string, string>.Empty;
	private static readonly IReadOnlyList<string> s_emptyArgumentList = Array.Empty<string>();

	private readonly string _fileName = string.Empty;

	/// <summary>
	/// Gets the executable, file, or shell target to launch.
	/// </summary>
	/// <remarks>
	/// The value must not be <see langword="null"/> or blank. When <see cref="UseShellExecute"/> is
	/// <see langword="true"/>, the operating system resolves the target (an executable, a file, or a URL);
	/// otherwise, the value names the executable directly.
	/// </remarks>
	/// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The value is blank.</exception>
	public required string FileName
	{
		get => _fileName;
		init
		{
			ArgumentNullException.ThrowIfNull(value, nameof(FileName));

			_fileName = string.IsNullOrWhiteSpace(value)
				? throw new ArgumentException("The file name must not be blank.", nameof(FileName))
				: value;
		}
	}

	private readonly string _rawArguments = string.Empty;

	/// <summary>
	/// Gets the command line as one raw, pre-escaped string, or an empty string when none are set.
	/// </summary>
	/// <remarks>
	/// The value is passed to the operating system as-is, so the caller escapes it. A <see langword="null"/>
	/// value is treated as an empty string. Prefer <see cref="ArgumentList"/> when the arguments need escaping;
	/// the runner rejects a request that sets both non-empty argument shapes.
	/// </remarks>
	public string RawArguments
	{
		get => _rawArguments;
		init => _rawArguments = value ?? string.Empty;
	}

	private readonly IReadOnlyList<string> _argumentList = s_emptyArgumentList;

	/// <summary>
	/// Gets the individual command-line arguments, or an empty collection when none are set.
	/// </summary>
	/// <remarks>
	/// The runtime escapes each entry for the platform, so prefer this property over <see cref="RawArguments"/>.
	/// The collection is copied when the request is constructed, and a <see langword="null"/> collection is
	/// treated as empty.
	/// </remarks>
	/// <exception cref="ArgumentException">The collection contains a <see langword="null"/> entry.</exception>
	public IReadOnlyList<string> ArgumentList
	{
		get => _argumentList;
		init
		{
			if (value is null || value.Count == 0)
			{
				_argumentList = s_emptyArgumentList;
				return;
			}

			string[] arguments = [.. value];

			foreach (string argument in arguments)
			{
				if (argument is null)
					throw new ArgumentException("The argument list must not contain null entries.", nameof(ArgumentList));
			}

			_argumentList = Array.AsReadOnly(arguments);
		}
	}

	/// <summary>
	/// Gets the working directory for the process, or <see langword="null"/> to inherit the caller's directory.
	/// </summary>
	/// <remarks>
	/// On Windows, a shell-launched request uses the directory to locate the target, matching
	/// <see cref="System.Diagnostics.ProcessStartInfo.WorkingDirectory"/>; on other systems the operating system
	/// resolves a shell target itself. Every directly launched process starts in this directory.
	/// </remarks>
	public string? WorkingDirectory { get; init; }

	private readonly IReadOnlyDictionary<string, string> _environmentVariables = s_emptyEnvironment;

	/// <summary>
	/// Gets the environment variables to add or replace for the process, or an empty collection when none are
	/// requested.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The collection is copied into a case-sensitive snapshot when the request is constructed, and a
	/// <see langword="null"/> collection is treated as empty. The launcher copies these entries to the process
	/// start information; variables not listed here keep the environment inherited from the caller.
	/// </para>
	/// <para>
	/// An entry replaces an inherited variable case-insensitively on Windows and case-sensitively on other
	/// systems, where a differently cased name adds a second variable. Environment overrides cannot be combined
	/// with shell execution.
	/// </para>
	/// </remarks>
	public IReadOnlyDictionary<string, string> EnvironmentVariables
	{
		get => _environmentVariables;
		init => _environmentVariables = value is null || value.Count == 0 ? s_emptyEnvironment : value.ToFrozenDictionary();
	}

	/// <summary>
	/// Gets a value indicating whether the executable, file, or shell target is launched through the operating system shell.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The "shell" is the operating system's shell launcher (file associations on Windows, the platform opener
	/// on other systems), not a command interpreter. Use it when the operating system must resolve the target,
	/// such as a batch file or a document, or when the launch must honor the target's own shell behavior; a shell
	/// launch can raise the elevation prompt of a target that requests it, which a direct launch cannot.
	/// </para>
	/// <para>
	/// A shell-launched request cannot redirect standard output or error and cannot override environment
	/// variables, and it resolves <see cref="WorkingDirectory"/> as described on that property; the runner rejects
	/// those combinations. A shell launch can complete without returning a process handle, in which case the run
	/// reports <see cref="ProcessRunOutcome.NoProcessHandle"/> and cannot observe an exit. Because a shell
	/// launch cannot produce the handle <see cref="IProcessRunner.Start"/> requires, that member rejects a
	/// shell request outright; only <see cref="IProcessRunner.Run"/> and <see cref="IProcessRunner.RunAsync"/>
	/// accept one.
	/// </para>
	/// </remarks>
	public bool UseShellExecute { get; init; }

	/// <summary>
	/// Gets a value indicating whether the process is started without a console window when it is a console application.
	/// </summary>
	/// <remarks>
	/// The setting has no effect when <see cref="UseShellExecute"/> is <see langword="true"/>, and it is ignored
	/// on platforms that do not create console windows.
	/// </remarks>
	public bool CreateNoWindow { get; init; }

	/// <summary>
	/// Gets a value indicating whether standard output is redirected so it can be read from the process handle or included in a run result.
	/// </summary>
	public bool RedirectStandardOutput { get; init; }

	/// <summary>
	/// Gets a value indicating whether standard error is redirected so it can be read from the process handle or included in a run result.
	/// </summary>
	public bool RedirectStandardError { get; init; }

	/// <summary>
	/// Gets the encoding used to decode captured standard output, or <see langword="null"/> for the runtime
	/// default.
	/// </summary>
	/// <remarks>
	/// The property requires <see cref="RedirectStandardOutput"/>; the runner rejects a request that sets the
	/// encoding without the redirection. When <see langword="null"/>, the runtime's default for redirected output
	/// applies (the console output code page on Windows, UTF-8 on other systems), so set the encoding explicitly
	/// when a tool emits text in another encoding.
	/// </remarks>
	public Encoding? StandardOutputEncoding { get; init; }

	/// <summary>
	/// Gets the encoding used to decode captured standard error, or <see langword="null"/> for the runtime default.
	/// </summary>
	/// <remarks>
	/// The property requires <see cref="RedirectStandardError"/>; the runner rejects a request that sets the
	/// encoding without the redirection. When <see langword="null"/>, the runtime's default for redirected output
	/// applies (the console output code page on Windows, UTF-8 on other systems), so set the encoding explicitly
	/// when a tool emits text in another encoding.
	/// </remarks>
	public Encoding? StandardErrorEncoding { get; init; }

	private readonly TimeSpan? _timeout;

	/// <summary>
	/// Gets the maximum time <see cref="IProcessRunner.Run"/> and <see cref="IProcessRunner.RunAsync"/> wait for
	/// the process before termination is attempted, or <see langword="null"/> to wait indefinitely.
	/// <see cref="Timeout.InfiniteTimeSpan"/> is also accepted and means the same as
	/// <see langword="null"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The timeout applies to <see cref="IProcessRunner.Run"/> and <see cref="IProcessRunner.RunAsync"/>; a handle
	/// obtained from <see cref="IProcessRunner.Start"/> is driven by the caller. Termination confirmation and
	/// output capture can add the configured grace periods after the timeout elapsed; see
	/// <see cref="ProcessRunnerOptions"/>.
	/// </para>
	/// <para>
	/// A timeout shorter than one millisecond, including <see cref="TimeSpan.Zero"/>, is observed with a single
	/// non-blocking check: an already-exited process completes the run, and a running process is terminated
	/// instead of waited for.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The value is negative and not <see cref="Timeout.InfiniteTimeSpan"/>, or it is longer than
	/// <see cref="int.MaxValue"/> milliseconds.
	/// </exception>
	public TimeSpan? Timeout
	{
		get => _timeout;
		init
		{
			if (value is { } timeout
				&& timeout != System.Threading.Timeout.InfiniteTimeSpan
				&& (timeout < TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue))
			{
				throw new ArgumentOutOfRangeException(
					nameof(Timeout), value, "The timeout must be non-negative, Timeout.InfiniteTimeSpan, or null.");
			}

			_timeout = value;
		}
	}

	/// <summary>
	/// Determines whether the other request describes the same process launch. The copied collections compare
	/// structurally and the output encodings compare by code page and decoder fallback.
	/// </summary>
	/// <param name="other">The request to compare with.</param>
	/// <returns><see langword="true"/> when the requests are equal; otherwise, <see langword="false"/>.</returns>
	public bool Equals(ProcessRunRequest? other)
	{
		if (ReferenceEquals(this, other))
			return true;

		if (other is null
			|| _fileName != other._fileName
			|| _rawArguments != other._rawArguments
			|| WorkingDirectory != other.WorkingDirectory
			|| UseShellExecute != other.UseShellExecute
			|| CreateNoWindow != other.CreateNoWindow
			|| RedirectStandardOutput != other.RedirectStandardOutput
			|| RedirectStandardError != other.RedirectStandardError
			|| !EncodingsEqual(StandardOutputEncoding, other.StandardOutputEncoding)
			|| !EncodingsEqual(StandardErrorEncoding, other.StandardErrorEncoding)
			|| _timeout != other._timeout
			|| _argumentList.Count != other._argumentList.Count
			|| _environmentVariables.Count != other._environmentVariables.Count)
		{
			return false;
		}

		for (int i = 0; i < _argumentList.Count; i++)
		{
			if (_argumentList[i] != other._argumentList[i])
				return false;
		}

		foreach ((string name, string value) in _environmentVariables)
		{
			if (!other._environmentVariables.TryGetValue(name, out string? otherValue) || otherValue != value)
				return false;
		}

		return true;
	}

	/// <inheritdoc/>
	public override int GetHashCode()
	{
		var hash = new HashCode();

		hash.Add(_fileName);
		hash.Add(_rawArguments);
		hash.Add(WorkingDirectory);
		hash.Add(UseShellExecute);
		hash.Add(CreateNoWindow);
		hash.Add(RedirectStandardOutput);
		hash.Add(RedirectStandardError);
		hash.Add(GetEncodingHashCode(StandardOutputEncoding));
		hash.Add(GetEncodingHashCode(StandardErrorEncoding));
		hash.Add(_timeout);

		foreach (string argument in _argumentList)
			hash.Add(argument);

		// The environment contributes an order-independent hash so equal dictionaries hash equally.
		int environmentHash = 0;

		foreach ((string name, string value) in _environmentVariables)
			environmentHash += HashCode.Combine(name, value);

		hash.Add(environmentHash);

		return hash.ToHashCode();
	}

	// Two encodings describe the same capture when they decode identically: the same code page with the same
	// decoder fallback. Only the decoder participates - the encoding is used to decode captured output, never to
	// encode - so a cosmetic difference such as byte-order-mark emission does not make two launches different.
	private static bool EncodingsEqual(Encoding? first, Encoding? second)
	{
		if (ReferenceEquals(first, second))
			return true;

		if (first is null || second is null)
			return false;

		return first.CodePage == second.CodePage && Equals(first.DecoderFallback, second.DecoderFallback);
	}

	private static int GetEncodingHashCode(Encoding? encoding)
		=> encoding is null ? 0 : HashCode.Combine(encoding.CodePage, encoding.DecoderFallback);
}
