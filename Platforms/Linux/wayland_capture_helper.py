#!/usr/bin/env python3

import binascii
import os
import struct
import sys
import threading
import zlib

try:
    import dbus
    from dbus.mainloop.glib import DBusGMainLoop
    import gi
    gi.require_version("Gst", "1.0")
    from gi.repository import GLib, Gst
except Exception as exc:
    print(
        "ERR\tWayland ScreenCast needs python3-dbus, python3-gi, GStreamer, and the PipeWire GStreamer plugin. "
        f"Import failed: {exc}",
        flush=True,
    )
    raise SystemExit(2)

DBusGMainLoop(set_as_default=True)
Gst.init(None)

REQUEST_IFACE = "org.freedesktop.portal.Request"
SCREENCAST_IFACE = "org.freedesktop.portal.ScreenCast"
SESSION_IFACE = "org.freedesktop.portal.Session"
PORTAL_NAME = "org.freedesktop.portal.Desktop"
PORTAL_PATH = "/org/freedesktop/portal/desktop"

loop = GLib.MainLoop()
bus = dbus.SessionBus()
portal = bus.get_object(PORTAL_NAME, PORTAL_PATH)
sender = bus.get_unique_name()[1:].replace(".", "_")
request_counter = 0
session_counter = 0
session_handle = None
pipewire_fd = None
pipeline = None
latest_frame = None
frame_lock = threading.Lock()
ready_sent = False
stopping = False
capture_cursor = os.environ.get("WINDOWSNAPPER_CAPTURE_CURSOR", "0") == "1"


def emit_error(message):
    print(f"ERR\t{message}", flush=True)


def request_path():
    global request_counter
    request_counter += 1
    token = f"wsc{request_counter}"
    return f"/org/freedesktop/portal/desktop/request/{sender}/{token}", token


def session_path():
    global session_counter
    session_counter += 1
    token = f"wsc{session_counter}"
    return f"/org/freedesktop/portal/desktop/session/{sender}/{token}", token


def portal_request(method_name, callback, *args, options=None):
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

    method = portal.get_dbus_method(method_name, SCREENCAST_IFACE)
    method(*args, dbus.Dictionary(values, signature="sv"))


def png_chunk(kind, payload):
    crc = binascii.crc32(kind)
    crc = binascii.crc32(payload, crc) & 0xFFFFFFFF
    return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", crc)


def write_png(path, width, height, rgba):
    row_size = width * 4
    rows = []
    for y in range(height):
        start = y * row_size
        rows.append(b"\x00" + rgba[start:start + row_size])

    header = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    body = zlib.compress(b"".join(rows), 6)

    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as stream:
        stream.write(b"\x89PNG\r\n\x1a\n")
        stream.write(png_chunk(b"IHDR", header))
        stream.write(png_chunk(b"IDAT", body))
        stream.write(png_chunk(b"IEND", b""))


def on_frame(sink):
    global latest_frame, ready_sent

    sample = sink.emit("pull-sample")
    if sample is None:
        return Gst.FlowReturn.ERROR

    caps = sample.get_caps()
    structure = caps.get_structure(0)
    width = int(structure.get_value("width"))
    height = int(structure.get_value("height"))
    buffer = sample.get_buffer()

    ok, mapped = buffer.map(Gst.MapFlags.READ)
    if not ok:
        return Gst.FlowReturn.ERROR

    try:
        raw = bytes(mapped.data)
        if height <= 0 or width <= 0:
            return Gst.FlowReturn.ERROR

        stride = len(raw) // height
        packed_stride = width * 4
        if stride < packed_stride:
            return Gst.FlowReturn.ERROR

        if stride == packed_stride:
            packed = raw[: packed_stride * height]
        else:
            rows = []
            for y in range(height):
                start = y * stride
                rows.append(raw[start:start + packed_stride])
            packed = b"".join(rows)

        with frame_lock:
            latest_frame = (width, height, packed)

        if not ready_sent:
            ready_sent = True
            print("READY", flush=True)
    finally:
        buffer.unmap(mapped)

    return Gst.FlowReturn.OK


def on_bus_message(_bus, message):
    if message.type == Gst.MessageType.ERROR:
        error, debug = message.parse_error()
        emit_error(f"PipeWire/GStreamer error: {error.message}")
        if debug:
            print(debug, file=sys.stderr, flush=True)
        shutdown()
    elif message.type == Gst.MessageType.EOS and not stopping:
        emit_error("The selected Wayland window stream ended")
        shutdown()


def start_pipeline(node_id):
    global pipeline, pipewire_fd

    empty = dbus.Dictionary(signature="sv")
    fd_object = portal.OpenPipeWireRemote(
        dbus.ObjectPath(session_handle),
        empty,
        dbus_interface=SCREENCAST_IFACE,
    )
    pipewire_fd = fd_object.take()

    pipeline = Gst.parse_launch(
        f"pipewiresrc fd={pipewire_fd} path={int(node_id)} do-timestamp=true ! "
        "queue max-size-buffers=2 leaky=downstream ! "
        "videoconvert ! video/x-raw,format=RGBA ! "
        "appsink name=frames emit-signals=true max-buffers=1 drop=true sync=false"
    )

    sink = pipeline.get_by_name("frames")
    if sink is None:
        raise RuntimeError("GStreamer appsink could not be created")

    sink.connect("new-sample", on_frame)
    gst_bus = pipeline.get_bus()
    gst_bus.add_signal_watch()
    gst_bus.connect("message", on_bus_message)

    result = pipeline.set_state(Gst.State.PLAYING)
    if result == Gst.StateChangeReturn.FAILURE:
        raise RuntimeError("GStreamer could not start the PipeWire stream")


def on_start(response, results):
    if int(response) != 0:
        emit_error("Wayland window selection was cancelled or denied")
        shutdown()
        return

    streams = results.get("streams", [])
    if not streams:
        emit_error("The portal did not return a PipeWire window stream")
        shutdown()
        return

    node_id = streams[0][0]
    try:
        start_pipeline(node_id)
    except Exception as exc:
        emit_error(str(exc))
        shutdown()


def on_sources_selected(response, _results):
    if int(response) != 0:
        emit_error("The portal refused the requested window source")
        shutdown()
        return

    portal_request("Start", on_start, dbus.ObjectPath(session_handle), "")


def on_session_created(response, results):
    global session_handle

    if int(response) != 0:
        emit_error("Could not create a Wayland ScreenCast session")
        shutdown()
        return

    session_handle = results.get("session_handle")
    if not session_handle:
        emit_error("The portal did not return a ScreenCast session handle")
        shutdown()
        return

    portal_request(
        "SelectSources",
        on_sources_selected,
        dbus.ObjectPath(session_handle),
        options={
            "types": dbus.UInt32(2),       # WINDOW
            "multiple": dbus.Boolean(False),
            "cursor_mode": dbus.UInt32(2 if capture_cursor else 1),
        },
    )


def shutdown():
    global stopping
    if stopping:
        return
    stopping = True

    try:
        if pipeline is not None:
            pipeline.set_state(Gst.State.NULL)
    except Exception:
        pass

    try:
        if session_handle:
            session = bus.get_object(PORTAL_NAME, session_handle)
            session.Close(dbus_interface=SESSION_IFACE)
    except Exception:
        pass

    try:
        if pipewire_fd is not None:
            os.close(pipewire_fd)
    except Exception:
        pass

    GLib.idle_add(loop.quit)


def command_loop():
    for raw_line in sys.stdin:
        line = raw_line.rstrip("\r\n")
        if not line:
            continue

        command, _, argument = line.partition("\t")
        command = command.upper()

        if command == "CAPTURE":
            with frame_lock:
                frame = latest_frame

            if frame is None:
                emit_error("The Wayland stream has not produced a frame yet")
                continue

            try:
                width, height, rgba = frame
                write_png(argument, width, height, rgba)
                print(f"OK\t{argument}", flush=True)
            except Exception as exc:
                emit_error(f"Could not save PipeWire frame: {exc}")

        elif command == "QUIT":
            GLib.idle_add(shutdown)
            return


def begin():
    if Gst.ElementFactory.find("pipewiresrc") is None:
        emit_error("GStreamer's PipeWire plugin is missing (install gstreamer1.0-pipewire)")
        return False

    _, token = session_path()
    portal_request(
        "CreateSession",
        on_session_created,
        options={"session_handle_token": token},
    )
    return True


if begin():
    threading.Thread(target=command_loop, daemon=True).start()
    try:
        loop.run()
    except KeyboardInterrupt:
        shutdown()
