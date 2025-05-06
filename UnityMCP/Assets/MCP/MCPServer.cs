using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class MCPServer : MonoBehaviour
{
    [SerializeField] private int port = 8090;
    private TcpListener server;
    private Thread serverThread;
    private bool isRunning = false;
    
    private Dictionary<string, Func<JObject, JObject>> toolHandlers = new Dictionary<string, Func<JObject, JObject>>();
    
    void Awake()
    {
        // Register Unity-specific tool handlers
        RegisterToolHandlers();
    }
    
    void OnEnable()
    {
        StartServer();
    }
    
    void OnDisable()
    {
        StopServer();
    }
    
    void OnDestroy()
    {
        StopServer();
    }
    
    private void RegisterToolHandlers()
    {
        // Register your tool handlers here
        toolHandlers["unity_create_primitive"] = CreatePrimitive;
        toolHandlers["unity_get_scene_info"] = GetSceneInfo;
        toolHandlers["unity_create_empty_object"] = CreateEmptyObject;
        toolHandlers["unity_add_component"] = AddComponent;
    }
    
private JObject GetSceneInfo(JObject parameters)
{
    JObject result = new JObject();
    JArray objects = new JArray();
    
    UnityMainThreadDispatcher.Instance.Enqueue(() => {
        GameObject[] rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
        
        foreach (GameObject obj in rootObjects)
        {
            objects.Add(new JObject {
                ["name"] = obj.name,
                ["id"] = obj.GetInstanceID(),
                ["position"] = new JObject {
                    ["x"] = obj.transform.position.x,
                    ["y"] = obj.transform.position.y,
                    ["z"] = obj.transform.position.z
                }
            });
        }
        
        result["objects"] = objects;
        result["name"] = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
    });
    
    return result;
}

private JObject CreateEmptyObject(JObject parameters)
{
    string name = parameters["name"]?.ToString() ?? "New Object";
    
    int objectId = 0;
    UnityMainThreadDispatcher.Instance.Enqueue(() => {
        GameObject newObject = new GameObject(name);
        objectId = newObject.GetInstanceID();
    });
    
    return new JObject {
        ["success"] = true,
        ["object_id"] = objectId,
        ["message"] = $"Created empty object named '{name}'"
    };
}

private JObject AddComponent(JObject parameters)
{
    string objectName = parameters["object_name"].ToString();
    string componentType = parameters["component_type"].ToString();
    bool success = false;
    string message = "";
    
    UnityMainThreadDispatcher.Instance.Enqueue(() => {
        GameObject targetObject = GameObject.Find(objectName);
        
        if (targetObject != null)
        {
            try
            {
                Type type = Type.GetType($"UnityEngine.{componentType}, UnityEngine");
                if (type != null)
                {
                    targetObject.AddComponent(type);
                    success = true;
                    message = $"Added {componentType} to {objectName}";
                }
                else
                {
                    message = $"Component type {componentType} not found";
                }
            }
            catch (Exception e)
            {
                message = $"Error adding component: {e.Message}";
            }
        }
        else
        {
            message = $"Object '{objectName}' not found";
        }
    });
    
    return new JObject {
        ["success"] = success,
        ["message"] = message
    };
}
    private JObject CreatePrimitive(JObject parameters)
    {
        string primitiveType = parameters["type"].ToString();
        Vector3 position = new Vector3();
        
        if (parameters["position"] != null)
        {
            JObject posObj = (JObject)parameters["position"];
            position.x = posObj["x"].Value<float>();
            position.y = posObj["y"].Value<float>();
            position.z = posObj["z"].Value<float>();
        }
        
        // Execute on main thread since Unity API requires it
        UnityMainThreadDispatcher.Instance.Enqueue(() => {
            PrimitiveType type = (PrimitiveType)Enum.Parse(typeof(PrimitiveType), primitiveType, true);
            GameObject primitive = GameObject.CreatePrimitive(type);
            primitive.transform.position = position;
        });
        
        return new JObject {
            ["success"] = true,
            ["message"] = $"Created {primitiveType} at position ({position.x}, {position.y}, {position.z})"
        };
    }
    
    private void StartServer()
    {
        if (isRunning) return;
        
        isRunning = true;
        serverThread = new Thread(ServerLoop);
        serverThread.Start();
        
        Debug.Log($"MCP Server started on port {port}");
    }
    
    private void StopServer()
    {
        if (!isRunning) return;
        
        isRunning = false;
        if (server != null)
        {
            server.Stop();
        }
        
        if (serverThread != null && serverThread.IsAlive)
        {
            serverThread.Join(1000); // Wait 1 second for thread to terminate
            if (serverThread.IsAlive)
            {
                serverThread.Abort(); // Force abort if it doesn't terminate cleanly
            }
        }
        
        Debug.Log("MCP Server stopped");
    }
    
    private void ServerLoop()
    {
        try
        {
            server = new TcpListener(IPAddress.Loopback, port);
            server.Start();
            
            while (isRunning)
            {
                if (server.Pending())
                {
                    TcpClient client = server.AcceptTcpClient();
                    Thread clientThread = new Thread(() => HandleClient(client));
                    clientThread.Start();
                }
                Thread.Sleep(100); // Small delay to prevent CPU hogging
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Server error: {e.Message}");
        }
        finally
        {
            if (server != null)
            {
                server.Stop();
            }
        }
    }
    
    private void HandleClient(TcpClient client)
    {
        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[4096];
        int bytesRead;
        
        try
        {
            while (isRunning && client.Connected && (bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                JObject request = JObject.Parse(message);
                
                JObject response = ProcessRequest(request);
                
                string responseStr = JsonConvert.SerializeObject(response);
                byte[] responseBytes = Encoding.UTF8.GetBytes(responseStr);
                stream.Write(responseBytes, 0, responseBytes.Length);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Client handler error: {e.Message}");
        }
        finally
        {
            client.Close();
        }
    }
    
    private JObject ProcessRequest(JObject request)
    {
        try
        {
            string method = request["method"].ToString();
            
            // Handle MCP protocol methods
            if (method == "mcp.list_tools")
            {
                return HandleListTools();
            }
            else if (method == "mcp.run_tool")
            {
                return HandleRunTool(request["params"] as JObject);
            }
            
            return new JObject {
                ["error"] = "Method not supported"
            };
        }
        catch (Exception e)
        {
            return new JObject {
                ["error"] = e.Message
            };
        }
    }
    
    private JObject HandleListTools()
    {
        JArray tools = new JArray();
        
        // Add tool definitions
        tools.Add(new JObject {
            ["name"] = "unity_create_primitive",
            ["description"] = "Creates a primitive GameObject in the scene",
            ["parameters"] = new JObject {
                ["type"] = "object",
                ["properties"] = new JObject {
                    ["type"] = new JObject {
                        ["type"] = "string",
                        ["enum"] = new JArray { "Cube", "Sphere", "Capsule", "Cylinder", "Plane", "Quad" }
                    },
                    ["position"] = new JObject {
                        ["type"] = "object",
                        ["properties"] = new JObject {
                            ["x"] = new JObject { ["type"] = "number" },
                            ["y"] = new JObject { ["type"] = "number" },
                            ["z"] = new JObject { ["type"] = "number" }
                        }
                    }
                },
                ["required"] = new JArray { "type" }
            }
        });
        
        // Add more tool definitions as needed
        
        return new JObject {
            ["tools"] = tools
        };
    }
    
    private JObject HandleRunTool(JObject toolParams)
    {
        string toolName = toolParams["tool_name"].ToString();
        JObject parameters = toolParams["parameters"] as JObject;
        
        if (toolHandlers.TryGetValue(toolName, out var handler))
        {
            return handler(parameters);
        }
        
        return new JObject {
            ["error"] = $"Tool '{toolName}' not found or not implemented"
        };
    }
}