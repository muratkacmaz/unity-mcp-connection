# mcp_server.py

import asyncio
import websockets
import json
from fastapi import FastAPI, Request
import uvicorn
import threading

clients = set()

# -- WebSocket MCP Server --
async def handle_connection(websocket):
    clients.add(websocket)
    print("🟢 Unity connected")

    try:
        async for message in websocket:
            print("📥 Received:", message)
            data = json.loads(message)
            if data.get("method") == "get_tools":
                await websocket.send(json.dumps({
                    "jsonrpc": "2.0",
                    "id": data["id"],
                    "result": ["spawn_object"]
                }))
    finally:
        clients.remove(websocket)
        print("🔴 Unity disconnected")

async def start_ws_server():
    print("🚀 Starting MCP WebSocket server on ws://localhost:8765")
    async with websockets.serve(handle_connection, "localhost", 8766):
        await asyncio.Future()  # keep alive

# -- FastAPI Web Interface --
app = FastAPI()

@app.post("/message")
async def send_command(req: Request):
    data = await req.json()
    text = data.get("text", "").lower()

    # simple example: extract position
    if "spawn" in text and "cube" in text:
        x, y, z = 0, 0, 0
        import re
        pos_match = re.search(r"\(([-\d.]+),\s*([-\d.]+),\s*([-\d.]+)\)", text)
        if pos_match:
            x, y, z = map(float, pos_match.groups())

        command = {
            "jsonrpc": "2.0",
            "method": "spawn_object",
            "params": {
                "name": "GPTCube",
                "position": {"x": x, "y": y, "z": z}
            },
            "id": 10
        }

        for ws in clients:
            await ws.send(json.dumps(command))

        return {"status": "sent", "position": (x, y, z)}
    
    return {"status": "ignored", "text": text}

# -- Launch both servers (WebSocket + HTTP) --
def start_http_server():
    uvicorn.run(app, host="localhost", port=8000)

def start_servers():
    threading.Thread(target=start_http_server, daemon=True).start()
    asyncio.run(start_ws_server())

if __name__ == "__main__":
    start_servers()
