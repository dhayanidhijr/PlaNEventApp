# PlaNEvent MCP Server

MCP stdio server that exposes PlaNEvent API tools.

## Tools

- `get_countries`: calls `GET /api/lookups/countries`
- `api_health`: checks `GET /swagger/index.html`

## Environment variables

- `PLANEVENT_API_BASE_URL` (optional): API base URL. Default: `https://planevent.dawindemoproductsdemo.com`
- `PLANEVENT_API_TOKEN` (optional): bearer token for protected endpoints

## Run

```bash
dotnet run --project src/PlaNEvent.McpServer/PlaNEvent.McpServer.csproj
```

## MCP client example

```json
{
  "mcpServers": {
    "planevent": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "src/PlaNEvent.McpServer/PlaNEvent.McpServer.csproj"
      ],
      "env": {
        "PLANEVENT_API_BASE_URL": "https://planevent.dawindemoproductsdemo.com"
      }
    }
  }
}
```
