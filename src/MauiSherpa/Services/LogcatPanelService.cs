namespace MauiSherpa.Services;

public class LogcatPanelService
{
    public bool IsOpen { get; private set; }
    public bool IsMinimized { get; private set; }
    public string? ActiveSerial { get; private set; }
    public string? ActiveDeviceName { get; private set; }

    public event Action? StateChanged;
    public event Action<string>? DeviceChanged;

    public void Open(string serial, string? deviceName = null)
    {
        var switchingDevice = IsOpen && ActiveSerial != serial;
        ActiveSerial = serial;
        ActiveDeviceName = deviceName ?? serial;
        IsOpen = true;
        IsMinimized = false;
        StateChanged?.Invoke();
        if (switchingDevice)
            DeviceChanged?.Invoke(serial);
    }

    public void SwitchDevice(string serial, string? deviceName = null)
    {
        if (ActiveSerial == serial) return;
        ActiveSerial = serial;
        ActiveDeviceName = deviceName ?? serial;
        StateChanged?.Invoke();
        DeviceChanged?.Invoke(serial);
    }

    public void Minimize()
    {
        IsMinimized = true;
        StateChanged?.Invoke();
    }

    public void Restore()
    {
        IsMinimized = false;
        StateChanged?.Invoke();
    }

    public void Close()
    {
        IsOpen = false;
        IsMinimized = false;
        ActiveSerial = null;
        ActiveDeviceName = null;
        StateChanged?.Invoke();
    }
}
