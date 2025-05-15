#!/usr/bin/env python
import sys
import logging
import asyncio
import websockets
import json
from datetime import datetime

# Import the official MCP SDK
from mcp.server.fastmcp import FastMCP, Context

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s',
    handlers=[
        logging.StreamHandler(sys.stderr)  # Log to stderr so Claude Desktop can capture it
    ]
)
logger = logging.getLogger("unity-mcp")

# Unity connection settings
UNITY_WS_PORT = 8090
unity_websocket = None
is_unity_connected = False
pending_requests = {}

# Create the MCP server
mcp = FastMCP("Unity MCP Server")

# Function to connect to Unity WebSocket server
async def connect_to_unity():
    global unity_websocket, is_unity_connected
    
    try:
        logger.info(f"Attempting to connect to Unity on ws://localhost:{UNITY_WS_PORT}")
        unity_websocket = await websockets.connect(f"ws://localhost:{UNITY_WS_PORT}", ping_interval=None)
        logger.info("Connected to Unity WebSocket server")
        is_unity_connected = True
        
        # Start background task to listen for messages from Unity
        asyncio.create_task(listen_to_unity())
        
        return True
    except Exception as e:
        logger.error(f"Failed to connect to Unity: {str(e)}")
        is_unity_connected = False
        return False

# Function to listen for messages from Unity
async def listen_to_unity():
    global unity_websocket, is_unity_connected, pending_requests
    
    try:
        async for message in unity_websocket:
            try:
                response = json.loads(message)
                logger.info(f"Received message from Unity: {response}")
                
                # Check if there's a pending request for this response
                request_id = response.get('id')
                if request_id and request_id in pending_requests:
                    future = pending_requests.pop(request_id)
                    future.set_result(response)
            except json.JSONDecodeError:
                logger.error(f"Received invalid JSON from Unity")
    except websockets.exceptions.ConnectionClosed:
        logger.info("Unity WebSocket connection closed")
        is_unity_connected = False
        
        # Try to reconnect after a delay
        await asyncio.sleep(5)
        asyncio.create_task(connect_to_unity())

# Function to send message to Unity
async def send_to_unity(message):
    global unity_websocket, is_unity_connected, pending_requests
    
    if not is_unity_connected:
        # Try to connect if we're not connected
        success = await connect_to_unity()
        if not success:
            raise ConnectionError("Not connected to Unity")
    
    message_id = str(datetime.now().timestamp())
    message_with_id = {
        **message,
        "id": message_id
    }
    
    # Create a future to wait for the response
    future = asyncio.get_running_loop().create_future()
    pending_requests[message_id] = future
    
    # Send the message
    await unity_websocket.send(json.dumps(message_with_id))
    
    # Wait for the response with a timeout
    try:
        response = await asyncio.wait_for(future, timeout=10.0)
        return response
    except asyncio.TimeoutError:
        pending_requests.pop(message_id, None)
        raise TimeoutError("Timeout waiting for Unity response")

# Connect to Unity at startup (non-blocking)
async def startup():
    try:
        success = await connect_to_unity()
        if success:
            logger.info("Successfully connected to Unity")
        else:
            logger.warning("Failed to connect to Unity, will continue without Unity connection")
    except Exception as e:
        logger.error(f"Error connecting to Unity: {str(e)}")
        logger.warning("Continuing without Unity connection")

# Define Unity MCP tools using the decorator syntax
@mcp.tool()
async def unity_create_primitive(ctx: Context, type: str, name: str ,position: dict = None) -> str:
    """
    Create a primitive GameObject in Unity
    
    Args:
        type: The type of primitive to create (Cube, Sphere, Cylinder, Plane, Capsule, Quad)
        position: The position in 3D space to place the primitive (optional)
    """
    try:
        if position is None:
            position = {"x": 0, "y": 0, "z": 0}
            
        response = await send_to_unity({
            "action": "createPrimitive",
            "params": {
                "type": type,
                "position": position,
                "name": name
            }
        })
        
        result = response.get("result", {})
        if "error" in response:
            return f"Failed to create primitive: {response['error']}"
            
        return f"Created {type} at position ({position.get('x', 0)}, {position.get('y', 0)}, {position.get('z', 0)})"
    except Exception as e:
        logger.error(f"Error in create_primitive: {str(e)}")
        return f"Failed to create primitive: {str(e)}"

@mcp.tool()
async def unity_get_scene_info(ctx: Context) -> str:
    """
    Get information about the current Unity scene
    """
    try:
        response = await send_to_unity({
            "action": "getSceneInfo"
        })
        
        result = response.get("result", {})
        if "error" in response:
            return f"Failed to get scene info: {response['error']}"
            
        scene_name = result.get("sceneName", "Unknown")
        objects = result.get("objects", [])
        
        info = f"Scene: {scene_name}\n\nObjects:\n"
        for obj in objects:
            pos = obj.get("position", {})
            info += f"- {obj.get('name')}: Position ({pos.get('x', 0)}, {pos.get('y', 0)}, {pos.get('z', 0)})\n"
            
        return info
    except Exception as e:
        logger.error(f"Error in get_scene_info: {str(e)}")
        return f"Failed to get scene info: {str(e)}"

@mcp.tool()
async def unity_add_component(ctx: Context, objectName: str, componentType: str) -> str:
    """
    Add a component to a GameObject in Unity
    
    Args:
        objectName: The name of the GameObject to add the component to
        componentType: The type of component to add (e.g., Rigidbody, BoxCollider)
    """
    try:
        response = await send_to_unity({
            "action": "addComponent",
            "params": {
                "objectName": objectName,
                "componentType": componentType
            }
        })
        
        result = response.get("result", {})
        if "error" in response:
            return f"Failed to add component: {response['error']}"
            
        return f"Added {componentType} to {objectName}"
    except Exception as e:
        logger.error(f"Error in add_component: {str(e)}")
        return f"Failed to add component: {str(e)}"

@mcp.tool()
async def unity_set_transform(ctx: Context, objectName: str, position: dict = None, rotation: dict = None, scale: dict = None) -> str:
    """
    Update the position, rotation, or scale of a GameObject in Unity
    
    Args:
        objectName: The name of the GameObject to update
        position: The new position (optional)
        rotation: The new rotation in Euler angles (optional)
        scale: The new scale (optional)
    """
    try:
        response = await send_to_unity({
            "action": "setTransform",
            "params": {
                "objectName": objectName,
                "position": position,
                "rotation": rotation,
                "scale": scale
            }
        })
        
        result = response.get("result", {})
        if "error" in response:
            return f"Failed to set transform: {response['error']}"
            
        return f"Updated transform of {objectName}"
    except Exception as e:
        logger.error(f"Error in set_transform: {str(e)}")
        return f"Failed to set transform: {str(e)}"

@mcp.tool()
async def unity_create_empty(ctx: Context, name: str = "New GameObject", position: dict = None) -> str:
    """
    Create an empty GameObject in Unity
    
    Args:
        name: The name of the new GameObject (optional)
        position: The position in 3D space to place the GameObject (optional)
    """
    try:
        if position is None:
            position = {"x": 0, "y": 0, "z": 0}
        
        response = await send_to_unity({
            "action": "createEmpty",
            "params": {
                "name": name,
                "position": position
            }
        })
        
        result = response.get("result", {})
        if "error" in response:
            return f"Failed to create empty object: {response['error']}"
            
        return f"Created empty GameObject named {name}"
    except Exception as e:
        logger.error(f"Error in create_empty: {str(e)}")
        return f"Failed to create empty object: {str(e)}"

@mcp.tool()
async def unity_delete_object(ctx: Context, objectName: str) -> str:
    """
    Delete a GameObject in Unity
    
    Args:
        objectName: The name of the GameObject to delete
    """
    try:
        response = await send_to_unity({
            "action": "deleteObject",
            "params": {
                "objectName": objectName
            }
        })
        
        result = response.get("result", {})
        if "error" in response:
            return f"Failed to delete object: {response['error']}"
            
        return f"Deleted object {objectName}"
    except Exception as e:
        logger.error(f"Error in delete_object: {str(e)}")
        return f"Failed to delete object: {str(e)}"

@mcp.tool()
async def unity_set_material(ctx: Context, objectName: str, materialName: str, color: dict = None) -> str:
    """
    Set the material of a GameObject in Unity
    
    Args:
        objectName: The name of the GameObject to set the material for
        materialName: The name of the material
        color: The color of the material (RGBA, values 0-1) (optional)
    """
    try:
        if color is None:
            color = {"r": 1.0, "g": 1.0, "b": 1.0, "a": 1.0}
        
        response = await send_to_unity({
            "action": "setMaterial",
            "params": {
                "objectName": objectName,
                "materialName": materialName,
                "color": color
            }
        })
        
        result = response.get("result", {})
        if "error" in response:
            return f"Failed to set material: {response['error']}"
            
        return f"Set material {materialName} for {objectName}"
    except Exception as e:
        logger.error(f"Error in set_material: {str(e)}")
        return f"Failed to set material: {str(e)}"

@mcp.tool()
async def unity_instantiate_prefab(ctx: Context, prefabPath: str, position: dict = None) -> str:
    """
    Instantiate a prefab in Unity
    
    Args:
        prefabPath: The path to the prefab in the Resources folder (without the .prefab extension)
        position: The position in 3D space to place the prefab (optional)
    """
    try:
        if position is None:
            position = {"x": 0, "y": 0, "z": 0}
        
        response = await send_to_unity({
            "action": "instantiatePrefab",
            "params": {
                "prefabPath": prefabPath,
                "position": position
            }
        })
        
        result = response.get("result", {})
        if "error" in response:
            return f"Failed to instantiate prefab: {response['error']}"
            
        return f"Instantiated prefab {prefabPath}"
    except Exception as e:
        logger.error(f"Error in instantiate_prefab: {str(e)}")
        return f"Failed to instantiate prefab: {str(e)}"
        

@mcp.tool()
async def unity_create_script(ctx: Context, objectName: str, scriptName: str, properties: dict = None) -> str:
    """
    Create a script (MonoBehaviour) to a GameObject in Unity at runtime
    
    Args:
        objectName: The name of the GameObject to add the script to
        scriptName: The fully qualified name of the script class (e.g., 'RotateScript')
        properties: Optional dictionary of property values to set on the script
    """
    try:
        response = await send_to_unity({
            "action": "createScript",
            "params": {
                "objectName": objectName,
                "scriptName": scriptName,
                "properties": properties
            }
        })
        
        if "error" in response:
            return f"Failed to add script: {response['error']}"
            
        result = response.get("result", {})
        return f"Added script {scriptName} to {objectName}"
    except Exception as e:
        logger.error(f"Error in add_script: {str(e)}")
        return f"Failed to add script: {str(e)}"
        
        
@mcp.tool()
async def unity_add_script(ctx: Context, objectName: str, componentType: str) -> str:
    """
    Add a script to a GameObject in Unity
    
    Args:
        objectName: The name of the GameObject to add the component to
        componentType: The type of component to add (e.g., Rigidbody, BoxCollider)
    """
    try:
        response = await send_to_unity({
            "action": "addScript",
            "params": {
                "objectName": objectName,
                "scriptName": componentType
            }
        })
        
        result = response.get("result", {})
        if "error" in response:
            return f"Failed to add component: {response['error']}"
            
        return f"Added {componentType} to {objectName}"
    except Exception as e:
        logger.error(f"Error in add_component: {str(e)}")
        return f"Failed to add component: {str(e)}"


# Start the MCP server using run() with stdio transport
if __name__ == "__main__":
    logger.info("Starting Unity MCP server with official SDK...")
    
    # Run with stdio transport
    mcp.run(transport="stdio")
