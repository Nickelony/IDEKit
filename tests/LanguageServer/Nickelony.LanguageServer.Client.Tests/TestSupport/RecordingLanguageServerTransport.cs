namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>A transport that hands each connect request to a delegate and keeps the requests it received.</summary>
internal sealed class RecordingLanguageServerTransport : ILanguageServerTransport
{
	private readonly Func<LanguageServerConnectRequest, CancellationToken, Task<ILanguageServerConnection>> _connect;

	private int _connectCount;

	public RecordingLanguageServerTransport(Func<LanguageServerConnectRequest, CancellationToken, Task<ILanguageServerConnection>> connect) =>
		_connect = connect;

	public LanguageServerConnectRequest? LastRequest { get; private set; }

	/// <summary>How many times the client asked this transport to connect.</summary>
	public int ConnectCount => Volatile.Read(ref _connectCount);

	public Task<ILanguageServerConnection> ConnectAsync(LanguageServerConnectRequest request, CancellationToken cancellationToken)
	{
		Interlocked.Increment(ref _connectCount);
		LastRequest = request;
		return _connect(request, cancellationToken);
	}
}
