from __future__ import annotations

from datetime import UTC, datetime, timedelta
from typing import Any
from zoneinfo import ZoneInfo, ZoneInfoNotFoundError

from strands import tool


def build_datetime_tools(default_timezone: str = "America/New_York") -> list[Any]:
    return [
        get_current_datetime_factory(default_timezone),
        get_datetime_in_timezone,
        convert_datetime_between_timezones,
        add_days_to_datetime,
    ]


def get_current_datetime_factory(default_timezone: str):
    @tool(
        name="get_current_datetime",
        description="Get the current date and time in the default business timezone and UTC.",
        inputSchema={
            "json": {
                "type": "object",
                "properties": {},
                "additionalProperties": False,
            }
        },
    )
    def get_current_datetime() -> dict[str, Any]:
        local_zone = safe_zoneinfo(default_timezone)
        now_utc = datetime.now(UTC)
        now_local = now_utc.astimezone(local_zone)
        return build_datetime_payload(now_local, now_utc, local_zone.key)

    return get_current_datetime


@tool(
    name="get_datetime_in_timezone",
    description="Get the current date and time in a specified IANA timezone such as America/New_York or Europe/London.",
    inputSchema={
        "json": {
            "type": "object",
            "properties": {
                "timezone": {
                    "type": "string",
                    "description": "IANA timezone name such as America/New_York.",
                }
            },
            "required": ["timezone"],
            "additionalProperties": False,
        }
    },
)
def get_datetime_in_timezone(*, timezone: str) -> dict[str, Any]:
    zone = safe_zoneinfo(timezone)
    now_utc = datetime.now(UTC)
    now_local = now_utc.astimezone(zone)
    return build_datetime_payload(now_local, now_utc, zone.key)


@tool(
    name="convert_datetime_between_timezones",
    description="Convert an ISO datetime string from one timezone to another. Useful for booking and schedule comparisons.",
    inputSchema={
        "json": {
            "type": "object",
            "properties": {
                "datetime_iso": {
                    "type": "string",
                    "description": "ISO datetime like 2026-03-22T14:30:00 or 2026-03-22T14:30:00-04:00.",
                },
                "from_timezone": {
                    "type": "string",
                    "description": "Source IANA timezone if the ISO string has no offset.",
                },
                "to_timezone": {
                    "type": "string",
                    "description": "Target IANA timezone such as America/New_York.",
                },
            },
            "required": ["datetime_iso", "to_timezone"],
            "additionalProperties": False,
        }
    },
)
def convert_datetime_between_timezones(
    *,
    datetime_iso: str,
    to_timezone: str,
    from_timezone: str | None = None,
) -> dict[str, Any]:
    value = datetime.fromisoformat(datetime_iso.replace("Z", "+00:00"))
    if value.tzinfo is None:
        source_zone = safe_zoneinfo(from_timezone or "UTC")
        value = value.replace(tzinfo=source_zone)
    else:
        source_zone = value.tzinfo

    target_zone = safe_zoneinfo(to_timezone)
    converted = value.astimezone(target_zone)
    return {
        "source_timezone": timezone_name(source_zone),
        "target_timezone": target_zone.key,
        "source_iso": value.isoformat(),
        "target_iso": converted.isoformat(),
        "target_date": converted.date().isoformat(),
        "target_time": converted.strftime("%H:%M:%S"),
        "target_day_of_week": converted.strftime("%A"),
    }


@tool(
    name="add_days_to_datetime",
    description="Add or subtract whole days from an ISO datetime string and return the adjusted value.",
    inputSchema={
        "json": {
            "type": "object",
            "properties": {
                "datetime_iso": {
                    "type": "string",
                    "description": "ISO datetime like 2026-03-22T14:30:00 or 2026-03-22T14:30:00-04:00.",
                },
                "days": {
                    "type": "integer",
                    "description": "Number of days to add. Negative values subtract days.",
                },
            },
            "required": ["datetime_iso", "days"],
            "additionalProperties": False,
        }
    },
)
def add_days_to_datetime(*, datetime_iso: str, days: int) -> dict[str, Any]:
    value = datetime.fromisoformat(datetime_iso.replace("Z", "+00:00"))
    adjusted = value + timedelta(days=days)
    return {
        "source_iso": value.isoformat(),
        "days_delta": days,
        "adjusted_iso": adjusted.isoformat(),
        "adjusted_date": adjusted.date().isoformat(),
        "adjusted_day_of_week": adjusted.strftime("%A"),
    }


def build_datetime_payload(local_value: datetime, utc_value: datetime, timezone_name_value: str) -> dict[str, Any]:
    return {
        "timezone": timezone_name_value,
        "local_iso": local_value.isoformat(),
        "local_date": local_value.date().isoformat(),
        "local_time": local_value.strftime("%H:%M:%S"),
        "local_day_of_week": local_value.strftime("%A"),
        "utc_iso": utc_value.isoformat(),
    }


def safe_zoneinfo(timezone_name_value: str) -> ZoneInfo:
    try:
        return ZoneInfo(timezone_name_value)
    except ZoneInfoNotFoundError as exc:
        raise ValueError(f"Unknown timezone '{timezone_name_value}'. Use a valid IANA timezone name.") from exc


def timezone_name(zone: Any) -> str:
    return getattr(zone, "key", None) or str(zone)
