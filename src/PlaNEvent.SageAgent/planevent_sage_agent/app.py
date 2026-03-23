from __future__ import annotations

import os
import re
from typing import Any

from bedrock_agentcore.runtime import BedrockAgentCoreApp
from strands import Agent
from strands.models import BedrockModel

from planevent_sage_agent.datetime_tools import build_datetime_tools
from planevent_sage_agent.openapi_tools import build_api_tools


DEFAULT_API_BASE_URL = "https://planevent.dawindemoproductsdemo.com"
DEFAULT_SWAGGER_URL = f"{DEFAULT_API_BASE_URL}/swagger/v1/swagger.json"
DEFAULT_MODEL_ID = "us.anthropic.claude-3-5-haiku-20241022-v1:0"
SESSION_HISTORY: dict[str, list[dict[str, str]]] = {}
MAX_HISTORY_ITEMS = 8


app = BedrockAgentCoreApp()


@app.entrypoint
async def invoke(payload: dict[str, Any]) -> Any:
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

    agent = create_agent(api_base_url, swagger_url, access_token, region_name, model_id)

    prompt_with_context = build_prompt_with_history(prompt, session_id)
    if bool(payload.get("stream")):
        return stream_agent_reply(
            agent,
            prompt_with_context,
            session_id,
            prompt,
            lambda: create_agent(api_base_url, swagger_url, access_token, region_name, model_id),
        )

    response_text = await collect_agent_reply(agent, prompt_with_context)
    update_history(session_id, prompt, response_text)
    return build_result_payload(response_text)


async def collect_agent_reply(agent: Agent, prompt_with_context: str) -> str:
    chunks: list[str] = []
    final_text = ""

    async for event in agent.stream_async(prompt_with_context):
        if isinstance(event, dict):
            if text := extract_stream_text(event):
                chunks.append(text)
            if "result" in event:
                final_text = stringify_result(event["result"])

    assembled = strip_thinking("".join(chunks).strip())
    if assembled:
        return assembled

    if final_text.strip():
        return strip_thinking(final_text)

    return "No response from agent."


def stream_agent_reply(agent: Agent, prompt_with_context: str, session_id: str, prompt: str, fallback_agent_factory):
    async def event_generator():
        chunks: list[str] = []
        final_text = ""

        yield {"type": "session", "sessionId": session_id}

        try:
            async for event in agent.stream_async(prompt_with_context):
                if not isinstance(event, dict):
                    continue

                if text := extract_stream_text(event):
                    chunks.append(text)
                    yield {"type": "delta", "delta": text, "sessionId": session_id}

                if "result" in event:
                    final_text = stringify_result(event["result"])

            reply_text = strip_thinking("".join(chunks).strip())
            if not reply_text:
                reply_text = strip_thinking(final_text)
            if not reply_text:
                reply_text = await collect_agent_reply(fallback_agent_factory(), prompt_with_context)

            if not chunks and reply_text.strip():
                for chunk in chunk_text_for_stream(reply_text):
                    yield {"type": "delta", "delta": chunk, "sessionId": session_id}

            update_history(session_id, prompt, reply_text)
            yield {"type": "complete", "sessionId": session_id, "reply": reply_text}
        except Exception as ex:
            yield {"type": "error", "sessionId": session_id, "message": str(ex)}

    return event_generator()


def build_result_payload(response_text: str) -> dict[str, Any]:
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


def create_agent(api_base_url: str, swagger_url: str, access_token: str | None, region_name: str, model_id: str) -> Agent:
    tools = [
        *build_datetime_tools("America/New_York"),
        *build_api_tools(api_base_url, swagger_url, access_token),
    ]
    return Agent(
        model=BedrockModel(model_id=model_id, region_name=region_name, temperature=0.1),
        tools=tools,
        system_prompt=build_system_prompt(api_base_url, swagger_url, has_token=bool(access_token)),
    )


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
- Prefer tools over guessing when the question is about PlaNEvent data, users, groups, staff, bookings, occurrences, categories, offerings, rule groups, timeslots, showcase pages, public showcase flows, authentication, account management, or admin actions.
- Use the built-in date/time tools whenever the user asks about today, tomorrow, next week, timezone conversion, current time, relative date windows, or schedule math.
- Before creating or updating offerings, rule groups, timeslots, or showcase plans that depend on relative dates, call the date/time tools first and anchor the schedule to the business timezone.
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
- PlaNEvent has both internal management flows and public booking/showcase flows:
  - Internal management covers categories, offerings, showcase pages, bookings, staff, groups, occurrences, and account/admin actions.
  - Public booking/showcase covers owner slug pages, tabs, search, carousel rows, offering drill-down, occurrence selection, and booking calendar views.
- Prefer these tools for live PlaNEvent data:
  - Staff list or count: `get__api_lookups_staff` with no arguments.
  - Group list or count: `get__api_lookups_groups` with no arguments.
  - Booking list or count: `get__api_bookings` with no arguments.
  - Occurrence list or count: `get__api_occurrences`; only pass `startUtc` and `endUtc` when the user asks for a range or calendar window.
  - Signed-in user details: `get__api_auth_me` or `get__api_account_profile`.
- Prefer calendar-management tools for the newer scheduling and editorial model:
  - Categories: use the tool for `/api/calendar-management/categories` to list, create, update, or delete the category tree.
  - Dashboard/calendar summary: use the tool for `/api/calendar-management/dashboard` when the user asks about the admin calendar view, counts, visible offerings, or a date window.
  - Offerings: use the tools for `/api/calendar-management/offerings` to list offerings, load a single offering editor, save offerings, or delete offerings.
  - Showcase editorial pages: use the tools for `/api/calendar-management/showcase-pages` to list pages, load one page, save page configuration, or delete pages.
- Showcase write rules are strict:
  - Goal-setting features are planning signals only. They are not valid showcase API sources by themselves.
  - Before creating or updating a showcase page, resolve each planned feature to a real live offering or category by calling the offerings or categories tools and matching the best source.
  - A showcase page item may only use `sourceType` values `offering` or `category`.
  - A showcase page item may only use `carouselType` values `carousel` or `rail`.
  - When saving a showcase page, include the full page payload with `name`, `slug`, `isActive`, `isHomePage`, and `items`.
  - Every showcase item should include at least `name`, `sourceType`, `sourceId`, `carouselType`, and `sortOrder`.
  - If you are updating an existing page, load the page first and then save the complete updated item list instead of sending a partial patch.
  - If there is no real offering or category to back a requested feature, say that clearly and recommend creating or activating the missing supply first.
- Treat offering setup as a 3-part concept even if the user describes it casually:
  - General info: offering name, description, category, color, publishing state, and cover image.
  - Rule groups: named scheduling variants, date ranges, and day-of-week rules.
  - Timeslots: start/end times, all-day flags, repeat-slot behavior, repeat interval, and until-last-start settings.
- When creating or updating offerings:
  - Never choose a past year or a fully past date range unless the user explicitly asks for historical data.
  - For requests like "today", "tomorrow", "this week", or "next week", resolve the exact business-local dates first with the date/time tools.
  - Make sure the saved rule groups and timeslots will generate upcoming occurrences.
  - After a create or update action, verify that upcoming occurrences exist before claiming success.
- For public showcase and customer booking questions:
  - Use the tool for `/api/public/showcase/{{ownerSlug}}` when the user asks what a customer sees, wants showcase tabs or carousel rows, needs offering drill-down, or wants booking-calendar data for a public page.
  - Use the tool for `/api/public/sales/{{ownerSlug}}` only for the legacy public sales listing flow.
  - When the user asks how to navigate to the customer-facing booking page, explain the route pattern as `/showcase/{{ownerSlug}}?pageSlug={{pageSlug}}`.
- When the user asks for counts or summaries in the newer model:
  - Category count: call the category list tool and count the returned items.
  - Offering count: call the offerings list tool and count the returned items.
  - Showcase page count: call the showcase pages list tool and count the returned items.
  - Public page details: call the public showcase tool and summarize tabs, rows, cards, breadcrumbs, occurrence choices, or booking calendar details from the response.
- Prefer these date/time tools when needed:
  - `get_current_datetime` for the current business date/time and UTC.
  - `get_relative_date_context` for business-local today, tomorrow, this week, and this month anchors.
  - `get_datetime_in_timezone` for current time in a specific timezone.
  - `convert_datetime_between_timezones` for translating times across zones.
  - `add_days_to_datetime` for moving a concrete datetime forward or backward by days.
- When the user says relative dates like "today", "tomorrow", "this weekend", or "next Friday", resolve them using the date/time tools before calling live schedule APIs.
- Use admin endpoints only for explicit admin tasks.
- For read-only lookup endpoints with no parameters, call them with no arguments.
- For public or anonymous endpoints, do not assume authentication is required just because a user token is available.
- If a tool fails due to missing parameters, do not retry the same invalid call repeatedly. Re-check the schema, choose the correct tool, or ask only for the specific missing input.
- When the user asks for a count, call the relevant list endpoint and count the returned items.
- If a user asks Sage to perform a PlaNEvent action and there is a matching live API tool, prefer the tool over explaining how a human could click through the UI.
- Always respond as a valid HTML fragment suitable for direct rendering in a chat bubble.
- Do not return Markdown.
- Do not return plain text outside HTML tags.
- Do not include `html`, `head`, `body`, `style`, or `script` tags.
- Use only safe semantic tags such as `p`, `ul`, `ol`, `li`, `strong`, `em`, `code`, `pre`, `blockquote`, `a`, `h3`, and `h4`.
- Keep the HTML concise, readable, and ready to render as-is.
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


def extract_stream_text(event: dict[str, Any]) -> str:
    if event.get("reasoning"):
        return ""

    text = event.get("data")
    if isinstance(text, str) and text:
        return text

    result = event.get("result")
    if result is not None:
        return ""

    delta = event.get("delta")
    if isinstance(delta, dict):
        delta_text = delta.get("text")
        if isinstance(delta_text, str) and delta_text:
            return delta_text

    return ""


def chunk_text_for_stream(text: str) -> list[str]:
    normalized = strip_thinking(text)
    if not normalized:
        return []

    sentence_chunks = [chunk for chunk in re.split(r"(?<=[.!?])\s+", normalized) if chunk.strip()]
    if len(sentence_chunks) > 1:
        return [f"{chunk} " for chunk in sentence_chunks[:-1]] + [sentence_chunks[-1]]

    word_chunks = normalized.split()
    if len(word_chunks) <= 6:
        return [normalized]

    chunks: list[str] = []
    current: list[str] = []
    for word in word_chunks:
        current.append(word)
        if len(current) >= 4:
            chunks.append(" ".join(current) + " ")
            current = []

    if current:
        chunks.append(" ".join(current))

    return chunks


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
