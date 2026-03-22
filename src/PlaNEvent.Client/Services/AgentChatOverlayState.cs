using PlaNEvent.Shared.Contracts;

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
            BeginAgentMessage(true);
        }

        var last = Messages[^1];
        if (last.IsUser)
        {
            BeginAgentMessage(true);
            last = Messages[^1];
        }

        Messages[^1] = last with
        {
            Text = $"{last.Text}{text}",
            IsHtml = true,
            IsThinking = false,
            IsStreaming = true,
            ShowLoadingTail = false
        };
        Changed?.Invoke();
    }

    public void SetLastAgentHtmlStream(string htmlText)
    {
        if (Messages.Count == 0)
        {
            Messages.Add(new ChatLine(false, htmlText, true, false, true, false));
        }
        else
        {
            var last = Messages[^1];
            if (last.IsUser)
            {
                Messages.Add(new ChatLine(false, htmlText, true, false, true, false));
            }
            else
            {
                Messages[^1] = last with
                {
                    Text = htmlText,
                    IsHtml = true,
                    IsThinking = false,
                    IsStreaming = true,
                    ShowLoadingTail = false
                };
            }
        }

        Changed?.Invoke();
    }

    public void SetLastAgentMessage(string text, bool isHtml = false)
    {
        SetLastAgentMessage(text, isHtml, false, false, false, null, null);
    }

    public void SetLastAgentThinking(string text)
    {
        SetLastAgentMessage(text, false, true, false, false, null, null);
    }

    public void SetLastAgentResponse(AgentChatResponse response)
    {
        var text = !string.IsNullOrWhiteSpace(response.HtmlReply) ? response.HtmlReply : response.Reply;
        var isHtml = !string.IsNullOrWhiteSpace(response.HtmlReply);
        SetLastAgentMessage(text, isHtml, false, false, false, response.Actions, response.Verification);
    }

    public void SetLastAgentMessage(
        string text,
        bool isHtml,
        bool isThinking,
        bool isStreaming,
        bool showLoadingTail,
        IReadOnlyList<AgentChatActionDto>? actions,
        AgentChatVerificationDto? verification)
    {
        if (Messages.Count == 0)
        {
            Messages.Add(new ChatLine(false, text, isHtml, isThinking, isStreaming, showLoadingTail, actions?.ToList() ?? new List<AgentChatActionDto>(), verification));
        }
        else
        {
            var last = Messages[^1];
            if (last.IsUser)
            {
                Messages.Add(new ChatLine(false, text, isHtml, isThinking, isStreaming, showLoadingTail, actions?.ToList() ?? new List<AgentChatActionDto>(), verification));
            }
            else
            {
                Messages[^1] = last with
                {
                    Text = text,
                    IsHtml = isHtml,
                    IsThinking = isThinking,
                    IsStreaming = isStreaming,
                    ShowLoadingTail = showLoadingTail,
                    Actions = actions?.ToList() ?? new List<AgentChatActionDto>(),
                    Verification = verification
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

    public void ResetSession()
    {
        SessionId = null;
        Changed?.Invoke();
    }

    public sealed record ChatLine(
        bool IsUser,
        string Text,
        bool IsHtml,
        bool IsThinking = false,
        bool IsStreaming = false,
        bool ShowLoadingTail = false,
        List<AgentChatActionDto>? Actions = null,
        AgentChatVerificationDto? Verification = null);
}
