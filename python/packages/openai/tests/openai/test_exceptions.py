# Copyright (c) Microsoft. All rights reserved.

from __future__ import annotations

from collections.abc import Iterator, Mapping
from typing import Any
from unittest.mock import MagicMock

import pytest
from openai import BadRequestError

from agent_framework_openai import OpenAIContentFilterException
from agent_framework_openai._exceptions import (
    ContentFilterCodes,
    ContentFilterResult,
    ContentFilterResultSeverity,
    _get_openai_error_details,
)


@pytest.mark.parametrize(
    ("inner_code", "expected_code"),
    [
        ("ResponsibleAIPolicyViolation", "ResponsibleAIPolicyViolation"),
        ("ContentFiltered", "ContentFiltered"),
        ("FutureContentFilterCode", "Unknown"),
        (None, "ResponsibleAIPolicyViolation"),
    ],
)
def test_content_filter_exception_codes(inner_code: str | None, expected_code: str) -> None:
    inner_error: dict[str, Any] = {
        "content_filter_result": {"self_harm": {"filtered": True, "severity": "medium"}},
    }
    if inner_code is not None:
        inner_error["code"] = inner_code
    error = BadRequestError(
        "Prompt blocked by content filter",
        response=MagicMock(status_code=400),
        body={"code": "content_filter", "param": "prompt", "innererror": inner_error},
    )

    exception = OpenAIContentFilterException("Content filtered", error)

    assert isinstance(exception.content_filter_code, ContentFilterCodes)
    assert exception.content_filter_code.value == expected_code
    assert exception.param == "prompt"
    assert exception.content_filter_result == {
        "self_harm": ContentFilterResult(filtered=True, severity=ContentFilterResultSeverity.MEDIUM),
    }


def test_openai_error_details_preserve_structured_body() -> None:
    error = BadRequestError(
        "rendered error",
        response=MagicMock(status_code=400),
        body={
            "error": {
                "code": "missing_required_parameter",
                "message": "Input is required.",
                "type": "invalid_request_error",
                "param": "input",
                "request_id": "req_test",
            }
        },
    )
    details = _get_openai_error_details(error)

    assert details is not None
    assert details.code == "missing_required_parameter"
    assert details.message == "Input is required."
    assert details.status_code == 400
    assert details.additional_properties == {
        "code": "missing_required_parameter",
        "message": "Input is required.",
        "type": "invalid_request_error",
        "param": "input",
        "request_id": "req_test",
    }


def test_openai_error_details_fail_closed_when_mapping_access_raises() -> None:
    class BrokenMapping(Mapping[str, object]):
        def __getitem__(self, key: str) -> object:
            raise RuntimeError("broken body")

        def __iter__(self) -> Iterator[str]:
            return iter(("error",))

        def __len__(self) -> int:
            return 1

    error = BadRequestError(
        "rendered error",
        response=MagicMock(status_code=400),
        body=BrokenMapping(),
    )
    assert _get_openai_error_details(error) is None
