using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace PlaNEvent.Api.Infrastructure;

public sealed class McpExamplesOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType?.Name != "McpController")
        {
            return;
        }

        switch (context.MethodInfo.Name)
        {
            case "JsonRpc":
                ApplyJsonRpcExamples(operation);
                break;
            case "CallToolByRoute":
                ApplyRouteToolExamples(operation);
                break;
            case "Tools":
                ApplyToolsExamples(operation);
                break;
        }
    }

    private static void ApplyJsonRpcExamples(OpenApiOperation operation)
    {
        if (operation.RequestBody?.Content.TryGetValue("application/json", out var mediaType) == true)
        {
            mediaType.Example = new OpenApiObject
            {
                ["jsonrpc"] = new OpenApiString("2.0"),
                ["id"] = new OpenApiInteger(1),
                ["method"] = new OpenApiString("tools/call"),
                ["params"] = new OpenApiObject
                {
                    ["name"] = new OpenApiString("get_countries"),
                    ["arguments"] = new OpenApiObject()
                }
            };
        }

        if (operation.Responses.TryGetValue("200", out var response)
            && response.Content.TryGetValue("application/json", out var responseContent))
        {
            responseContent.Example = new OpenApiObject
            {
                ["jsonrpc"] = new OpenApiString("2.0"),
                ["id"] = new OpenApiInteger(1),
                ["result"] = new OpenApiObject
                {
                    ["content"] = new OpenApiArray
                    {
                        new OpenApiObject
                        {
                            ["type"] = new OpenApiString("text"),
                            ["text"] = new OpenApiString("[{\"code\":\"US\",\"name\":\"United States\"}]")
                        }
                    }
                }
            };
        }
    }

    private static void ApplyRouteToolExamples(OpenApiOperation operation)
    {
        var nameParam = operation.Parameters.FirstOrDefault(x => x.Name == "name");
        if (nameParam is not null)
        {
            nameParam.Example = new OpenApiString("get_countries");
            nameParam.Description = "MCP tool name. Supported: get_countries, api_health.";
        }

        if (operation.Responses.TryGetValue("200", out var response)
            && response.Content.TryGetValue("application/json", out var responseContent))
        {
            responseContent.Example = new OpenApiObject
            {
                ["content"] = new OpenApiArray
                {
                    new OpenApiObject
                    {
                        ["type"] = new OpenApiString("text"),
                        ["text"] = new OpenApiString("{\"status\":\"ok\",\"timestampUtc\":\"2026-03-08T12:00:00Z\"}")
                    }
                }
            };
        }
    }

    private static void ApplyToolsExamples(OpenApiOperation operation)
    {
        if (operation.Responses.TryGetValue("200", out var response)
            && response.Content.TryGetValue("application/json", out var responseContent))
        {
            responseContent.Example = new OpenApiObject
            {
                ["tools"] = new OpenApiArray
                {
                    new OpenApiObject
                    {
                        ["name"] = new OpenApiString("get_countries"),
                        ["description"] = new OpenApiString("Fetch all countries from PlaNEvent public lookup API.")
                    },
                    new OpenApiObject
                    {
                        ["name"] = new OpenApiString("api_health"),
                        ["description"] = new OpenApiString("Check if PlaNEvent API is reachable.")
                    }
                }
            };
        }
    }
}
