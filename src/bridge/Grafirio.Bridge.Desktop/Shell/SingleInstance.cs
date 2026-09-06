using System.Security.Principal;

namespace Grafirio.Bridge.Desktop.Shell;

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activation;
    private RegisteredWaitHandle? _registration;
    public bool IsFirstInstance { get; }

    public SingleInstance()
    {
        var identity = WindowsIdentity.GetCurrent().User!.Value;
        _mutex = new Mutex(true, $"Local\\Grafirio.Desktop.{identity}", out var created);
        IsFirstInstance = created;
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset,
            $"Local\\Grafirio.Desktop.Activate.{identity}");
        if (!created) _activation.Set();
    }

    public void Listen(Action activate) => _registration = ThreadPool.RegisterWaitForSingleObject(
        _activation, (_, _) => activate(), null, Timeout.Infinite, executeOnlyOnce: false);

    public void Dispose()
    {
        _registration?.Unregister(null);
        _activation.Dispose();
        if (IsFirstInstance) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}