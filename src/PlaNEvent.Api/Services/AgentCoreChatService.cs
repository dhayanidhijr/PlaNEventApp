using System.Net;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using Amazon.BedrockAgentCore;
using Amazon.BedrockAgentCore.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlaNEvent.Api.Data;
using PlaNEvent.Api.Models;
using PlaNEvent.Shared.Contracts;

namespace PlaNEvent.Api.Services;

public interface IAgentCoreChatService
{
    Task<AgentChatResponse> ChatAsync(AgentChatRequest request, CancellationToken cancellationToken);
    Task StreamChatAsync(AgentChatRequest request, HttpResponse response, CancellationToken cancellationToken);
    Task<AgentChatResponse> ExecuteActionAsync(AgentChatActionRequest request, CancellationToken cancellationToken);
}

public sealed class AgentCoreChatService(
    IAmazonBedrockAgentCore bedrockClient,
    IOptions<AgentCoreOptions> options,
    IHttpContextAccessor httpContextAccessor,
    AppDbContext dbContext,
    IOfferingScheduleService offeringScheduleService) : IAgentCoreChatService
{
    private readonly AgentCoreOptions optionsValue = options.Value;

    public async Task<AgentChatResponse> ChatAsync(AgentChatRequest request, CancellationToken cancellationToken)
    {
        var sessionId = NormalizeSessionId(request.SessionId);

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return new AgentChatResponse
            {
                SessionId = sessionId,
                Reply = "Please provide a message.",
                HtmlReply = "<p>Please provide a message.</p>"
            };
        }

        if (string.IsNullOrWhiteSpace(optionsValue.AgentRuntimeArn))
        {
            const string message = "AgentCore runtime ARN is not configured. Set AgentCore:AgentRuntimeArn in API settings.";
            return new AgentChatResponse
            {
                SessionId = sessionId,
                Reply = message,
                HtmlReply = $"<p>{WebUtility.HtmlEncode(message)}</p>"
            };
        }

        try
        {
            var runtimePrompt = await BuildRuntimePromptAsync(request.Message, cancellationToken);
            var firstPass = await InvokeAndExtractAsync(runtimePrompt, sessionId, cancellationToken);
            var html = string.IsNullOrWhiteSpace(firstPass.Reply) ? "<p><em>No response from agent.</em></p>" : firstPass.Reply.Trim();
            return await BuildChatResponseAsync(request, firstPass.SessionId, html, cancellationToken);
        }
        catch (Exception ex)
        {
            return ErrorResponse(sessionId, $"AgentCore call failed: {ex.Message}");
        }
    }

    public async Task StreamChatAsync(AgentChatRequest request, HttpResponse response, CancellationToken cancellationToken)
    {
        var sessionId = NormalizeSessionId(request.SessionId);

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Append("X-Accel-Buffering", "no");

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            await WriteSseEventAsync(response, "error", new
            {
                sessionId,
                message = "Please provide a message."
            }, cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(optionsValue.AgentRuntimeArn))
        {
            await WriteSseEventAsync(response, "error", new
            {
                sessionId,
                message = "AgentCore runtime ARN is not configured. Set AgentCore:AgentRuntimeArn in API settings."
            }, cancellationToken);
            return;
        }

        try
        {
            var runtimePrompt = await BuildRuntimePromptAsync(request.Message, cancellationToken);
            var streamResult = await StreamFromRuntimeAsync(runtimePrompt, sessionId, response, cancellationToken);
            var htmlReply = string.IsNullOrWhiteSpace(streamResult.Reply) ? "<p><em>No response from agent.</em></p>" : streamResult.Reply.Trim();
            var responsePayload = await BuildChatResponseAsync(request, streamResult.SessionId, htmlReply, cancellationToken);
            await WriteSseEventAsync(response, "complete", responsePayload, cancellationToken);
        }
        catch (Exception)
        {
            try
            {
                var runtimePrompt = await BuildRuntimePromptAsync(request.Message, cancellationToken);
                var result = await InvokeAndExtractAsync(runtimePrompt, sessionId, cancellationToken);
                var htmlReply = string.IsNullOrWhiteSpace(result.Reply) ? "<p><em>No response from agent.</em></p>" : result.Reply.Trim();
                var responsePayload = await BuildChatResponseAsync(request, result.SessionId, htmlReply, cancellationToken);

                await WriteSseEventAsync(response, "session", new { sessionId = result.SessionId }, cancellationToken);
                foreach (var chunk in ChunkTextForStream(htmlReply))
                {
                    await WriteSseEventAsync(response, "delta", new { sessionId = result.SessionId, delta = chunk, htmlReply = chunk }, cancellationToken);
                }

                await WriteSseEventAsync(response, "complete", responsePayload, cancellationToken);
            }
            catch (Exception ex)
            {
                await WriteSseEventAsync(response, "error", new
                {
                    sessionId,
                    message = $"AgentCore call failed: {ex.Message}"
                }, cancellationToken);
            }
        }
    }

    public async Task<AgentChatResponse> ExecuteActionAsync(AgentChatActionRequest request, CancellationToken cancellationToken)
    {
        var sessionId = NormalizeSessionId(request.SessionId);
        var ownerId = CurrentUserId();
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return ErrorResponse(sessionId, "Unable to determine the signed-in user for this action.");
        }

        if (!string.Equals(request.ActionType, "create_template", StringComparison.OrdinalIgnoreCase))
        {
            return ErrorResponse(sessionId, $"Unsupported agent action '{request.ActionType}'.");
        }

        try
        {
            return request.EntityType.ToLowerInvariant() switch
            {
                "offering" => await CreateOfferingFromTemplateAsync(sessionId, ownerId, request.TemplateKey, cancellationToken),
                "category" => await CreateCategoryFromTemplateAsync(sessionId, ownerId, request.TemplateKey, cancellationToken),
                "showcase_page" => await CreateShowcasePageFromTemplateAsync(sessionId, ownerId, request.TemplateKey, cancellationToken),
                _ => ErrorResponse(sessionId, $"Unsupported agent entity '{request.EntityType}'.")
            };
        }
        catch (Exception ex)
        {
            return ErrorResponse(sessionId, $"Action failed: {ex.Message}");
        }
    }

    private async Task<(string SessionId, string Reply)> InvokeAndExtractAsync(string prompt, string sessionId, CancellationToken cancellationToken)
    {
        var payloadJson = BuildPayloadJson(prompt, sessionId, stream: false);

        using var payloadStream = new MemoryStream(Encoding.UTF8.GetBytes(payloadJson));

        var invokeRequest = new InvokeAgentRuntimeRequest
        {
            AgentRuntimeArn = optionsValue.AgentRuntimeArn,
            Qualifier = optionsValue.Qualifier,
            ContentType = "application/json",
            Accept = "application/json",
            RuntimeSessionId = sessionId,
            Payload = payloadStream
        };

        var response = await bedrockClient.InvokeAgentRuntimeAsync(invokeRequest, cancellationToken);
        var responseText = await ReadResponseAsync(response);

        return (response.RuntimeSessionId ?? sessionId, ExtractReply(responseText));
    }

    private string BuildPayloadJson(string prompt, string sessionId, bool stream)
    {
        var bearerToken = GetForwardedAccessToken();
        return JsonSerializer.Serialize(new
        {
            sessionId,
            prompt,
            message = prompt,
            inputText = prompt,
            stream,
            accessToken = bearerToken,
            apiBaseUrl = optionsValue.ApiBaseUrl,
            swaggerUrl = optionsValue.SwaggerUrl,
            userContext = new
            {
                email = httpContextAccessor.HttpContext?.User?.Identity?.Name,
                roles = httpContextAccessor.HttpContext?.User?.Claims
                    .Where(claim => claim.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase) || claim.Type == "role")
                    .Select(claim => claim.Value)
                    .ToArray() ?? Array.Empty<string>()
            }
        });
    }

    private async Task<string> BuildRuntimePromptAsync(string prompt, CancellationToken cancellationToken)
    {
        var ownerId = CurrentUserId();
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return prompt;
        }

        var settings = await dbContext.SageGoalSettings
            .AsNoTracking()
            .Include(x => x.Features.OrderBy(f => f.SortOrder).ThenBy(f => f.Id))
            .FirstOrDefaultAsync(x => x.OwnerId == ownerId, cancellationToken);

        if (settings is null)
        {
            return prompt;
        }

        var summaryLines = new List<string>
        {
            "This facility has saved business guidance you should use when suggesting offerings, showcase pages, and promotional booking strategies.",
            $"Facility business summary: {settings.FacilityBusinessSummary}",
            $"Expected monthly booking count target: {settings.ExpectedMonthlyBookingCount}",
            $"Expected monthly sales amount target: {settings.ExpectedMonthlySalesAmount:0.##}"
        };

        if (settings.Features.Count > 0)
        {
            summaryLines.Add("Feature targets:");
            summaryLines.AddRange(settings.Features
                .OrderBy(x => x.SortOrder)
                .Select(x => $"- {x.Name}: target share {x.TargetSharePercent:0.##}%, expected bookings {x.ExpectedMonthlyBookingCount}, expected sales {x.ExpectedMonthlySalesAmount:0.##}"));
        }

        var facilityContext = string.Join("\n", summaryLines.Where(x => !string.IsNullOrWhiteSpace(x)));
        return $"""
            <system>
            {facilityContext}
            Use this context when you recommend or create showcase events so the plan helps meet or exceed the saved targets.
            When discussing facility strategy, tie your recommendations back to these goals explicitly.
            </system>

            {prompt}
            """;
    }

    private string? GetForwardedAccessToken()
    {
        if (!optionsValue.ForwardUserToken)
        {
            return null;
        }

        var authorization = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return null;
        }

        const string bearerPrefix = "Bearer ";
        return authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? authorization[bearerPrefix.Length..].Trim()
            : authorization.Trim();
    }

    private static async Task<string> ReadResponseAsync(InvokeAgentRuntimeResponse response)
    {
        if (response.Response is null)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(response.Response);
        return await reader.ReadToEndAsync();
    }

    private static string ExtractReply(string payload)
    {
        return ExtractReply(payload, 0);
    }

    private async Task<(string SessionId, string Reply)> StreamFromRuntimeAsync(
        string prompt,
        string sessionId,
        HttpResponse downstreamResponse,
        CancellationToken cancellationToken)
    {
        var payloadJson = BuildPayloadJson(prompt, sessionId, stream: true);
        using var payloadStream = new MemoryStream(Encoding.UTF8.GetBytes(payloadJson));
        var invokeRequest = new InvokeAgentRuntimeRequest
        {
            AgentRuntimeArn = optionsValue.AgentRuntimeArn,
            Qualifier = optionsValue.Qualifier,
            ContentType = "application/json",
            Accept = "text/event-stream",
            RuntimeSessionId = sessionId,
            Payload = payloadStream
        };

        var runtimeResponse = await bedrockClient.InvokeAgentRuntimeAsync(invokeRequest, cancellationToken);

        var actualSessionId = runtimeResponse.RuntimeSessionId ?? sessionId;
        await WriteSseEventAsync(downstreamResponse, "session", new { sessionId = actualSessionId }, cancellationToken);

        if (runtimeResponse.Response is null)
        {
            throw new InvalidOperationException("Runtime stream response was empty.");
        }

        await using var stream = runtimeResponse.Response;
        using var reader = new StreamReader(stream);

        var sseBuffer = new StringBuilder();
        var replyBuffer = new StringBuilder();

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;
            if (string.IsNullOrEmpty(line))
            {
                if (sseBuffer.Length == 0)
                {
                    continue;
                }

                var completion = await ProcessRuntimeStreamPayloadAsync(
                    sseBuffer.ToString(),
                    actualSessionId,
                    replyBuffer,
                    downstreamResponse,
                    cancellationToken);
                sseBuffer.Clear();
                if (completion is not null)
                {
                    return (actualSessionId, completion);
                }

                continue;
            }

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                sseBuffer.AppendLine(line["data:".Length..].Trim());
            }
            else
            {
                sseBuffer.AppendLine(line.Trim());
            }
        }

        if (sseBuffer.Length > 0)
        {
            var completion = await ProcessRuntimeStreamPayloadAsync(
                sseBuffer.ToString(),
                actualSessionId,
                replyBuffer,
                downstreamResponse,
                cancellationToken);
            if (completion is not null)
            {
                return (actualSessionId, completion);
            }
        }

        return (actualSessionId, replyBuffer.ToString().Trim());
    }

    private static string NormalizeSessionId(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return $"session{Guid.NewGuid():N}";
        }

        var normalized = sessionId.Trim();
        if (normalized.Length < 33)
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
            normalized += hash[..(33 - normalized.Length)];
        }

        if (normalized.Length > 100)
        {
            normalized = normalized[..100];
        }

        return normalized;
    }

    private static async Task<string?> ProcessRuntimeStreamPayloadAsync(
        string payload,
        string sessionId,
        StringBuilder replyBuffer,
        HttpResponse downstreamResponse,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        var streamedEvent = ParseRuntimeStreamEvent(payload, sessionId);
        switch (streamedEvent.Kind)
        {
            case "delta":
                if (!string.IsNullOrWhiteSpace(streamedEvent.Delta))
                {
                    replyBuffer.Append(streamedEvent.Delta);
                    await WriteSseEventAsync(downstreamResponse, "delta", new
                    {
                        sessionId,
                        delta = streamedEvent.Delta,
                        htmlReply = replyBuffer.ToString()
                    }, cancellationToken);
                }

                return null;
            case "complete":
                if (!string.IsNullOrWhiteSpace(streamedEvent.Reply))
                {
                    replyBuffer.Clear();
                    replyBuffer.Append(streamedEvent.Reply);
                }

                return replyBuffer.ToString().Trim();
            case "error":
                throw new InvalidOperationException(streamedEvent.Message ?? "Runtime stream failed.");
            default:
                return null;
        }
    }

    private static (string Kind, string? Delta, string? Reply, string? Message) ParseRuntimeStreamEvent(string payload, string sessionId)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var kind = root.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
            ? typeElement.GetString() ?? string.Empty
            : string.Empty;

        return kind switch
        {
            "session" => ("session", null, null, null),
            "delta" => ("delta", root.TryGetProperty("delta", out var delta) ? delta.GetString() : null, null, null),
            "complete" => ("complete", null, root.TryGetProperty("reply", out var reply) ? reply.GetString() : null, null),
            "error" => ("error", null, null, root.TryGetProperty("message", out var message) ? message.GetString() : "Runtime stream failed."),
            _ => ("delta", ExtractReply(payload), null, null)
        };
    }

    private static string ExtractReply(string payload, int depth)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        if (depth >= 4)
        {
            return payload.Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (TryGetString(root, "outputText", out var outputText)) return ExtractReply(outputText, depth + 1);
            if (TryGetString(root, "response", out var response)) return ExtractReply(response, depth + 1);
            if (TryGetString(root, "message", out var message)) return ExtractReply(message, depth + 1);
            if (TryGetString(root, "completion", out var completion)) return ExtractReply(completion, depth + 1);

            if (root.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.Array
                && content.GetArrayLength() > 0)
            {
                var first = content[0];
                if (TryGetString(first, "text", out var text)) return ExtractReply(text, depth + 1);
                if (first.ValueKind == JsonValueKind.String) return ExtractReply(first.GetString() ?? payload, depth + 1);
            }

            if (root.TryGetProperty("result", out var result))
            {
                if (TryGetString(result, "outputText", out var resultOutput)) return ExtractReply(resultOutput, depth + 1);
                if (TryGetString(result, "response", out var resultResponse)) return ExtractReply(resultResponse, depth + 1);
                if (TryGetString(result, "message", out var resultMessage)) return ExtractReply(resultMessage, depth + 1);

                if (result.TryGetProperty("content", out var resultContent)
                    && resultContent.ValueKind == JsonValueKind.Array
                    && resultContent.GetArrayLength() > 0)
                {
                    foreach (var item in resultContent.EnumerateArray())
                    {
                        if (TryGetString(item, "text", out var resultText)) return ExtractReply(resultText, depth + 1);
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            return ExtractReply(item.GetString() ?? payload, depth + 1);
                        }
                    }
                }
            }
        }
        catch
        {
            if (TryExtractPseudoJsonText(payload, out var pseudoJsonText))
            {
                return ExtractReply(pseudoJsonText, depth + 1);
            }
        }

        return payload.Trim();
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryExtractPseudoJsonText(string payload, out string value)
    {
        var matches = Regex.Matches(payload, @"['""]text['""]\s*:\s*(?<quote>['""])(?<value>(?:\\.|(?!\k<quote>).)*)\k<quote>");
        if (matches.Count == 0)
        {
            value = string.Empty;
            return false;
        }

        var texts = matches
            .Select(match => Regex.Unescape(match.Groups["value"].Value).Trim())
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        value = texts.Length == 0 ? string.Empty : string.Join("\n\n", texts);
        return texts.Length > 0;
    }

    private static async Task WriteSseEventAsync(HttpResponse response, string eventName, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        await response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private static IReadOnlyList<string> ChunkTextForStream(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var sentenceChunks = Regex.Split(text.Trim(), @"(?<=[.!?])\s+")
            .Where(chunk => !string.IsNullOrWhiteSpace(chunk))
            .ToArray();

        if (sentenceChunks.Length > 1)
        {
            return sentenceChunks
                .Select((chunk, index) => index < sentenceChunks.Length - 1 ? $"{chunk} " : chunk)
                .ToArray();
        }

        var words = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 6)
        {
            return new[] { text.Trim() };
        }

        var chunks = new List<string>();
        for (var i = 0; i < words.Length; i += 4)
        {
            var slice = words.Skip(i).Take(4).ToArray();
            var chunk = string.Join(' ', slice);
            if (i + 4 < words.Length)
            {
                chunk += " ";
            }

            chunks.Add(chunk);
        }

        return chunks;
    }

    private async Task<AgentChatResponse> BuildChatResponseAsync(
        AgentChatRequest request,
        string sessionId,
        string htmlReply,
        CancellationToken cancellationToken)
    {
        return new AgentChatResponse
        {
            SessionId = sessionId,
            Reply = htmlReply,
            HtmlReply = htmlReply,
            Actions = await BuildSuggestedActionsAsync(request.Message, cancellationToken)
        };
    }

    private async Task<List<AgentChatActionDto>> BuildSuggestedActionsAsync(string message, CancellationToken cancellationToken)
    {
        var userMessage = ExtractUserMessage(message);
        var normalized = userMessage.ToLowerInvariant();
        var actions = new List<AgentChatActionDto>();

        if (LooksLikeCreateRequest(normalized))
        {
            if (MentionsOffering(normalized))
            {
                actions.AddRange(new[]
                {
                    CreateTemplateAction("offering", "private-session", "Create Private Session", "Build a 1:1 weekday offering with a ready-to-book morning slot.", "primary"),
                    CreateTemplateAction("offering", "team-workshop", "Create Team Workshop", "Build a weekday group workshop with an evening schedule.", "outline-primary"),
                    CreateTemplateAction("offering", "weekend-bootcamp", "Create Weekend Bootcamp", "Build a weekend offering with a longer session block.", "outline-primary")
                });
            }
            else if (MentionsCategory(normalized))
            {
                actions.AddRange(new[]
                {
                    CreateTemplateAction("category", "fitness-classes", "Create Fitness Classes", "Create a top-level category for recurring class offerings.", "primary"),
                    CreateTemplateAction("category", "private-training", "Create Private Training", "Create a category for 1:1 coaching and appointments.", "outline-primary"),
                    CreateTemplateAction("category", "community-events", "Create Community Events", "Create a category for showcases, open houses, and special events.", "outline-primary")
                });
            }
            else if (MentionsShowcasePage(normalized))
            {
                actions.AddRange(new[]
                {
                    CreateTemplateAction("showcase_page", "home-booking-page", "Create Home Booking Page", "Create a homepage-style showcase page and set it as home.", "primary"),
                    CreateTemplateAction("showcase_page", "featured-programs", "Create Featured Programs Page", "Create a page focused on featured offerings or categories.", "outline-primary"),
                    CreateTemplateAction("showcase_page", "weekend-specials", "Create Weekend Specials Page", "Create a landing page for weekend-focused offerings.", "outline-primary")
                });
            }
        }

        var moduleAction = await BuildModuleActionAsync(normalized, cancellationToken);
        if (moduleAction is not null && actions.All(x => !string.Equals(x.NavigateUrl, moduleAction.NavigateUrl, StringComparison.OrdinalIgnoreCase)))
        {
            actions.Add(moduleAction);
        }

        if (actions.Count == 0)
        {
            actions.Add(NavigateAction("/calendar", "Verify In Calendar", "Open the calendar dashboard and validate the current application state."));
        }

        return actions;
    }

    private Task<AgentChatActionDto?> BuildModuleActionAsync(string normalized, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        AgentChatActionDto? action = null;
        if (MentionsShowcasePage(normalized))
        {
            action = NavigateAction("/showcase-pages", "Verify In Showcase Pages", "Open the showcase module and verify the related page data.");
        }
        else if (MentionsOffering(normalized))
        {
            action = NavigateAction("/offerings", "Verify In Offerings", "Open the offering wizard and verify the related offering details.");
        }
        else if (normalized.Contains("calendar", StringComparison.Ordinal))
        {
            action = NavigateAction("/calendar", "Verify In Calendar", "Open the calendar dashboard and verify the current schedule.");
        }
        else if (normalized.Contains("booking", StringComparison.Ordinal))
        {
            action = NavigateAction("/bookings", "Verify In Booking Management", "Open booking management and review the related activity.");
        }

        return Task.FromResult(action);
    }

    private async Task<AgentChatResponse> CreateOfferingFromTemplateAsync(
        string sessionId,
        string ownerId,
        string templateKey,
        CancellationToken cancellationToken)
    {
        var template = templateKey.ToLowerInvariant() switch
        {
            "private-session" => new
            {
                Name = "Private Session",
                Description = "One-on-one booking experience with a simple weekday schedule.",
                Color = "#ec3e47",
                RuleGroupName = "Weekday Availability",
                Weekdays = new[] { 1, 3, 5 },
                Start = new TimeSpan(9, 0, 0),
                End = new TimeSpan(10, 0, 0),
                RepeatSlots = false,
                RepeatEveryMinutes = (int?)null,
                RepeatUntil = (TimeSpan?)null
            },
            "team-workshop" => new
            {
                Name = "Team Workshop",
                Description = "A shared evening workshop designed for small groups and recurring cohorts.",
                Color = "#157f6b",
                RuleGroupName = "Evening Workshop Schedule",
                Weekdays = new[] { 2, 4 },
                Start = new TimeSpan(18, 0, 0),
                End = new TimeSpan(20, 0, 0),
                RepeatSlots = false,
                RepeatEveryMinutes = (int?)null,
                RepeatUntil = (TimeSpan?)null
            },
            "weekend-bootcamp" => new
            {
                Name = "Weekend Bootcamp",
                Description = "A longer-form weekend experience with one highlighted session block.",
                Color = "#2f80ff",
                RuleGroupName = "Saturday Sessions",
                Weekdays = new[] { 6 },
                Start = new TimeSpan(10, 0, 0),
                End = new TimeSpan(12, 0, 0),
                RepeatSlots = false,
                RepeatEveryMinutes = (int?)null,
                RepeatUntil = (TimeSpan?)null
            },
            _ => throw new InvalidOperationException($"Unknown offering template '{templateKey}'.")
        };

        var categoryId = await dbContext.Categories
            .Where(x => x.OwnerId == ownerId && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var offering = new Offering
        {
            OwnerId = ownerId,
            CategoryId = categoryId,
            Name = template.Name,
            Description = template.Description,
            Color = template.Color,
            IsActive = true,
            AllowNewBookings = true,
            RuleGroups =
            {
                new OfferingRuleGroup
                {
                    Name = template.RuleGroupName,
                    Color = template.Color,
                    StartDateUtc = DateTime.UtcNow.Date,
                    FrequencyType = "daysOfWeek",
                    WeekdaysCsv = string.Join(",", template.Weekdays),
                    Interval = 1,
                    Timeslots =
                    {
                        new OfferingTimeslot
                        {
                            StartTime = template.Start,
                            EndTime = template.End,
                            RepeatGeneratedSlots = template.RepeatSlots,
                            RepeatEveryMinutes = template.RepeatEveryMinutes,
                            RepeatUntilLastStartTime = template.RepeatUntil
                        }
                    }
                }
            }
        };

        dbContext.Offerings.Add(offering);
        await dbContext.SaveChangesAsync(cancellationToken);
        await offeringScheduleService.RebuildOccurrencesAsync(offering, cancellationToken);

        var refreshed = await dbContext.Offerings
            .AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.RuleGroups)
            .ThenInclude(x => x.Timeslots)
            .FirstAsync(x => x.Id == offering.Id, cancellationToken);

        var firstRuleGroup = refreshed.RuleGroups.OrderBy(x => x.Id).First();
        var firstTimeslot = firstRuleGroup.Timeslots.OrderBy(x => x.Id).First();
        var verification = new AgentChatVerificationDto
        {
            Title = "Offering Created",
            Summary = $"'{refreshed.Name}' is ready. You can verify the schedule details below and jump straight into the Offering Wizard.",
            NavigateLabel = "Open Offering",
            NavigateUrl = $"/offerings?id={refreshed.Id}",
            Details =
            {
                new AgentChatDetailDto { Label = "Name", Value = refreshed.Name },
                new AgentChatDetailDto { Label = "Category", Value = refreshed.Category?.Name ?? "Unassigned" },
                new AgentChatDetailDto { Label = "Rule Group", Value = firstRuleGroup.Name },
                new AgentChatDetailDto { Label = "Weekdays", Value = string.Join(", ", ParseWeekdayNames(firstRuleGroup.WeekdaysCsv)) },
                new AgentChatDetailDto { Label = "Timeslot", Value = $"{firstTimeslot.StartTime:hh\\:mm} - {firstTimeslot.EndTime:hh\\:mm}" },
                new AgentChatDetailDto { Label = "Published", Value = refreshed.IsPublished ? "Yes" : "No" }
            }
        };

        return CompleteActionResponse(
            sessionId,
            $"Offering '{refreshed.Name}' has been created successfully.",
            verification,
            "Open Offerings",
            "/offerings");
    }

    private async Task<AgentChatResponse> CreateCategoryFromTemplateAsync(
        string sessionId,
        string ownerId,
        string templateKey,
        CancellationToken cancellationToken)
    {
        var template = templateKey.ToLowerInvariant() switch
        {
            "fitness-classes" => new { Name = "Fitness Classes", Color = "#2f80ff", Description = "Best for recurring class-style offerings." },
            "private-training" => new { Name = "Private Training", Color = "#ec3e47", Description = "Best for 1:1 coaching and appointment-based services." },
            "community-events" => new { Name = "Community Events", Color = "#157f6b", Description = "Best for showcases, special events, and public sessions." },
            _ => throw new InvalidOperationException($"Unknown category template '{templateKey}'.")
        };

        var category = new Category
        {
            OwnerId = ownerId,
            Name = template.Name,
            Color = template.Color,
            IsActive = true
        };

        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);

        var verification = new AgentChatVerificationDto
        {
            Title = "Category Created",
            Summary = $"'{category.Name}' is now part of your calendar tree and can be used when you create offerings.",
            NavigateLabel = "Open Calendar",
            NavigateUrl = "/calendar",
            Details =
            {
                new AgentChatDetailDto { Label = "Name", Value = category.Name },
                new AgentChatDetailDto { Label = "Color", Value = category.Color },
                new AgentChatDetailDto { Label = "Status", Value = category.IsActive ? "Active" : "Inactive" },
                new AgentChatDetailDto { Label = "Suggested Use", Value = template.Description }
            }
        };

        return CompleteActionResponse(
            sessionId,
            $"Category '{category.Name}' has been created successfully.",
            verification,
            "Open Calendar",
            "/calendar");
    }

    private async Task<AgentChatResponse> CreateShowcasePageFromTemplateAsync(
        string sessionId,
        string ownerId,
        string templateKey,
        CancellationToken cancellationToken)
    {
        var template = templateKey.ToLowerInvariant() switch
        {
            "home-booking-page" => new { Name = "Home Booking Page", Slug = "home", IsHomePage = true, ItemName = "Browse Offerings", Description = "Use this as the public landing page for bookings." },
            "featured-programs" => new { Name = "Featured Programs", Slug = "featured-programs", IsHomePage = false, ItemName = "Featured Programs", Description = "Highlight your strongest offerings or categories in one place." },
            "weekend-specials" => new { Name = "Weekend Specials", Slug = "weekend-specials", IsHomePage = false, ItemName = "Weekend Specials", Description = "Promote weekend-focused classes and events." },
            _ => throw new InvalidOperationException($"Unknown showcase page template '{templateKey}'.")
        };

        var page = new ShowcasePage
        {
            OwnerId = ownerId,
            Name = template.Name,
            Slug = await EnsureUniqueShowcaseSlugAsync(ownerId, Slugify(template.Slug, template.Name), cancellationToken),
            IsActive = true,
            IsHomePage = template.IsHomePage
        };

        var categorySource = await dbContext.Categories
            .Where(x => x.OwnerId == ownerId && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name })
            .FirstOrDefaultAsync(cancellationToken);

        var offeringSource = await dbContext.Offerings
            .Where(x => x.OwnerId == ownerId && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (offeringSource is not null || categorySource is not null)
        {
            var sourceType = offeringSource is not null ? "offering" : "category";
            var sourceId = offeringSource?.Id ?? categorySource!.Id;
            page.Items.Add(new ShowcasePageItem
            {
                Name = template.ItemName,
                SourceType = sourceType,
                SourceId = sourceId,
                CarouselType = "carousel",
                Description = template.Description,
                ShowDescription = true,
                IsActive = true,
                SortOrder = 0
            });
        }

        if (page.IsHomePage)
        {
            var existingHomePages = await dbContext.ShowcasePages
                .Where(x => x.OwnerId == ownerId && x.IsHomePage)
                .ToListAsync(cancellationToken);
            foreach (var existing in existingHomePages)
            {
                existing.IsHomePage = false;
            }
        }

        dbContext.ShowcasePages.Add(page);
        await dbContext.SaveChangesAsync(cancellationToken);

        var publicSlug = await dbContext.Users
            .Where(x => x.Id == ownerId)
            .Select(x => x.PublicSlug)
            .FirstOrDefaultAsync(cancellationToken) ?? "admin";

        var publicPreviewUrl = $"/showcase/{publicSlug}?pageSlug={Uri.EscapeDataString(page.Slug)}";
        var verification = new AgentChatVerificationDto
        {
            Title = "Showcase Page Created",
            Summary = $"'{page.Name}' is saved and ready for editorial review. You can verify the record details below and then open the editing module.",
            NavigateLabel = "Open Showcase Page",
            NavigateUrl = $"/showcase-pages?id={page.Id}",
            Details =
            {
                new AgentChatDetailDto { Label = "Name", Value = page.Name },
                new AgentChatDetailDto { Label = "Slug", Value = page.Slug },
                new AgentChatDetailDto { Label = "Home Page", Value = page.IsHomePage ? "Yes" : "No" },
                new AgentChatDetailDto { Label = "Active", Value = page.IsActive ? "Yes" : "No" },
                new AgentChatDetailDto { Label = "Public Preview", Value = publicPreviewUrl }
            }
        };

        return new AgentChatResponse
        {
            SessionId = sessionId,
            Reply = $"Showcase page '{page.Name}' has been created successfully.",
            HtmlReply = $"<div><p><strong>Showcase page created.</strong></p><p>{WebUtility.HtmlEncode(page.Name)} is ready. Use the verify button to inspect the saved details or open the public preview.</p></div>",
            ActionCompleted = true,
            Verification = verification,
            Actions =
            {
                VerifyAction("Verify Created Page", verification),
                NavigateAction(publicPreviewUrl, "Preview Public Page", "Open the customer-facing version of the new showcase page.", "outline-primary"),
                NavigateAction($"/showcase-pages?id={page.Id}", "Open Showcase Pages", "Jump into the showcase admin module for this page.")
            }
        };
    }

    private static AgentChatResponse CompleteActionResponse(
        string sessionId,
        string plainTextMessage,
        AgentChatVerificationDto verification,
        string moduleLabel,
        string moduleUrl)
    {
        return new AgentChatResponse
        {
            SessionId = sessionId,
            Reply = plainTextMessage,
            HtmlReply = $"<div><p><strong>Action completed.</strong></p><p>{WebUtility.HtmlEncode(plainTextMessage)} Use the verify button below to review the created record.</p></div>",
            ActionCompleted = true,
            Verification = verification,
            Actions =
            {
                VerifyAction($"Verify {verification.Title}", verification),
                NavigateAction(moduleUrl, moduleLabel, "Open the related module and inspect the record in context.")
            }
        };
    }

    private static AgentChatResponse ErrorResponse(string sessionId, string message)
    {
        return new AgentChatResponse
        {
            SessionId = sessionId,
            Reply = message,
            HtmlReply = $"<p>{WebUtility.HtmlEncode(message)}</p>"
        };
    }

    private static AgentChatActionDto CreateTemplateAction(
        string entityType,
        string templateKey,
        string label,
        string description,
        string style)
    {
        return new AgentChatActionDto
        {
            ActionType = "create_template",
            EntityType = entityType,
            TemplateKey = templateKey,
            Label = label,
            Description = description,
            Style = style,
            RequiresExecution = true
        };
    }

    private static AgentChatActionDto NavigateAction(string navigateUrl, string label, string description, string style = "secondary")
    {
        return new AgentChatActionDto
        {
            ActionType = "open_module",
            Label = label,
            Description = description,
            Style = style,
            NavigateUrl = navigateUrl
        };
    }

    private static AgentChatActionDto VerifyAction(string label, AgentChatVerificationDto verification)
    {
        return new AgentChatActionDto
        {
            ActionType = "show_verification",
            Label = label,
            Description = "Open a quick verification dialog with the saved record details.",
            Style = "success",
            Verification = verification
        };
    }

    private static string ExtractUserMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var closingTagIndex = message.LastIndexOf("</system>", StringComparison.OrdinalIgnoreCase);
        return closingTagIndex >= 0
            ? message[(closingTagIndex + "</system>".Length)..].Trim()
            : message.Trim();
    }

    private static bool LooksLikeCreateRequest(string normalized)
        => normalized.Contains("create", StringComparison.Ordinal)
            || normalized.Contains("new ", StringComparison.Ordinal)
            || normalized.Contains("add ", StringComparison.Ordinal)
            || normalized.Contains("set up", StringComparison.Ordinal)
            || normalized.Contains("setup", StringComparison.Ordinal);

    private static bool MentionsOffering(string normalized)
        => normalized.Contains("offering", StringComparison.Ordinal)
            || normalized.Contains("class", StringComparison.Ordinal)
            || normalized.Contains("session", StringComparison.Ordinal);

    private static bool MentionsCategory(string normalized)
        => normalized.Contains("category", StringComparison.Ordinal)
            || normalized.Contains("tree", StringComparison.Ordinal);

    private static bool MentionsShowcasePage(string normalized)
        => normalized.Contains("showcase", StringComparison.Ordinal)
            || normalized.Contains("booking page", StringComparison.Ordinal)
            || normalized.Contains("public page", StringComparison.Ordinal)
            || normalized.Contains("page", StringComparison.Ordinal) && normalized.Contains("slug", StringComparison.Ordinal);

    private string CurrentUserId()
        => httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContextAccessor.HttpContext?.User.FindFirstValue("sub")
            ?? string.Empty;

    private async Task<string> EnsureUniqueShowcaseSlugAsync(string ownerId, string baseSlug, CancellationToken cancellationToken)
    {
        var slug = string.IsNullOrWhiteSpace(baseSlug) ? Guid.NewGuid().ToString("N")[..8] : baseSlug;
        var counter = 2;
        while (await dbContext.ShowcasePages.AnyAsync(x => x.OwnerId == ownerId && x.Slug == slug, cancellationToken))
        {
            slug = $"{baseSlug}-{counter++}";
        }

        return slug;
    }

    private static string Slugify(string slug, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(slug) ? fallback : slug;
        var clean = string.Concat(value.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-'));
        clean = string.Join('-', clean.Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(clean) ? Guid.NewGuid().ToString("N")[..8] : clean;
    }

    private static IReadOnlyList<string> ParseWeekdayNames(string csv)
    {
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value, out var parsed) ? parsed : -1)
            .Where(value => value >= 0 && value <= 6)
            .Select(value => ((DayOfWeek)value).ToString()[..3])
            .ToList();
    }
}
