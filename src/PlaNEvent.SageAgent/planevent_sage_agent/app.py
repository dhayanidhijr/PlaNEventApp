from __future__ import annotations

import os
import re
from typing import Any

from bedrock_agentcore.runtime import BedrockAgentCoreApp
from strands import Agent
from strands.models import BedrockModel

from planevent_sage_agent.openapi_tools import build_api_tools


DEFAULT_API_BASE_URL = "https://planevent.dawindemoproductsdemo.com"
DEFAULT_SWAGGER_URL = f"{DEFAULT_API_BASE_URL}/swagger/v1/swagger.json"
DEFAULT_MODEL_ID = "us.anthropic.claude-sonnet-4-20250514-v1:0"
SESSION_HISTORY: dict[str, list[dict[str, str]]] = {}
MAX_HISTORY_ITEMS = 8


app = BedrockAgentCoreApp()


@app.entrypoint
def invoke(payload: dict[str, Any]) -> dict[str, Any]:
    session_id = str(payload.get("sessionId") or "default")
    prompt = (
        payload.get("prompt")
        or payload.get("message")
        or payload.get("inputText")
        or "Introduce yourself as Sage AI for PlaNEvent."
    )

    access_token = payload.get("accessToken") or os.getenv("PLANEVENT_SYSTEM_TOKEN")
    api_base_url = payload.get("apiBaseUrl") or os.getenv("PLANEVENT_API_BASE_URL", DEFAULT_API_BASE_URL)
    swagger_url = payload.get("swaggerUrl") or os.getenv("PLANEVENT_SWAGGER_URL", DEFAULT_SWAGGER_URL)
    region_name = os.getenv("AWS_REGION") or os.getenv("AWS_DEFAULT_REGION") or "us-east-1"
    model_id = os.getenv("SAGE_MODEL_ID", DEFAULT_MODEL_ID)

    tools = build_api_tools(api_base_url, swagger_url, access_token)
    agent = Agent(
        model=BedrockModel(model_id=model_id, region_name=region_name, temperature=0.1),
        tools=tools,
        system_prompt=build_system_prompt(api_base_url, swagger_url, has_token=bool(access_token)),
    )

    prompt_with_context = build_prompt_with_history(prompt, session_id)
    result = agent(prompt_with_context)
    response_text = stringify_result(result)
    update_history(session_id, prompt, response_text)
    return {
        "result": {
            "role": "assistant",
            "content": [
                {
                    "text": response_text,
                }
            ],
        }
    }


def build_prompt_with_history(prompt: str, session_id: str) -> str:
    if session_id.endswith("-fmt"):
        return prompt

    history = SESSION_HISTORY.get(session_id, [])
    if not history:
        return prompt

    lines = ["Conversation so far:"]
    for item in history[-MAX_HISTORY_ITEMS:]:
        lines.append(f"{item['role']}: {item['text']}")

    lines.append("Current user message:")
    lines.append(prompt)
    return "\n".join(lines)


def update_history(session_id: str, user_prompt: str, response_text: str) -> None:
    if session_id.endswith("-fmt"):
        return

    history = SESSION_HISTORY.setdefault(session_id, [])
    history.append({"role": "User", "text": user_prompt.strip()})
    history.append({"role": "Assistant", "text": response_text.strip()})
    if len(history) > MAX_HISTORY_ITEMS * 2:
        del history[:-MAX_HISTORY_ITEMS * 2]


def build_system_prompt(api_base_url: str, swagger_url: str, has_token: bool) -> str:
    return f"""
You are Sage AI for PlaNEvent.

Your job is to help users operate the PlaNEvent application by using the available API tools whenever an answer depends on live application data or when an action must be performed in the system.

Rules:
- Prefer tools over guessing when the question is about PlaNEvent data, users, groups, staff, bookings, occurrences, authentication, account management, or admin actions.
- Use the PlaNEvent API at {api_base_url}.
- Swagger source for tool definitions is {swagger_url}.
- A user bearer token {"is" if has_token else "is not"} available for authenticated calls.
- Maintain conversation context across turns. Reuse facts or IDs the user already provided unless they correct them.
- Conversation history included in the prompt is authoritative for follow-up questions about what the user previously said.
- If the user asks you to remember something, acknowledge it and store it mentally for the rest of the conversation.
- If the user later asks what they previously told you, answer from conversation context first and do not call an API unless the question is explicitly about live PlaNEvent data.
- Be explicit when an action succeeded, failed, or requires missing information.
- If a tool response includes validation or API errors, explain them clearly and suggest the next corrective step.
- Do not invent records, IDs, or operation results.
- Keep answers concise and action-oriented.
- Prefer these tools for live PlaNEvent data:
  - Staff list or count: `get__api_lookups_staff` with no arguments.
  - Group list or count: `get__api_lookups_groups` with no arguments.
  - Booking list or count: `get__api_bookings` with no arguments.
  - Occurrence list or count: `get__api_occurrences`; only pass `startUtc` and `endUtc` when the user asks for a range or calendar window.
  - Signed-in user details: `get__api_auth_me` or `get__api_account_profile`.
- Use admin endpoints only for explicit admin tasks.
- For read-only lookup endpoints with no parameters, call them with no arguments.
- If a tool fails due to missing parameters, do not retry the same invalid call repeatedly. Re-check the schema, choose the correct tool, or ask only for the specific missing input.
- When the user asks for a count, call the relevant list endpoint and count the returned items.
""".strip()


def stringify_result(result: Any) -> str:
    if isinstance(result, str):
        return strip_thinking(result)
    if isinstance(result, dict):
        extracted = extract_content_text(result)
        return strip_thinking(extracted or str(result))
    content = getattr(result, "content", None)
    if content:
        extracted = extract_content_text({"content": content})
        if extracted:
            return strip_thinking(extracted)
        return strip_thinking(str(content))
    message = getattr(result, "message", None)
    if message:
        return strip_thinking(str(message))
    return strip_thinking(str(result))


def extract_content_text(payload: dict[str, Any]) -> str:
    content = payload.get("content")
    if isinstance(content, list):
        texts: list[str] = []
        for item in content:
            if isinstance(item, dict):
                text = item.get("text")
                if isinstance(text, str) and text.strip():
                    texts.append(text.strip())
            elif isinstance(item, str) and item.strip():
                texts.append(item.strip())
        return "\n\n".join(texts)
    return ""


def strip_thinking(text: str) -> str:
    cleaned = re.sub(r"<thinking>.*?</thinking>\s*", "", text, flags=re.DOTALL | re.IGNORECASE)
    return cleaned.strip()
