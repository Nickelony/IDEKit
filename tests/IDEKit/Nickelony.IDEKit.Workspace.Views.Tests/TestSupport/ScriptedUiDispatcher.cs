using Nickelony.IDEKit.Core.Threading;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

/// <summary>
/// A dispatcher whose <see cref="InvokeAsync"/> and <see cref="Post"/> play back delegates the test supplies; the coordinator reaches
/// no other member.
/// </summary>
/// <remarks>
/// <para>
/// Without a post delegate, <see cref="Post"/> throws, which the coordinator treats as a rejected post.
/// </para>
/// <para>
/// It stays apart from <c>FakePumpedDispatcher</c>, which completes every call through its own queue. The coordinator tests need to choose
/// when an invocation completes, whether it faults or never returns, and whether a post is rejected, and every other member throws so that a test
/// proves the coordinator reaches nothing else.
/// </para>
/// </remarks>
internal sealed class ScriptedUiDispatcher(Func<Action, Task> invokeAsync, Action<Action>? post = null) : IUiDispatcher
{
	public Task InvokeAsync(Action callback) => invokeAsync(callback);

	public bool CheckAccess() => throw new NotSupportedException();

	public void VerifyAccess() => throw new NotSupportedException();

	public void Invoke(Action callback) => throw new NotSupportedException();

	public TResult Invoke<TResult>(Func<TResult> callback) => throw new NotSupportedException();

	public void Post(Action callback, UiDispatcherPriority priority = UiDispatcherPriority.Normal) =>
		(post ?? throw new NotSupportedException()).Invoke(callback);

	public IDisposable Schedule(TimeSpan delay, Action callback) => throw new NotSupportedException();
}
