using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaNEvent.Shared.Contracts;

namespace PlaNEvent.Api.Controllers;

[ApiController]
[Route("api/mcp")]
[AllowAnonymous]
public sealed class McpController : ControllerBase
{
    [HttpGet("tools")]
    public IActionResult Tools()
    {
        return Ok(new
        {
            tools = BuildToolsList()
        });
    }

    [HttpPost("tools/{name}")]
    public IActionResult CallToolByRoute(string name)
    {
        return Ok(new
        {
            content = new[]
            {
                new
                {
                    type = "text",
                    text = RunTool(name)
                }
            }
        });
    }

    [HttpPost]
    public IActionResult JsonRpc([FromBody] McpJsonRpcRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Method))
        {
            return Ok(ErrorResponse(request.Id, -32600, "Invalid Request"));
        }

        return request.Method switch
        {
            "initialize" => Ok(SuccessResponse(request.Id, new
            {
                protocolVersion = "2024-11-05",
                serverInfo = new
                {
                    name = "PlaNEvent MCP HTTP API",
                    version = "1.0.0"
                },
                capabilities = new
                {
                    tools = new { }
                }
            })),
            "ping" => Ok(SuccessResponse(request.Id, new { })),
            "tools/list" => Ok(SuccessResponse(request.Id, new { tools = BuildToolsList() })),
            "tools/call" => Ok(HandleToolCall(request.Id, request.Params)),
            "notifications/initialized" => Ok(),
            _ => Ok(ErrorResponse(request.Id, -32601, $"Method not found: {request.Method}"))
        };
    }

    private object HandleToolCall(JsonElement? id, JsonElement? requestParams)
    {
        if (!requestParams.HasValue
            || requestParams.Value.ValueKind != JsonValueKind.Object
            || !requestParams.Value.TryGetProperty("name", out var nameProperty)
            || nameProperty.ValueKind != JsonValueKind.String)
        {
            return ErrorResponse(id, -32602, "Invalid params: tool name is required.");
        }

        var toolName = nameProperty.GetString();
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return ErrorResponse(id, -32602, "Invalid params: tool name is required.");
        }

        try
        {
            var result = RunTool(toolName);
            return SuccessResponse(id, new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = result
                    }
                }
            });
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResponse(id, -32602, ex.Message);
        }
        catch (Exception ex)
        {
            return ErrorResponse(id, -32000, $"Tool execution failed: {ex.Message}");
        }
    }

    private static object[] BuildToolsList()
    {
        return
        [
            new
            {
                name = "get_countries",
                description = "Fetch all countries from PlaNEvent public lookup API.",
                inputSchema = new
                {
                    type = "object",
                    properties = new { },
                    additionalProperties = false
                }
            },
            new
            {
                name = "api_health",
                description = "Check if PlaNEvent API is reachable.",
                inputSchema = new
                {
                    type = "object",
                    properties = new { },
                    additionalProperties = false
                }
            }
        ];
    }

    private static string RunTool(string name)
    {
        return name switch
        {
            "get_countries" => JsonSerializer.Serialize(GetCountries()),
            "api_health" => JsonSerializer.Serialize(new
            {
                status = "ok",
                timestampUtc = DateTime.UtcNow
            }),
            _ => throw new InvalidOperationException($"Unknown tool: {name}")
        };
    }

    private static IReadOnlyCollection<CountryDto> GetCountries()
    {
        return CultureInfo
            .GetCultures(CultureTypes.SpecificCultures)
            .Select(culture =>
            {
                try
                {
                    var region = new RegionInfo(culture.Name);
                    return new CountryDto { Code = region.TwoLetterISORegionName, Name = region.EnglishName };
                }
                catch
                {
                    return null;
                }
            })
            .Where(country => country is not null)
            .DistinctBy(country => country!.Code)
            .Select(country => country!)
            .Where(country => country.Code.Length == 2 && country.Code.All(char.IsLetter))
            .OrderBy(country => country.Name)
            .ToList();
    }

    private static object SuccessResponse(JsonElement? id, object result)
    {
        return new
        {
            jsonrpc = "2.0",
            id = ToJsonCompatibleId(id),
            result
        };
    }

    private static object ErrorResponse(JsonElement? id, int code, string message)
    {
        return new
        {
            jsonrpc = "2.0",
            id = ToJsonCompatibleId(id),
            error = new
            {
                code,
                message
            }
        };
    }

    private static object? ToJsonCompatibleId(JsonElement? id)
    {
        if (!id.HasValue || id.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        return JsonSerializer.Deserialize<object>(id.Value.GetRawText());
    }

    public sealed class McpJsonRpcRequest
    {
        public string Jsonrpc { get; set; } = "2.0";
        public JsonElement? Id { get; set; }
        public string Method { get; set; } = string.Empty;
        public JsonElement? Params { get; set; }
    }
}
