namespace PlaNEvent.Client.Services;

public sealed class TimeZoneState
{
    public string SelectedTimeZoneId { get; private set; } = "America/New_York";

    public event Action? Changed;

    public void SetSelectedTimeZone(string? timeZoneId)
    {
        var next = string.IsNullOrWhiteSpace(timeZoneId) ? "America/New_York" : timeZoneId;
        if (string.Equals(SelectedTimeZoneId, next, StringComparison.Ordinal))
        {
            return;
        }

        SelectedTimeZoneId = next;
        Changed?.Invoke();
    }
}
