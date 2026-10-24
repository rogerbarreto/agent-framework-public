# Copyright (c) Microsoft. All rights reserved.

"""Tests for AgentFrameworkException inner_exception handling."""

import pytest

from agent_framework import AgentFrameworkException, ResponseInvalidatedException
from agent_framework.exceptions import (
    ChatClientException,
    ChatClientInvalidResponseException,
    _get_response_error_details,
)


def test_exception_with_inner_exception():
    """When inner_exception is provided, it should be set as the second arg."""
    inner = ValueError("inner error")
    exc = AgentFrameworkException("test message", inner_exception=inner)
    assert exc.args[0] == "test message"
    assert exc.args[1] is inner


def test_exception_without_inner_exception():
    """When inner_exception is None, args should only contain the message."""
    exc = AgentFrameworkException("test message")
    assert exc.args == ("test message",)
    assert len(exc.args) == 1


def test_exception_inner_exception_none_explicit():
    """When inner_exception is explicitly None, args should only contain the message."""
    exc = AgentFrameworkException("test message", inner_exception=None)
    assert exc.args == ("test message",)
    assert len(exc.args) == 1


def test_response_invalidated_exception_is_public_and_picklable() -> None:
    """The partial-response invalidation signal is a public chat-client exception."""
    import pickle

    inner = RuntimeError("provider stream failed")
    exc = ResponseInvalidatedException("partial response output was invalidated", inner_exception=inner)
    restored = pickle.loads(pickle.dumps(exc))

    assert isinstance(exc, ChatClientException)
    assert not isinstance(exc, ChatClientInvalidResponseException)
    assert isinstance(restored, ResponseInvalidatedException)
    assert restored.args[0] == "partial response output was invalidated"
    assert isinstance(restored.args[1], RuntimeError)
    assert str(restored.args[1]) == "provider stream failed"


@pytest.mark.parametrize(
    "status, code",
    [
        (400, "invalid_request_error"),
        (403, "permission_denied"),
        (429, "token_rate_limit_exceeded"),
        (503, "backend_overloaded"),
    ],
)
def test_response_error_details_follow_framework_inner_exception(status: int, code: str) -> None:
    class ProviderError(Exception):
        status_code = status

        def __init__(self) -> None:
            super().__init__("rendered exception")
            self.body = {"error": {"code": code, "message": "provider details"}}

    error = ChatClientException("wrapper", inner_exception=ProviderError())
    actual_code, message = _get_response_error_details(error)
    assert actual_code == code
    assert message == "provider details"


def test_response_error_details_support_problem_details_title_and_detail() -> None:
    class ProviderError(Exception):
        status_code = 429
        body = {"title": "usage_limit_reached", "detail": "Monthly usage limit reached."}

    assert _get_response_error_details(ProviderError()) == (
        "usage_limit_reached",
        "Monthly usage limit reached.",
    )


def test_response_error_details_fall_back_to_exception_code_and_message() -> None:
    class ProviderError(Exception):
        status_code = 429
        body: dict[str, object] = {}
        code = "token_quota_exceeded"
        message = "Token quota exceeded."

    assert _get_response_error_details(ProviderError()) == (
        "token_quota_exceeded",
        "Token quota exceeded.",
    )


def test_response_error_details_handle_cycles_and_ignore_implicit_context() -> None:
    class RateLimitError(Exception):
        status_code = 429

    outer = RuntimeError("outer")
    inner = RuntimeError("inner")
    outer.__cause__ = inner
    inner.__cause__ = outer
    inner.__context__ = RateLimitError("unrelated")
    assert _get_response_error_details(outer) == ("server_error", None)


def test_response_error_details_respect_non_framework_wrapper_message() -> None:
    class StorageConflictError(Exception):
        status_code = 409

    error = RuntimeError("Another request advanced this agent session; reload before writing.")
    error.__cause__ = StorageConflictError("State store item already exists.")
    assert _get_response_error_details(error) == ("server_error", None)


def test_response_error_details_preserve_outer_http_failure() -> None:
    class ServerError(Exception):
        status_code = 500
        body = {"error": {"code": "backend_overloaded", "message": "Backend is overloaded."}}

    class RateLimitError(Exception):
        status_code = 429

    error = ServerError("server failure")
    error.__cause__ = RateLimitError("earlier failure")
    assert _get_response_error_details(error) == ("backend_overloaded", "Backend is overloaded.")


@pytest.mark.parametrize("status", [True, "429", None, 200])
def test_response_error_details_do_not_infer_unsupported_classifications(status: object) -> None:
    class ProviderError(Exception):
        status_code = status

    assert _get_response_error_details(ProviderError("Error code: 429")) == ("server_error", None)
