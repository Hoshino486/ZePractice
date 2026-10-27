import argparse
import hashlib
import json
import time
from pathlib import Path

import frida

parser = argparse.ArgumentParser()
parser.add_argument("pid", type=int)
parser.add_argument("output")
parser.add_argument("--seconds", type=int, default=40)
args = parser.parse_args()
binary = Path(r"E:\mapper\chana\steamapps\common\Counter-Strike Global Offensive\game\csgo\bin\win64\server.dll")
if hashlib.sha256(binary.read_bytes()).hexdigest() != "9f31fc84164f7822e015dcd95ea958f9dd33d02308a646a2bee5c9b39bd6fc28":
    raise RuntimeError("Unverified server binary")
events = []
session = frida.attach(args.pid)
script = session.create_script(r"""
const server = Process.enumerateModules().find(module => module.path.toLowerCase().endsWith('\\game\\csgo\\bin\\win64\\server.dll'));
if (!server) throw new Error('Dedicated game module not found');
if (!server.path.toLowerCase().includes('e:\\mapper\\chana\\')) throw new Error('Unexpected target');
Interceptor.attach(server.base.add(0x298c70), {
    onEnter(parameters) {
        send({kind:'shot', time:Date.now(), shooter:parameters[0].toInt32(), weapon:parameters[1].toString(),
            inaccuracy:this.context.rsp.add(0x40).readFloat(), spread:this.context.rsp.add(0x48).readFloat()});
    }
});
send({kind:'ready', module:server.path});
""")

def record(message, data):
    events.append(message.get("payload", message))
    if len(events) <= 4:
        print(json.dumps(events[-1]), flush=True)

script.on("message", record)
try:
    script.load()
    time.sleep(args.seconds)
finally:
    session.detach()
    Path(args.output).write_text(json.dumps(events, indent=2), encoding="utf-8")
shots = [event for event in events if event.get("kind") == "shot"]
print(json.dumps({"shots":len(shots), "zero":sum(event["inaccuracy"] == 0 and event["spread"] == 0 for event in shots)}))
