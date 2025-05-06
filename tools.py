tools = {
    "spawn_object": {
        "description": "Spawn an object in the Unity scene.",
        "parameters": {
            "type": "object",
            "properties": {
                "name": {"type": "string"},
                "position": {
                    "type": "object",
                    "properties": {
                        "x": {"type": "number"},
                        "y": {"type": "number"},
                        "z": {"type": "number"}
                    },
                    "required": ["x", "y", "z"]
                }
            },
            "required": ["name", "position"]
        }
    }
}
