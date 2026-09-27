"""A client for EditSharp's remote API: JSON-RPC 2.0, one message per line, over a local pipe.

Start the app with `-- --api` (add `--headless` for no windows). The running app writes its address to
user://api.json; this finds it there, or takes a pipe name.

As a library:
    with Client.connect() as app:
        app.call("command.run", {"id": "timeline.seek", "args": {"at": 2.5}})
        clips = app.call("state.get", {"path": "timeline"})

From the command line:
    python Tools/editsharp_rpc.py state.get '{"path": "app"}'
    python Tools/editsharp_rpc.py --watch history.changed playback.position
"""
import argparse
import itertools
import json
import os
import platform
import socket
import sys


def address_file():
    system = platform.system()
    if system == "Windows":
        base = os.path.join(os.environ["APPDATA"], "Godot", "app_userdata")
    elif system == "Darwin":
        base = os.path.expanduser("~/Library/Application Support/Godot/app_userdata")
    else:
        base = os.path.join(os.environ.get("XDG_DATA_HOME", os.path.expanduser("~/.local/share")), "godot", "app_userdata")
    return os.path.join(base, "EditSharp GUI", "api.json")


class RpcError(Exception):
    def __init__(self, error):
        super().__init__(f"{error.get('code')}: {error.get('message')}")
        self.code = error.get("code")


class Client:
    def __init__(self, stream):
        self.stream = stream
        self.ids = itertools.count(1)
        self.events = []

    @classmethod
    def connect(cls, pipe=None):
        address = {}
        if pipe is None:
            with open(address_file(), encoding="utf-8") as f:
                address = json.load(f)
            pipe = address["pipe"]

        if platform.system() == "Windows":
            return cls(open(rf"\\.\pipe\{pipe}", "r+b", buffering=0))

        path = address.get("socket") or os.path.join(os.environ.get("TMPDIR", "/tmp"), f"CoreFxPipe_{pipe}")
        s = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        s.connect(path)
        return cls(s.makefile("rwb", buffering=0))

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.stream.close()

    def send(self, message):
        self.stream.write((json.dumps(message) + "\n").encode("utf-8"))

    def read(self):
        line = b""
        while not line.endswith(b"\n"):
            chunk = self.stream.read(1)
            if not chunk:
                raise ConnectionError("the app closed the connection")
            line += chunk
        return json.loads(line)

    def call(self, method, params=None):
        """Sends a request and returns its result; notifications that arrive meanwhile are kept in .events."""
        request_id = next(self.ids)
        self.send({"jsonrpc": "2.0", "id": request_id, "method": method, "params": params or {}})
        while True:
            message = self.read()
            if message.get("id") == request_id:
                if "error" in message:
                    raise RpcError(message["error"])
                return message.get("result")
            self.events.append(message)

    def command(self, command_id, args=None, project=None):
        params = {"id": command_id, "args": args or {}}
        if project:
            params["project"] = project
        return self.call("command.run", params)

    def state(self, path, project=None):
        params = {"path": path}
        if project:
            params["project"] = project
        return self.call("state.get", params)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("method", nargs="?")
    parser.add_argument("params", nargs="?", default="{}")
    parser.add_argument("--pipe")
    parser.add_argument("--watch", nargs="+", metavar="EVENT", help="subscribe and print events until interrupted")
    args = parser.parse_args()

    with Client.connect(args.pipe) as app:
        if args.method:
            print(json.dumps(app.call(args.method, json.loads(args.params)), indent=2))
        if args.watch:
            app.call("events.subscribe", {"names": args.watch})
            try:
                while True:
                    print(json.dumps(app.read()), flush=True)
            except KeyboardInterrupt:
                pass


if __name__ == "__main__":
    sys.exit(main())
