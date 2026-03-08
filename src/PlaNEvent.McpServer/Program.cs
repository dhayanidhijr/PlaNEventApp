using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var apiBaseUrl = Environment.GetEnvironmentVariable("PLANEVENT_API_BASE_URL")
    ?? "https://planevent.dawindemoproductsdemo.com";
var apiToken = Environment.GetEnvironmentVariable("PLANEVENT_API_TOKEN");

using var httpClient = new HttpClient
{
    BaseAddress = new Uri(apiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute)
};

if (!string.IsNullOrWhiteSpace(apiToken))
{
    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
}

var server = new McpServer(Console.OpenStandardInput(), Console.OpenStandardOutput(), httpClient);
await server.RunAsync();

internal sealed class McpServer(Stream input, Stream output, HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var request = await ReadMessageAsync(cancellationToken);
            if (request is null)
            {
                return;
            }

            await HandleRequestAsync(request, cancellationToken);
        }
    }

    private async Task HandleRequestAsync(JsonNode request, CancellationToken cancellationToken)
    {
        var method = request["method"]?.GetValue<string>();
        var id = request["id"];

        if (string.IsNullOrWhiteSpace(method))
        {
            if (id is not null)
            {
                await WriteErrorAsync(id, -32600, "Invalid Request", cancellationToken);
            }

            return;
        }

        switch (method)
        {
            case "initialize":
                await WriteResultAsync(id, new JsonObject
                {
                    ["protocolVersion"] = "2024-11-05",
                    ["serverInfo"] = new JsonObject
                    {
                        ["name"] = "PlaNEvent MCP Server",
                        ["version"] = "1.0.0"
                    },
                    ["capabilities"] = new JsonObject
                    {
                        ["tools"] = new JsonObject()
                    }
                }, cancellationToken);
                return;
            case "notifications/initialized":
                return;
            case "tools/list":
                await WriteResultAsync(id, BuildToolsList(), cancellationToken);
                return;
            case "tools/call":
                await HandleToolCallAsync(id, request["params"], cancellationToken);
                return;
            case "ping":
                await WriteResultAsync(id, new JsonObject(), cancellationToken);
                return;
            default:
                if (id is not null)
                {
                    await WriteErrorAsync(id, -32601, $"Method not found: {method}", cancellationToken);
                }
                return;
        }
    }

    private static JsonObject BuildToolsList()
    {
        return new JsonObject
        {
            ["tools"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "get_countries",
                    ["description"] = "Fetch all countries from PlaNEvent public lookup API.",
                    ["inputSchema"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject(),
                        ["additionalProperties"] = false
                    }
                },
                new JsonObject
                {
                    ["name"] = "api_health",
                    ["description"] = "Check if PlaNEvent API is reachable.",
                    ["inputSchema"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject(),
                        ["additionalProperties"] = false
                    }
                }
            }
        };
    }

    private async Task HandleToolCallAsync(JsonNode? id, JsonNode? requestParams, CancellationToken cancellationToken)
    {
        var toolName = requestParams?["name"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(toolName))
        {
            await WriteErrorAsync(id, -32602, "Invalid params: tool name is required.", cancellationToken);
            return;
        }

        try
        {
            var content = toolName switch
            {
                "get_countries" => await GetCountriesAsync(cancellationToken),
                "api_health" => await GetApiHealthAsync(cancellationToken),
                _ => null
            };

            if (content is null)
            {
                await WriteErrorAsync(id, -32602, $"Unknown tool: {toolName}", cancellationToken);
                return;
            }

            await WriteResultAsync(id, new JsonObject
            {
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = content
                    }
                }
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            await WriteErrorAsync(id, -32000, $"Tool execution failed: {ex.Message}", cancellationToken);
        }
    }

    private async Task<string> GetCountriesAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("api/lookups/countries", cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return $"Request failed: {(int)response.StatusCode} {response.ReasonPhrase}\n{payload}";
        }

        return payload;
    }

    private async Task<string> GetApiHealthAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("swagger/index.html", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var snippet = body.Length > 200 ? body[..200] : body;
        return $"Status: {(int)response.StatusCode} {response.ReasonPhrase}\nBodySnippet: {snippet}";
    }

    private async Task<JsonNode?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lineBuffer = new List<byte>(128);

        while (true)
        {
            lineBuffer.Clear();
            while (true)
            {
                var b = await ReadByteAsync(input, cancellationToken);
                if (b == -1)
                {
                    return null;
                }

                if (b == '\n')
                {
                    break;
                }

                lineBuffer.Add((byte)b);
            }

            var line = Encoding.ASCII.GetString(lineBuffer.ToArray()).TrimEnd('\r');
            if (line.Length == 0)
            {
                break;
            }

            var splitIndex = line.IndexOf(':');
            if (splitIndex <= 0 || splitIndex == line.Length - 1)
            {
                continue;
            }

            var key = line[..splitIndex].Trim();
            var value = line[(splitIndex + 1)..].Trim();
            headers[key] = value;
        }

        if (!headers.TryGetValue("Content-Length", out var contentLengthRaw)
            || !int.TryParse(contentLengthRaw, out var contentLength)
            || contentLength <= 0)
        {
            return null;
        }

        var payloadBuffer = new byte[contentLength];
        var readCount = 0;
        while (readCount < contentLength)
        {
            var read = await input.ReadAsync(payloadBuffer.AsMemory(readCount, contentLength - readCount), cancellationToken);
            if (read == 0)
            {
                return null;
            }

            readCount += read;
        }

        var payloadJson = Encoding.UTF8.GetString(payloadBuffer);
        return JsonNode.Parse(payloadJson);
    }

    private async Task WriteResultAsync(JsonNode? id, JsonNode result, CancellationToken cancellationToken)
    {
        if (id is null)
        {
            return;
        }

        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
            ["result"] = result
        };

        await WriteMessageAsync(response, cancellationToken);
    }

    private async Task WriteErrorAsync(JsonNode? id, int code, string message, CancellationToken cancellationToken)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };

        await WriteMessageAsync(response, cancellationToken);
    }

    private async Task WriteMessageAsync(JsonNode payload, CancellationToken cancellationToken)
    {
        var json = payload.ToJsonString(JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        var headers = $"Content-Length: {bytes.Length}\r\nContent-Type: application/json\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(headers);

        await output.WriteAsync(headerBytes.AsMemory(0, headerBytes.Length), cancellationToken);
        await output.WriteAsync(bytes.AsMemory(0, bytes.Length), cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static async Task<int> ReadByteAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        var read = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
        return read == 0 ? -1 : buffer[0];
    }
}
