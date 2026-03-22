using System.Net.Http.Headers;
using System.Net.Http.Json;
using PlaNEvent.Shared.Contracts;

namespace PlaNEvent.Client.Services;

public sealed class ApiClient(HttpClient httpClient, TokenAuthenticationStateProvider authStateProvider)
{
    public async Task<UserProfileDto?> MyProfileAsync()
    {
        await AttachTokenAsync();
        return await httpClient.GetFromJsonAsync<UserProfileDto>("api/account/profile");
    }

    public async Task<List<OccurrenceDto>> OccurrencesAsync(DateTime? startUtc = null, DateTime? endUtc = null)
    {
        await AttachTokenAsync();
        var query = "api/occurrences";
        if (startUtc.HasValue && endUtc.HasValue)
        {
            query += $"?startUtc={Uri.EscapeDataString(startUtc.Value.ToString("O"))}&endUtc={Uri.EscapeDataString(endUtc.Value.ToString("O"))}";
        }

        return await httpClient.GetFromJsonAsync<List<OccurrenceDto>>(query) ?? new List<OccurrenceDto>();
    }

    public async Task<List<EventGroupDto>> GroupsAsync()
    {
        await AttachTokenAsync();
        return await httpClient.GetFromJsonAsync<List<EventGroupDto>>("api/lookups/groups") ?? new List<EventGroupDto>();
    }

    public async Task<bool> CreateGroupAsync(EventGroupDto group)
    {
        await AttachTokenAsync();
        var response = await httpClient.PostAsJsonAsync("api/lookups/groups", group);
        return response.IsSuccessStatusCode;
    }

    public async Task<List<StaffDto>> StaffAsync()
    {
        await AttachTokenAsync();
        return await httpClient.GetFromJsonAsync<List<StaffDto>>("api/lookups/staff") ?? new List<StaffDto>();
    }

    public async Task<bool> CreateStaffAsync(StaffDto staff)
    {
        await AttachTokenAsync();
        var response = await httpClient.PostAsJsonAsync("api/lookups/staff", staff);
        return response.IsSuccessStatusCode;
    }

    public async Task<List<UserProfileDto>> AdminUsersAsync()
    {
        await AttachTokenAsync();
        return await httpClient.GetFromJsonAsync<List<UserProfileDto>>("api/admin/users") ?? new List<UserProfileDto>();
    }

    public async Task<(bool Success, string? Error)> AdminCreateUserAsync(AdminCreateUserRequest request)
    {
        await AttachTokenAsync();
        var response = await httpClient.PostAsJsonAsync("api/admin/users", request);
        if (response.IsSuccessStatusCode)
        {
            return (true, null);
        }

        var error = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(error))
        {
            error = $"Request failed with status {(int)response.StatusCode}.";
        }

        return (false, error);
    }

    public async Task<bool> SetUserStatusAsync(string id, bool isDisabled)
    {
        await AttachTokenAsync();
        var response = await httpClient.PutAsJsonAsync($"api/admin/users/{id}/status", new UpdateUserStatusRequest { IsDisabled = isDisabled });
        return response.IsSuccessStatusCode;
    }

    public async Task<List<OccurrenceDto>> PublicSalesAsync(string ownerSlug)
    {
        return await httpClient.GetFromJsonAsync<List<OccurrenceDto>>($"api/public/sales/{ownerSlug}") ?? new List<OccurrenceDto>();
    }

    public async Task<CalendarDashboardDto?> CalendarDashboardAsync(DateTime? startUtc = null, DateTime? endUtc = null)
    {
        await AttachTokenAsync();
        var query = "api/calendar-management/dashboard";
        if (startUtc.HasValue && endUtc.HasValue)
        {
            query += $"?startUtc={Uri.EscapeDataString(startUtc.Value.ToString("O"))}&endUtc={Uri.EscapeDataString(endUtc.Value.ToString("O"))}";
        }

        return await httpClient.GetFromJsonAsync<CalendarDashboardDto>(query);
    }

    public async Task<List<CategoryDto>> CategoriesAsync()
    {
        await AttachTokenAsync();
        return await httpClient.GetFromJsonAsync<List<CategoryDto>>("api/calendar-management/categories") ?? new List<CategoryDto>();
    }

    public async Task<CategoryDto?> SaveCategoryAsync(SaveCategoryRequest request)
    {
        await AttachTokenAsync();
        var response = await httpClient.PostAsJsonAsync("api/calendar-management/categories", request);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<CategoryDto>() : null;
    }

    public async Task<bool> DeleteCategoryAsync(int id)
    {
        await AttachTokenAsync();
        var response = await httpClient.DeleteAsync($"api/calendar-management/categories/{id}");
        return response.IsSuccessStatusCode;
    }

    public async Task<List<OfferingSummaryDto>> OfferingsSummaryAsync()
    {
        await AttachTokenAsync();
        return await httpClient.GetFromJsonAsync<List<OfferingSummaryDto>>("api/calendar-management/offerings") ?? new List<OfferingSummaryDto>();
    }

    public async Task<OfferingEditorDto?> OfferingAsync(int id)
    {
        await AttachTokenAsync();
        return await httpClient.GetFromJsonAsync<OfferingEditorDto>($"api/calendar-management/offerings/{id}");
    }

    public async Task<OfferingEditorDto?> SaveOfferingAsync(SaveOfferingRequest request)
    {
        await AttachTokenAsync();
        var response = await httpClient.PostAsJsonAsync("api/calendar-management/offerings", request);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<OfferingEditorDto>() : null;
    }

    public async Task<bool> DeleteOfferingAsync(int id)
    {
        await AttachTokenAsync();
        var response = await httpClient.DeleteAsync($"api/calendar-management/offerings/{id}");
        return response.IsSuccessStatusCode;
    }

    public async Task<List<ShowcasePageSummaryDto>> ShowcasePagesAsync()
    {
        await AttachTokenAsync();
        return await httpClient.GetFromJsonAsync<List<ShowcasePageSummaryDto>>("api/calendar-management/showcase-pages") ?? new List<ShowcasePageSummaryDto>();
    }

    public async Task<ShowcasePageEditorDto?> ShowcasePageAsync(int id)
    {
        await AttachTokenAsync();
        return await httpClient.GetFromJsonAsync<ShowcasePageEditorDto>($"api/calendar-management/showcase-pages/{id}");
    }

    public async Task<ShowcasePageEditorDto?> SaveShowcasePageAsync(SaveShowcasePageRequest request)
    {
        await AttachTokenAsync();
        var response = await httpClient.PostAsJsonAsync("api/calendar-management/showcase-pages", request);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<ShowcasePageEditorDto>() : null;
    }

    public async Task<bool> DeleteShowcasePageAsync(int id)
    {
        await AttachTokenAsync();
        var response = await httpClient.DeleteAsync($"api/calendar-management/showcase-pages/{id}");
        return response.IsSuccessStatusCode;
    }

    public async Task<PublicShowcaseDto?> PublicShowcaseAsync(string ownerSlug, string? pageSlug = null, string? sourceType = null, int? sourceId = null, int? offeringId = null, int? ruleGroupId = null, string? searchTerm = null)
    {
        var queryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(pageSlug))
        {
            queryParts.Add($"pageSlug={Uri.EscapeDataString(pageSlug)}");
        }

        if (!string.IsNullOrWhiteSpace(sourceType))
        {
            queryParts.Add($"sourceType={Uri.EscapeDataString(sourceType)}");
        }

        if (sourceId.HasValue)
        {
            queryParts.Add($"sourceId={sourceId.Value}");
        }

        if (offeringId.HasValue)
        {
            queryParts.Add($"offeringId={offeringId.Value}");
        }

        if (ruleGroupId.HasValue)
        {
            queryParts.Add($"ruleGroupId={ruleGroupId.Value}");
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            queryParts.Add($"searchTerm={Uri.EscapeDataString(searchTerm)}");
        }

        var query = queryParts.Count == 0 ? string.Empty : $"?{string.Join("&", queryParts)}";
        return await httpClient.GetFromJsonAsync<PublicShowcaseDto>($"api/public/showcase/{ownerSlug}{query}");
    }

    public async Task<List<BookingDto>> BookingsAsync()
    {
        await AttachTokenAsync();
        var response = await httpClient.GetAsync("api/bookings");
        if (!response.IsSuccessStatusCode)
        {
            return new List<BookingDto>();
        }

        return await response.Content.ReadFromJsonAsync<List<BookingDto>>() ?? new List<BookingDto>();
    }
    public async Task<AgentChatResponse?> AgentChatAsync(AgentChatRequest request)
    {
        await AttachTokenAsync();
        var response = await httpClient.PostAsJsonAsync("api/agent/chat", request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            return new AgentChatResponse
            {
                SessionId = request.SessionId ?? string.Empty,
                Reply = string.IsNullOrWhiteSpace(error)
                    ? $"Agent chat failed: {(int)response.StatusCode}"
                    : error
            };
        }

        return await response.Content.ReadFromJsonAsync<AgentChatResponse>();
    }

    public async Task<AgentChatResponse?> AgentChatActionAsync(AgentChatActionRequest request)
    {
        await AttachTokenAsync();
        var response = await httpClient.PostAsJsonAsync("api/agent/chat/actions/execute", request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            return new AgentChatResponse
            {
                SessionId = request.SessionId ?? string.Empty,
                Reply = string.IsNullOrWhiteSpace(error)
                    ? $"Agent action failed: {(int)response.StatusCode}"
                    : error
            };
        }

        return await response.Content.ReadFromJsonAsync<AgentChatResponse>();
    }

    private async Task AttachTokenAsync()
    {
        var token = await authStateProvider.GetTokenAsync();
        httpClient.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(token)
            ? null
            : new AuthenticationHeaderValue("Bearer", token);
    }
}
