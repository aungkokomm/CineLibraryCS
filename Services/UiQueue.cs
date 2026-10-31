using Microsoft.UI.Dispatching;

namespace CineLibraryCS.Services;

/// <summary>
/// v4.10.0: every job queued for the UI thread goes through here, never straight to TryEnqueue.
/// An exception escaping a TryEnqueue callback ends the process at once with 0xC000027B: no App or
/// AppDomain handler, no Windows report, nothing in any log. Posted this way it reaches
/// App.UnhandledException, as one from an awaited continuation does, and the app carries on.
/// A DispatcherQueueTimer's Tick ends it the same way, so a tick posts its work here too.
/// </summary>
internal static class UiQueue
{
    public static void Post(this DispatcherQueue queue, Action job) =>
        new DispatcherQueueSynchronizationContext(queue).Post(_ => job(), null);
}
