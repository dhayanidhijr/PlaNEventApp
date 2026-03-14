namespace PlaNEvent.Client.Services;

public sealed class AgentChatOverlayState
{
    public bool IsOpen { get; private set; }
    public string? SessionId { get; set; }
    public List<ChatLine> Messages { get; } = new();

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

    public void AddMessage(ChatLine message)
    {
        Messages.Add(message);
        Changed?.Invoke();
    }

    public void ClearConversation()
    {
        SessionId = null;
        Messages.Clear();
        Changed?.Invoke();
    }

    public sealed record ChatLine(bool IsUser, string Text, bool IsHtml);
}
