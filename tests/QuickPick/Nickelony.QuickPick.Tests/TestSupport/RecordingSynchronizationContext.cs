namespace Nickelony.QuickPick.Tests;

/// <summary>A synchronization context that queues a post for the test to run, so a test states when work reaches the owner thread.</summary>
internal sealed class RecordingSynchronizationContext : SynchronizationContext
{
	private readonly List<Action> _posted = [];

	/// <summary>The exception a post throws, or <see langword="null"/> to queue the post.</summary>
	public Exception? PostFault { get; set; }

	/// <summary>The number of posts that wait for the test to run them.</summary>
	public int PostedCount
	{
		get
		{
			lock (_posted)
				return _posted.Count;
		}
	}

	public override void Post(SendOrPostCallback d, object? state)
	{
		if (PostFault is { } fault)
			throw fault;

		lock (_posted)
			_posted.Add(() => d(state));
	}

	/// <summary>Runs the queued posts on the calling thread, in order.</summary>
	public void RunPosted()
	{
		Action[] posts;

		lock (_posted)
		{
			posts = [.. _posted];
			_posted.Clear();
		}

		foreach (Action post in posts)
			post();
	}
}
