# Copyright (c) Microsoft. All rights reserved.

"""Exception hierarchy used across Agent Framework core and connectors.

See python/CODING_STANDARD.md § Exception Hierarchy for design rationale
and guidance on choosing the correct exception class.
"""

import logging
from collections.abc import Mapping
from typing import Any, Literal, cast

logger = logging.getLogger("agent_framework")


class AgentFrameworkException(Exception):
    """Base exception for the Agent Framework.

    Automatically logs the message as debug.
    """

    def __init__(
        self,
        message: str,
        inner_exception: Exception | None = None,
        log_level: Literal[0] | Literal[10] | Literal[20] | Literal[30] | Literal[40] | Literal[50] | None = 10,
        *args: Any,
        **kwargs: Any,
    ):
        """Create an AgentFrameworkException.

        This emits a debug log (by default), with the inner_exception if provided.
        """
        if log_level is not None:
            logger.log(log_level, message, exc_info=inner_exception)
        if inner_exception:
            super().__init__(message, inner_exception, *args)
        else:
            super().__init__(message, *args)


def _get_response_error_details(  # pyright: ignore[reportUnusedFunction]
    exception: BaseException,
) -> tuple[str, str | None]:
    """Preserve the provider's structured error code and message when available."""
    errors = [exception]
    visited: set[int] = set()
    while errors:
        current = errors.pop()
        if id(current) in visited:
            continue
        visited.add(id(current))
        status = getattr(current, "status_code", None)
        if isinstance(status, int) and not isinstance(status, bool) and 400 <= status <= 599:
            body = getattr(current, "body", None)
            error_body: object = body
            if isinstance(body, Mapping):
                body_mapping = cast(Mapping[object, object], body)
                nested_error = body_mapping.get("error")
                if isinstance(nested_error, Mapping):
                    error_body = cast(Mapping[object, object], nested_error)

            code: object = None
            message: object = None
            if isinstance(error_body, Mapping):
                error_mapping = cast(Mapping[object, object], error_body)
                code = error_mapping.get("code") or error_mapping.get("type") or error_mapping.get("title")
                message = error_mapping.get("message") or error_mapping.get("detail")
            elif isinstance(error_body, str):
                message = error_body

            if not isinstance(code, str) or not code:
                code = getattr(current, "code", None)
            if not isinstance(message, str) or not message:
                message = getattr(current, "message", None)
            if not isinstance(message, str) or not message:
                message = str(current) or type(current).__name__
            return (code if isinstance(code, str) and code else "server_error"), message
        if isinstance(current, AgentFrameworkException):
            errors.extend(arg for arg in current.args if isinstance(arg, BaseException))
            if current.__cause__ is not None:
                errors.append(current.__cause__)
    return "server_error", None


# region Agent Exceptions


class AgentException(AgentFrameworkException):
    """Base class for all agent exceptions."""

    pass


class AgentInvalidAuthException(AgentException):
    """An authentication error occurred in an agent."""

    pass


class AgentInvalidRequestException(AgentException):
    """An invalid request was made to an agent."""

    pass


class AgentInvalidResponseException(AgentException):
    """An invalid or unexpected response was received from an agent."""

    pass


class AgentContentFilterException(AgentException):
    """A content filter was triggered by an agent."""

    pass


# endregion

# region Chat Client Exceptions


class ChatClientException(AgentFrameworkException):
    """Base class for all chat client exceptions."""

    pass


class ResponseInvalidatedException(ChatClientException):
    """A provider invalidated partial response output, so its local function calls must not execute."""

    pass


class ChatClientInvalidAuthException(ChatClientException):
    """An authentication error occurred in a chat client."""

    pass


class ChatClientInvalidRequestException(ChatClientException):
    """An invalid request was made to a chat client."""

    pass


class ChatClientInvalidResponseException(ChatClientException):
    """An invalid or unexpected response was received from a chat client."""

    pass


class ChatClientContentFilterException(ChatClientException):
    """A content filter was triggered by a chat client."""

    pass


# endregion

# region Integration Exceptions


class IntegrationException(AgentFrameworkException):
    """Base class for all external service/dependency integration exceptions."""

    pass


class IntegrationInitializationError(IntegrationException):
    """A wrapped dependency/service lifecycle failure occurred during setup."""

    pass


class IntegrationInvalidAuthException(IntegrationException):
    """An authentication error occurred in an external integration."""

    pass


class IntegrationInvalidRequestException(IntegrationException):
    """An invalid request was made to an external integration."""

    pass


class IntegrationInvalidResponseException(IntegrationException):
    """An invalid or unexpected response was received from an external integration."""

    pass


class IntegrationContentFilterException(IntegrationException):
    """A content filter was triggered by an external integration."""

    pass


# endregion

# region Content Exceptions


class ContentError(AgentFrameworkException):
    """An error occurred while processing content."""

    pass


class AdditionItemMismatch(ContentError):
    """A type mismatch occurred while merging content items."""

    pass


# endregion

# region Tool Exceptions


class ToolException(AgentFrameworkException):
    """Base class for all tool-related exceptions."""

    pass


class ToolExecutionException(ToolException):
    """A tool or prompt call failed at runtime."""

    pass


class UserInputRequiredException(ToolException):
    """Raised when a tool requires user input to proceed.

    This exception carries ``user_input_request`` Content items so the tool
    invocation layer can propagate them instead of swallowing them as a generic
    tool error. ``Agent.as_tool()`` handles unresolved child function approvals
    separately and fails the delegated invocation.

    Args:
        contents: The user-input-request Content items from the sub-agent response.
        message: Human-readable description of why user input is needed.
    """

    def __init__(
        self,
        contents: list[Any],
        message: str = "Tool requires user input to proceed.",
    ) -> None:
        """Create a UserInputRequiredException.

        Args:
            contents: The user-input-request Content items from the sub-agent response.
            message: Human-readable description of why user input is needed.
        """
        super().__init__(message, log_level=None)
        self.contents = contents


# endregion

# region Middleware Exceptions


class MiddlewareException(AgentFrameworkException):
    """An error occurred during middleware execution."""

    pass


# endregion

# region Settings Exceptions


class SettingNotFoundError(AgentFrameworkException):
    """A required setting could not be resolved from any source."""

    pass


# endregion

# region Workflow Exceptions


class WorkflowException(AgentFrameworkException):
    """Base exception for workflow errors."""

    pass


class WorkflowRunnerException(WorkflowException):
    """Base exception for workflow runner errors."""

    pass


class WorkflowConvergenceException(WorkflowRunnerException):
    """Exception raised when a workflow runner fails to converge within the maximum iterations."""

    pass


class WorkflowCheckpointException(WorkflowRunnerException):
    """Exception raised for errors related to workflow checkpoints."""

    pass


# endregion
