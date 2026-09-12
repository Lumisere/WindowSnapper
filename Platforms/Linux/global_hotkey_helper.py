#!/usr/bin/env python3

import hashlib
import os
import sys

try:
    import dbus
    from dbus.mainloop.glib import DBusGMainLoop
    from gi.repository import GLib
except Exception as exc:
    print(
        "ERR\tWayland global hotkeys need python3-dbus and python3-gi. "
        f"Import failed: {exc}",
        flush=True,
    )
    raise SystemExit(2)

if len(sys.argv) != 3:
    print("ERR\tExpected capture and start/stop shortcut arguments", flush=True)
    raise SystemExit(2)

capture_trigger = sys.argv[1]
toggle_trigger = sys.argv[2]

DBusGMainLoop(set_as_default=True)
PORTAL_NAME = "org.freedesktop.portal.Desktop"
PORTAL_PATH = "/org/freedesktop/portal/desktop"
REQUEST_IFACE = "org.freedesktop.portal.Request"
SESSION_IFACE = "org.freedesktop.portal.Session"
SHORTCUTS_IFACE = "org.freedesktop.portal.GlobalShortcuts"

loop = GLib.MainLoop()
bus = dbus.SessionBus()
portal = bus.get_object(PORTAL_NAME, PORTAL_PATH)
sender = bus.get_unique_name()[1:].replace(".", "_")
request_counter = 0
session_counter = 0
session_handle = None
stopping = False


def emit_error(message):
    print(f"ERR\t{message}", flush=True)


def request_path():
    global request_counter
    request_counter += 1
    token = f"wsh{request_counter}_{os.getpid()}"
    return f"/org/freedesktop/portal/desktop/request/{sender}/{token}", token


def session_token():
    global session_counter
    session_counter += 1
    return f"wshs{session_counter}_{os.getpid()}"


def call_request(method_name, callback, *args, options=None):
    path, token = request_path()
    values = dict(options or {})
    values["handle_token"] = token

    bus.add_signal_receiver(
        callback,
        signal_name="Response",
        dbus_interface=REQUEST_IFACE,
        bus_name=PORTAL_NAME,
        path=path,
    )

    method = portal.get_dbus_method(method_name, SHORTCUTS_IFACE)
    method(*args, dbus.Dictionary(values, signature="sv"))


def on_activated(session, shortcut_id, _timestamp, _options):
    if session_handle is not None and str(session) != str(session_handle):
        return

    shortcut_id = str(shortcut_id)
    if shortcut_id.startswith("capture_now_"):
        print("CAPTURE", flush=True)
    elif shortcut_id.startswith("toggle_capture_"):
        print("TOGGLE", flush=True)


def on_bound(response, results):
    if int(response) != 0:
        emit_error("The desktop portal refused or cancelled the global shortcut binding")
        shutdown()
        return

    shortcuts = results.get("shortcuts", [])
    bound_ids = {str(item[0]) for item in shortcuts}
    if not any(item.startswith("capture_now_") for item in bound_ids):
        emit_error("The capture-now global shortcut was not bound by the desktop portal")
        shutdown()
        return
    if not any(item.startswith("toggle_capture_") for item in bound_ids):
        emit_error("The start/stop global shortcut was not bound by the desktop portal")
        shutdown()
        return

    print("READY", flush=True)


def on_session_created(response, results):
    global session_handle

    if int(response) != 0:
        emit_error("Could not create an XDG global-shortcuts session")
        shutdown()
        return

    session_handle = results.get("session_handle")
    if not session_handle:
        emit_error("The desktop portal did not return a global-shortcuts session")
        shutdown()
        return

    # Bake the trigger into the ID or some desktops cling to the old binding like their life depends on it.
    capture_hash = hashlib.sha1(capture_trigger.encode("utf-8")).hexdigest()[:12]
    toggle_hash = hashlib.sha1(toggle_trigger.encode("utf-8")).hexdigest()[:12]
    capture_id = "capture_now_" + capture_hash
    toggle_id = "toggle_capture_" + toggle_hash

    shortcuts = dbus.Array(
        [
            dbus.Struct(
                (
                    dbus.String(capture_id),
                    dbus.Dictionary(
                        {
                            "description": dbus.String("Capture a screenshot now"),
                            "preferred_trigger": dbus.String(capture_trigger),
                        },
                        signature="sv",
                    ),
                ),
                signature=None,
            ),
            dbus.Struct(
                (
                    dbus.String(toggle_id),
                    dbus.Dictionary(
                        {
                            "description": dbus.String("Start or stop scheduled screenshots"),
                            "preferred_trigger": dbus.String(toggle_trigger),
                        },
                        signature="sv",
                    ),
                ),
                signature=None,
            ),
        ],
        signature="(sa{sv})",
    )

    call_request(
        "BindShortcuts",
        on_bound,
        dbus.ObjectPath(session_handle),
        shortcuts,
        "",
    )


def shutdown():
    global stopping
    if stopping:
        return
    stopping = True

    try:
        if session_handle:
            session = bus.get_object(PORTAL_NAME, session_handle)
            session.Close(dbus_interface=SESSION_IFACE)
    except Exception:
        pass

    GLib.idle_add(loop.quit)


def main():
    try:
        version = portal.Get("org.freedesktop.portal.GlobalShortcuts", "version", dbus_interface="org.freedesktop.DBus.Properties")
        if int(version) < 1:
            raise RuntimeError("XDG Global Shortcuts portal is unavailable")
    except Exception as exc:
        emit_error(f"XDG Global Shortcuts portal is unavailable: {exc}")
        return 2

    bus.add_signal_receiver(
        on_activated,
        signal_name="Activated",
        dbus_interface=SHORTCUTS_IFACE,
        bus_name=PORTAL_NAME,
        path=PORTAL_PATH,
    )

    expected_path, handle_token = request_path()
    token = session_token()
    # CreateSession wants two tokens because one apparently was not enough paperwork.
    bus.add_signal_receiver(
        on_session_created,
        signal_name="Response",
        dbus_interface=REQUEST_IFACE,
        bus_name=PORTAL_NAME,
        path=expected_path,
    )
    method = portal.get_dbus_method("CreateSession", SHORTCUTS_IFACE)
    method(
        dbus.Dictionary(
            {
                "handle_token": dbus.String(handle_token),
                "session_handle_token": dbus.String(token),
            },
            signature="sv",
        )
    )

    try:
        loop.run()
    except KeyboardInterrupt:
        pass
    finally:
        shutdown()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
