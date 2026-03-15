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

        Messages[^1] = last with
        {
            Text = $"{last.Text}{text}",
            IsHtml = false,
            IsThinking = false,
            IsStreaming = true,
            ShowLoadingTail = true
        };
        Changed?.Invoke();
    }

    public void SetLastAgentMessage(string text, bool isHtml = false)
    {
        SetLastAgentMessage(text, isHtml, false, false, false);
    }

    public void SetLastAgentThinking(string text)
    {
        SetLastAgentMessage(text, false, true, false, false);
    }

    public void SetLastAgentMessage(string text, bool isHtml, bool isThinking, bool isStreaming, bool showLoadingTail)
    {
        if (Messages.Count == 0)
        {
            Messages.Add(new ChatLine(false, text, isHtml, isThinking, isStreaming, showLoadingTail));
        }
        else
        {
            var last = Messages[^1];
            if (last.IsUser)
            {
                Messages.Add(new ChatLine(false, text, isHtml, isThinking, isStreaming, showLoadingTail));
            }
            else
            {
                Messages[^1] = last with
                {
                    Text = text,
                    IsHtml = isHtml,
                    IsThinking = isThinking,
                    IsStreaming = isStreaming,
                    ShowLoadingTail = showLoadingTail
                };
            }
        }

        Changed?.Invoke();
    }

    public ChatLine? GetLastMessage()
    {
        return Messages.Count == 0 ? null : Messages[^1];
    }

    public void ClearConversation()
    {
        SessionId = null;
        Messages.Clear();
        Changed?.Invoke();
    }

    public sealed record ChatLine(
        bool IsUser,
        string Text,
        bool IsHtml,
        bool IsThinking = false,
        bool IsStreaming = false,
        bool ShowLoadingTail = false);
}
