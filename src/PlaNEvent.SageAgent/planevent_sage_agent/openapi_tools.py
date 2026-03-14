from __future__ import annotations

import copy
import inspect
import keyword
import re
from dataclasses import dataclass
from functools import lru_cache
from typing import Any
from urllib.parse import quote

import httpx
from strands import tool


@dataclass(frozen=True)
class OperationSpec:
    name: str
    method: str
    path: str
    description: str
    input_schema: dict[str, Any]


def build_api_tools(api_base_url: str, swagger_url: str, access_token: str | None) -> list[Any]:
    document = load_openapi_document(swagger_url)
    operations = parse_operations(document)
    return [create_operation_tool(api_base_url, access_token, operation) for operation in operations]


@lru_cache(maxsize=4)
def load_openapi_document(swagger_url: str) -> dict[str, Any]:
    response = httpx.get(swagger_url, timeout=30.0)
    response.raise_for_status()
    return response.json()


def parse_operations(document: dict[str, Any]) -> list[OperationSpec]:
    components = document.get("components", {})
    operations: list[OperationSpec] = []
    used_names: set[str] = set()

    for path, path_item in document.get("paths", {}).items():
        if not isinstance(path_item, dict):
            continue

        for method, operation in path_item.items():
            if method.upper() not in {"GET", "POST", "PUT", "PATCH", "DELETE"}:
                continue
            if not isinstance(operation, dict):
                continue

            tool_name = build_unique_name(operation, method, path, used_names)
            description = operation.get("summary") or operation.get("description") or f"{method.upper()} {path}"
            input_schema = build_input_schema(operation, components)

            operations.append(
                OperationSpec(
                    name=tool_name,
                    method=method.upper(),
                    path=path,
                    description=description,
                    input_schema=input_schema,
                )
            )

    return operations


def create_operation_tool(api_base_url: str, access_token: str | None, operation: OperationSpec) -> Any:
    def invoke_operation(**kwargs: Any) -> dict[str, Any]:
        return execute_operation(api_base_url, access_token, operation, kwargs)

    invoke_operation.__name__ = operation.name
    invoke_operation.__doc__ = operation.description
    invoke_operation.__signature__ = build_tool_signature(operation.input_schema)

    return tool(
        name=operation.name,
        description=operation.description,
        inputSchema={"json": operation.input_schema},
    )(invoke_operation)


def execute_operation(
    api_base_url: str,
    access_token: str | None,
    operation: OperationSpec,
    values: dict[str, Any],
) -> dict[str, Any]:
    path = operation.path
    query: dict[str, Any] = {}
    body = values.get("body")

    for key, value in values.items():
        if key == "body" or value is None:
            continue

        token = "{" + key + "}"
        if token in path:
            path = path.replace(token, quote(str(value), safe=""))
        else:
            query[key] = value

    url = api_base_url.rstrip("/") + path
    headers = {"Accept": "application/json"}
    if access_token:
        headers["Authorization"] = f"Bearer {access_token}"

    with httpx.Client(timeout=30.0) as client:
        response = client.request(
            operation.method,
            url,
            params=query or None,
            json=body,
            headers=headers,
        )

    parsed_body = parse_response_body(response)
    return {
        "operation": operation.name,
        "method": operation.method,
        "url": str(response.request.url),
        "status_code": response.status_code,
        "success": response.is_success,
        "body": parsed_body,
    }


def parse_response_body(response: httpx.Response) -> Any:
    content_type = response.headers.get("content-type", "")
    if "application/json" in content_type:
        try:
            return response.json()
        except ValueError:
            return response.text
    return response.text


def build_unique_name(
    operation: dict[str, Any],
    method: str,
    path: str,
    used_names: set[str],
) -> str:
    base_name = operation.get("operationId") or f"{method}_{path}"
    normalized = re.sub(r"[^a-zA-Z0-9_]+", "_", base_name).strip("_").lower()
    if not normalized:
        normalized = "planevent_operation"
    if normalized[0].isdigit():
        normalized = f"op_{normalized}"

    candidate = normalized
    suffix = 2
    while candidate in used_names:
        candidate = f"{normalized}_{suffix}"
        suffix += 1

    used_names.add(candidate)
    return candidate


def build_input_schema(operation: dict[str, Any], components: dict[str, Any]) -> dict[str, Any]:
    properties: dict[str, Any] = {}
    required: list[str] = []

    for parameter in operation.get("parameters", []):
        resolved_parameter = resolve_schema(parameter, components)
        if not isinstance(resolved_parameter, dict):
            continue

        name = resolved_parameter.get("name")
        if not name:
            continue

        schema = resolve_schema(resolved_parameter.get("schema", {"type": "string"}), components)
        if not isinstance(schema, dict):
            schema = {"type": "string"}

        schema = copy.deepcopy(schema)
        location = resolved_parameter.get("in", "query")
        description = resolved_parameter.get("description") or f"{location} parameter {name}."
        schema["description"] = description
        properties[name] = schema

        if resolved_parameter.get("required"):
            required.append(name)

    request_body = resolve_schema(operation.get("requestBody"), components)
    if isinstance(request_body, dict):
        content = request_body.get("content", {})
        body_schema = None
        if "application/json" in content:
            body_schema = content["application/json"].get("schema")
        elif content:
            first_media_type = next(iter(content.values()))
            body_schema = first_media_type.get("schema")

        if body_schema:
            resolved_body = resolve_schema(body_schema, components)
            if not isinstance(resolved_body, dict):
                resolved_body = {"type": "object"}
            properties["body"] = copy.deepcopy(resolved_body)
            properties["body"]["description"] = request_body.get("description") or "Request body."
            if request_body.get("required"):
                required.append("body")

    schema: dict[str, Any] = {
        "type": "object",
        "properties": properties,
        "additionalProperties": False,
    }
    if required:
        schema["required"] = sorted(set(required))
    return schema


def build_tool_signature(input_schema: dict[str, Any]) -> inspect.Signature:
    properties = input_schema.get("properties", {})
    required = set(input_schema.get("required", []))
    parameters: list[inspect.Parameter] = []

    for name in properties:
        if not is_valid_parameter_name(name):
            continue

        default = inspect.Parameter.empty if name in required else None
        parameters.append(
            inspect.Parameter(
                name=name,
                kind=inspect.Parameter.KEYWORD_ONLY,
                default=default,
                annotation=Any,
            )
        )

    return inspect.Signature(parameters=parameters, return_annotation=dict[str, Any])


def is_valid_parameter_name(name: str) -> bool:
    return bool(name) and name.isidentifier() and not keyword.iskeyword(name)


def resolve_schema(schema: Any, components: dict[str, Any]) -> Any:
    if not isinstance(schema, dict):
        return schema

    if "$ref" in schema:
        ref = schema["$ref"]
        if isinstance(ref, str) and ref.startswith("#/components/"):
            target: Any = components
            for part in ref.removeprefix("#/components/").split("/"):
                if not isinstance(target, dict):
                    return schema
                target = target.get(part)
            return resolve_schema(copy.deepcopy(target), components)

    if "allOf" in schema:
        merged: dict[str, Any] = {"type": "object", "properties": {}, "required": []}
        for item in schema["allOf"]:
            resolved_item = resolve_schema(item, components)
            if not isinstance(resolved_item, dict):
                continue
            merged["properties"].update(copy.deepcopy(resolved_item.get("properties", {})))
            merged["required"].extend(copy.deepcopy(resolved_item.get("required", [])))
            if "type" in resolved_item and merged.get("type") == "object":
                merged["type"] = resolved_item["type"]
        if not merged["required"]:
            merged.pop("required")
        return merged

    resolved = copy.deepcopy(schema)
    if "properties" in resolved and isinstance(resolved["properties"], dict):
        resolved["properties"] = {
            key: resolve_schema(value, components)
            for key, value in resolved["properties"].items()
        }
    if "items" in resolved:
        resolved["items"] = resolve_schema(resolved["items"], components)
    if isinstance(resolved.get("additionalProperties"), dict):
        resolved["additionalProperties"] = resolve_schema(resolved["additionalProperties"], components)
    return resolved
