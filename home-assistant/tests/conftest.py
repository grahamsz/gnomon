import sys

import pytest
import pytest_socket


pytest_plugins = "pytest_homeassistant_custom_component"


@pytest.fixture(autouse=True)
def auto_enable_custom_integrations(enable_custom_integrations):
    """Let Home Assistant discover this repository's integration."""
    yield


@pytest.hookimpl(trylast=True)
def pytest_runtest_setup() -> None:
    """Allow the loopback socket pair required by asyncio on Windows."""
    if sys.platform == "win32":
        pytest_socket.enable_socket()


@pytest.hookimpl(hookwrapper=True, tryfirst=True)
def pytest_fixture_setup(fixturedef):
    """Enable sockets before pytest-asyncio constructs its Windows event loop."""
    if sys.platform == "win32" and fixturedef.argname == "event_loop":
        pytest_socket.enable_socket()
    yield
