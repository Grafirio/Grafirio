using System.Windows.Threading;

namespace Grafirio.Bridge.Desktop.Tests.Shell;

internal static class WorkspaceSmokeSta
{
    private static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OperationDeadline = TimeSpan.FromSeconds(25);

    public static async Task RunAsync(Func<CancellationToken, CancellationToken, Task> action)
    {
        using var deadline = new CancellationTokenSource(TestDeadline);
        using var operationDeadline = new CancellationTokenSource(OperationDeadline);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => RunDispatcher(action, operationDeadline.Token, deadline.Token, completion))
        {
            IsBackground = true,
            Name = "Local workspace WebView2 smoke STA"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try
        {
            await completion.Task.WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException exception)
        {
            throw new TimeoutException(
                "The real WPF/WebView2 smoke test exceeded its deadline. An interactive Windows desktop " +
                "and an installed WebView2 Runtime are required; initialization failures are not skipped.", exception);
        }
    }

    private static void RunDispatcher(
        Func<CancellationToken, CancellationToken, Task> action,
        CancellationToken operationToken, CancellationToken cleanupToken, TaskCompletionSource completion)
    {
        Exception? failure = null;
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        try
        {
            Func<Task> executeAsync = async () =>
            {
                try
                {
                    await action(operationToken, cleanupToken);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                }
            };
            dispatcher.InvokeAsync(executeAsync);
            Dispatcher.Run();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            if (failure is null) completion.TrySetResult();
            else completion.TrySetException(failure);
        }
    }
}