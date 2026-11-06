#!/usr/bin/env python3
"""Serve a live Wiimote view on the LAN.

The kernel already exposes the paired remote as evdev nodes. Those nodes are
readable by the logged-in user. /dev/hidraw is not, so this does not go through
the headless HidSharp service.
"""

from __future__ import annotations

import json
import math
import os
import select
import struct
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

HOST = "0.0.0.0"
PORT = 8095
ONE_G = 128.0
EVENT_STRUCT = struct.Struct("qqHHi")
EV_KEY = 0x01
EV_ABS = 0x03
ABS_RX, ABS_RY, ABS_RZ = 0x03, 0x04, 0x05
BUTTONS = {
    105: "Left",
    106: "Right",
    103: "Up",
    108: "Down",
    0x197: "+",
    0x19C: "-",
    0x101: "1",
    0x102: "2",
    0x130: "A",
    0x131: "B",
    0x13C: "Home",
}
WEB_ROOT = Path(__file__).resolve().parents[1] / "src" / "OpenWiimote.App" / "wwwroot"
ADDRESS = "02:11:AB:33:CD:55"


class Remote:
    def __init__(self) -> None:
        self._lock = threading.Lock()
        self.accel = (0.0, 0.0, 1.0)
        self.buttons: set[str] = set()
        self.recent: list[str] = []
        self.connected = False
        self.error = "waiting for the accelerometer device"

    def snapshot(self) -> dict:
        with self._lock:
            x, y, z = self.accel
            buttons = sorted(self.buttons)
            recent = list(self.recent)
            connected = self.connected
            error = self.error
        return {
            "connected": connected,
            "name": "Nintendo RVL-CNT-01",
            "address": ADDRESS,
            "accel": {"x": x, "y": y, "z": z},
            "tiltX": math.degrees(math.atan2(x, z)),
            "tiltY": math.degrees(math.atan2(y, z)),
            "buttons": buttons,
            "recent": recent,
            "error": error,
        }

    def set_accel(self, axis: int, raw: int) -> None:
        with self._lock:
            x, y, z = self.accel
            value = raw / ONE_G
            if axis == ABS_RX:
                x = value
            elif axis == ABS_RY:
                y = value
            elif axis == ABS_RZ:
                z = value
            self.accel = (x, y, z)
            self.connected = True
            self.error = ""

    def set_button(self, code: int, value: int) -> None:
        if value == 2:
            return
        name = BUTTONS.get(code, f"#{code}")
        with self._lock:
            if value:
                self.buttons.add(name)
            else:
                self.buttons.discard(name)
            self.recent.append(f"{name} {'down' if value else 'up'}")
            del self.recent[:-8]
            self.connected = True
            self.error = ""

    def set_error(self, message: str) -> None:
        with self._lock:
            self.connected = False
            self.error = message


def event_path(name: str) -> str | None:
    root = Path("/sys/class/input")
    if not root.exists():
        return None
    for node in root.glob("event*"):
        try:
            label = (node / "device" / "name").read_text().strip()
        except OSError:
            continue
        if label == name:
            return f"/dev/input/{node.name}"
    return None


def read_device(label: str, remote: Remote, kind: str, stop: threading.Event) -> None:
    while not stop.is_set():
        path = event_path(label)
        if path is None:
            if kind == "accel":
                remote.set_error("Wiimote accelerometer is not on evdev")
            stop.wait(1)
            continue
        try:
            fd = os.open(path, os.O_RDONLY | os.O_NONBLOCK)
        except OSError as exc:
            remote.set_error(f"cannot open {path}: {exc.strerror}")
            stop.wait(1)
            continue
        try:
            while not stop.is_set() and event_path(label) == path:
                ready, _, _ = select.select([fd], [], [], 0.5)
                if not ready:
                    continue
                data = os.read(fd, EVENT_STRUCT.size * 64)
                for offset in range(0, len(data) - EVENT_STRUCT.size + 1, EVENT_STRUCT.size):
                    _sec, _usec, typ, code, value = EVENT_STRUCT.unpack_from(data, offset)
                    if kind == "accel" and typ == EV_ABS:
                        remote.set_accel(code, value)
                    elif kind == "keys" and typ == EV_KEY:
                        remote.set_button(code, value)
        except OSError as exc:
            remote.set_error(f"lost {path}: {exc.strerror}")
        finally:
            os.close(fd)


class Handler(BaseHTTPRequestHandler):
    remote: Remote

    def do_GET(self) -> None:
        path = self.path.split("?", 1)[0]
        if path == "/api/state":
            body = json.dumps(self.remote.snapshot()).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Cache-Control", "no-store")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return
        if path in ("/", "/index.html"):
            page = WEB_ROOT / "index.html"
            if not page.exists():
                message = b"missing index.html"
                self.send_response(500)
                self.send_header("Content-Type", "text/plain; charset=utf-8")
                self.send_header("Content-Length", str(len(message)))
                self.end_headers()
                self.wfile.write(message)
                return
            body = page.read_bytes()
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Cache-Control", "no-cache")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return
        self.send_response(404)
        self.end_headers()

    def log_message(self, fmt: str, *args) -> None:
        return

    def handle_error(self, request, client_address) -> None:
        return


def main() -> None:
    remote = Remote()
    stop = threading.Event()
    threading.Thread(target=read_device, args=("Nintendo Wii Remote Accelerometer", remote, "accel", stop), daemon=True).start()
    threading.Thread(target=read_device, args=("Nintendo Wii Remote", remote, "keys", stop), daemon=True).start()
    Handler.remote = remote
    server = ThreadingHTTPServer((HOST, PORT), Handler)
    print(f"OpenWiimote view on http://127.0.0.1:{PORT}/", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        stop.set()
        server.shutdown()


if __name__ == "__main__":
    main()
