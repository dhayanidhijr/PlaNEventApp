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

    public void BeginAgentMessage(bool isHtml = false)
    {
        Messages.Add(new ChatLine(false, string.Empty, isHtml));
        Changed?.Invoke();
    }

    public void AppendToLastAgentMessage(string text)
    {
        if (Messages.Count == 0)
        {
            BeginAgentMessage();
        }

        var last = Messages[^1];
        if (last.IsUser)
        {
            BeginAgentMessage();
            last = Messages[^1];
        }

        Messages[^1] = last with { Text = $"{last.Text}{text}", IsHtml = false };
        Changed?.Invoke();
    }

    public void SetLastAgentMessage(string text, bool isHtml = false)
    {
        if (Messages.Count == 0)
        {
            Messages.Add(new ChatLine(false, text, isHtml));
        }
        else
        {
            var last = Messages[^1];
            if (last.IsUser)
            {
                Messages.Add(new ChatLine(false, text, isHtml));
            }
            else
            {
                Messages[^1] = last with { Text = text, IsHtml = isHtml };
            }
        }

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
