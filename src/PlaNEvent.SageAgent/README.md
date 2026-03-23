# PlaNEvent Sage AI

This project packages a Strands-based Python agent for Amazon Bedrock AgentCore Runtime. The agent loads PlaNEvent operations from Swagger, exposes them as tools, and uses a PlaNEvent bearer token to call secured APIs on behalf of the signed-in user.

## What it does

- Fetches the PlaNEvent OpenAPI document from `https://planevent.dawindemoproductsdemo.com/swagger/v1/swagger.json`
- Builds one Strands tool per PlaNEvent API operation
- Uses the forwarded JWT bearer token from the caller to access secured endpoints
- Runs behind Amazon Bedrock AgentCore Runtime using the official SDK entrypoint pattern

## Project layout

- `main.py` : runtime entrypoint used by AgentCore
- `planevent_sage_agent/app.py` : Strands agent orchestration and runtime payload handling
- `planevent_sage_agent/openapi_tools.py` : Swagger parser and authenticated dynamic tool factory

## Environment variables

- `AWS_REGION` or `AWS_DEFAULT_REGION`
- `SAGE_MODEL_ID`
- `PLANEVENT_API_BASE_URL`
- `PLANEVENT_SWAGGER_URL`
- `PLANEVENT_SYSTEM_TOKEN` : optional fallback token when no user token is forwarded

Suggested values:

```bash
export AWS_REGION=us-east-1
export SAGE_MODEL_ID=us.anthropic.claude-3-5-haiku-20241022-v1:0
export PLANEVENT_API_BASE_URL=https://planevent.dawindemoproductsdemo.com
export PLANEVENT_SWAGGER_URL=https://planevent.dawindemoproductsdemo.com/swagger/v1/swagger.json
```

## Local run

```bash
python -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
python main.py
```

Test locally:

```bash
curl -X POST http://localhost:8080/invocations \
  -H "Content-Type: application/json" \
  -d '{"prompt":"List my upcoming bookings","accessToken":"<jwt>"}'
```

## Deploy to Amazon Bedrock AgentCore Runtime

This project follows the official Strands + AgentCore SDK integration pattern:

```bash
pip install bedrock-agentcore-starter-toolkit
agentcore configure --entrypoint main.py --non-interactive
agentcore launch
agentcore deploy
```

The deployed runtime should receive payloads shaped like:

```json
{
  "prompt": "Create a staff member named Robin",
  "accessToken": "<planevent-jwt>",
  "apiBaseUrl": "https://planevent.dawindemoproductsdemo.com",
  "swaggerUrl": "https://planevent.dawindemoproductsdemo.com/swagger/v1/swagger.json"
}
```
