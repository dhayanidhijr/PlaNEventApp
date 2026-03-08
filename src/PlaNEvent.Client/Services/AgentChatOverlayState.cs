namespace PlaNEvent.Client.Services;

public sealed class AgentChatOverlayState
{
    public bool IsOpen { get; private set; }

    public event Action? Changed;

    public void Open()
    {
        IsOpen = true;
        Changed?.Invoke();
    }

    public void Close()
    {
        IsOpen = false;
        Changed?.Invoke();
    }
}
