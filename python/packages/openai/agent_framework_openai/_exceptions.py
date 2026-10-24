# Copyright (c) Microsoft. All rights reserved.

from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass
from enum import Enum
from typing import Any, cast

from agent_framework.exceptions import ChatClientContentFilterException, ChatClientErrorDetails
from openai import APIStatusError, BadRequestError


def _first_nonempty_string(mapping: Mapping[object, object], *keys: str) -> str | None:
    """Return the first nonempty string stored under ``keys``."""
    for key in keys:
        value = mapping.get(key)
        if isinstance(value, str) and value:
            return value
    return None


def _get_openai_error_details(  # pyright: ignore[reportUnusedFunction]
    error: Exception,
) -> ChatClientErrorDetails | None:
    """Normalize one OpenAI SDK status error into provider-neutral details."""
    if not isinstance(error, APIStatusError):
        return None
    try:
        body = error.body
        error_body: object = body
        if isinstance(body, Mapping):
            body_mapping = cast(Mapping[object, object], body)
            nested = body_mapping.get("error")
            if isinstance(nested, Mapping):
                error_body = cast(Mapping[object, object], nested)

        additional_properties: dict[str, Any] = {}
        code: str | None = None
        message: str | None = None
        if isinstance(error_body, Mapping):
            error_mapping = cast(Mapping[object, object], error_body)
            additional_properties = {key: value for key, value in error_mapping.items() if isinstance(key, str)}
            code = _first_nonempty_string(error_mapping, "code", "type", "title")
            message = _first_nonempty_string(error_mapping, "message", "detail")
        elif isinstance(error_body, str) and error_body:
            message = error_body

        if code is None and isinstance(error.code, str) and error.code:
            code = error.code
        if message is None and isinstance(error.message, str) and error.message:
            message = error.message
        if message is None:
            return None
        return ChatClientErrorDetails(
            code=code,
            message=message,
            status_code=error.status_code,
            additional_properties=additional_properties,
        )
    except Exception:
        return None


class ContentFilterResultSeverity(Enum):
    """The severity of the content filter result."""

    HIGH = "high"
    MEDIUM = "medium"
    SAFE = "safe"
    LOW = "low"


@dataclass
class ContentFilterResult:
    """The result of a content filter check."""

    filtered: bool = False
    detected: bool = False
    severity: ContentFilterResultSeverity = ContentFilterResultSeverity.SAFE

    @classmethod
    def from_inner_error_result(cls, inner_error_results: dict[str, Any]) -> ContentFilterResult:
        """Creates a ContentFilterResult from the inner error results.

        Args:
            inner_error_results: The inner error results.

        Returns:
            ContentFilterResult: The ContentFilterResult.
        """
        return cls(
            filtered=inner_error_results.get("filtered", False),
            detected=inner_error_results.get("detected", False),
            severity=ContentFilterResultSeverity(
                inner_error_results.get("severity", ContentFilterResultSeverity.SAFE.value)
            ),
        )


class ContentFilterCodes(Enum):
    """Content filter codes."""

    RESPONSIBLE_AI_POLICY_VIOLATION = "ResponsibleAIPolicyViolation"
    CONTENT_FILTERED = "ContentFiltered"
    UNKNOWN = "Unknown"


@dataclass
class OpenAIContentFilterException(ChatClientContentFilterException):
    """AI exception for an error from Azure OpenAI's content filter."""

    # The parameter that caused the error.
    param: str | None

    # The error code specific to the content filter.
    content_filter_code: ContentFilterCodes

    # The results of the different content filter checks.
    content_filter_result: dict[str, ContentFilterResult]

    def __init__(
        self,
        message: str,
        inner_exception: BadRequestError,
    ) -> None:
        """Initializes a new instance of the ContentFilterAIException class.

        Args:
            message: The error message.
            inner_exception: The inner exception.
        """
        super().__init__(message)

        self.param = inner_exception.param
        if inner_exception.body is not None and isinstance(inner_exception.body, dict):
            inner_error = inner_exception.body.get("innererror", {})  # type: ignore
            try:
                self.content_filter_code = ContentFilterCodes(
                    inner_error.get("code", ContentFilterCodes.RESPONSIBLE_AI_POLICY_VIOLATION.value)  # type: ignore
                )
            except ValueError:
                # New provider codes must not mask the original content-filter error.
                self.content_filter_code = ContentFilterCodes.UNKNOWN
            self.content_filter_result = {
                key: ContentFilterResult.from_inner_error_result(values)  # type: ignore
                for key, values in inner_error.get("content_filter_result", {}).items()  # type: ignore
            }
