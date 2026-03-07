using System.Net.Http.Json;
using PlaNEvent.Shared.Contracts;

namespace PlaNEvent.Client.Services;

public sealed class BookingService(HttpClient httpClient, TokenAuthenticationStateProvider authStateProvider)
{
    public async Task<bool> BookAsync(int occurrenceId, int slotId, string notes)
    {
        var token = await authStateProvider.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await httpClient.PostAsJsonAsync("api/bookings", new BookingRequest
        {
            OccurrenceId = occurrenceId,
            OccurrenceSlotId = slotId,
            CustomerNotes = notes
        });

        return response.IsSuccessStatusCode;
    }
}
