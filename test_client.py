import asyncio
import websockets
import json

async def test():
    uri = "ws://localhost:8765"
    async with websockets.connect(uri) as websocket:
        # Send "get_tools" request
        request = {
            "jsonrpc": "2.0",
            "method": "get_tools",
            "id": 1
        }
        await websocket.send(json.dumps(request))
        print("📤 Sent get_tools")

        # Receive and print the response
        response = await websocket.recv()
        print(f"📥 Response: {response}")

asyncio.run(test())
