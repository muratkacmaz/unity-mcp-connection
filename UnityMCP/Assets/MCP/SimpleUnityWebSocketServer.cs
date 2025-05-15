using UnityEngine;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class SimpleUnityWebSocketServer : MonoBehaviour
{
    [SerializeField] private int port = 8090;
    private TcpListener server;
    private Thread serverThread;
    private bool isRunning = false;
    private readonly List<TcpClient> clients = new List<TcpClient>();

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

    private void StartServer()
    {
        if (isRunning) return;

        isRunning = true;
        serverThread = new Thread(ServerLoop);
        serverThread.IsBackground = true;
        serverThread.Start();

        Debug.Log($"WebSocket server started on port {port}");
    }

    private void StopServer()
    {
        if (!isRunning) return;

        isRunning = false;

        if (server != null)
        {
            server.Stop();
        }

        foreach (TcpClient client in clients.ToArray())
        {
            try
            {
                client.Close();
            }
            catch (Exception e)
            {
                Debug.LogError($"Error closing client: {e.Message}");
            }
        }

        clients.Clear();

        if (serverThread != null && serverThread.IsAlive)
        {
            serverThread.Join(1000);
            if (serverThread.IsAlive)
            {
                try
                {
                    serverThread.Abort();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error aborting server thread: {e.Message}");
                }
            }
        }

        Debug.Log("WebSocket server stopped");
    }

    private ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
    private List<Thread> activeThreads = new List<Thread>();
    private object threadsLock = new object();

    private void ServerLoop()
    {
        try
        {
            server = new TcpListener(IPAddress.Any, port);
            server.Start();

            Debug.Log($"Server listening on port {port}");

            while (isRunning)
            {
                try
                {
                    if (server.Pending())
                    {
                        TcpClient client = server.AcceptTcpClient();
                        Debug.Log("Client connected");

                        clients.Add(client);

                        // Clean up any completed threads
                        CleanupCompletedThreads();

                        Thread clientThread = new Thread(() => HandleClient(client));
                        clientThread.IsBackground = true;

                        lock (threadsLock)
                        {
                            activeThreads.Add(clientThread);
                        }

                        clientThread.Start();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error accepting client: {e.Message}");
                }

                Thread.Sleep(100);
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

            // Clean up all threads when shutting down
            CleanupAllThreads();
        }
    }

    private void CleanupCompletedThreads()
    {
        lock (threadsLock)
        {
            activeThreads.RemoveAll(t => !t.IsAlive);
        }
    }

    private void CleanupAllThreads()
    {
        lock (threadsLock)
        {
            foreach (var thread in activeThreads)
            {
                // Try to abort threads safely
                try
                {
                    if (thread.IsAlive)
                    {
                        thread.Join(1000); // Give it a second to clean up
                    }
                }
                catch
                {
                }
            }

            activeThreads.Clear();
        }
    }

// In your Update() method, add:
    private void Update()
    {
        // Process any actions queued for the main thread
        while (mainThreadActions.TryDequeue(out var action))
        {
            action();
        }
    }

// In HandleClient, use this for Unity operations:
    private void QueueOnMainThread(Action action)
    {
        mainThreadActions.Enqueue(action);
    }

    private void HandleClient(TcpClient client)
    {
        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[8192];
        int bytesRead;
        bool handshakeComplete = false;

        try
        {
            while (isRunning && client.Connected)
            {
                try
                {
                    bytesRead = stream.Read(buffer, 0, buffer.Length);

                    if (bytesRead == 0)
                    {
                        break;
                    }

                    if (!handshakeComplete)
                    {
                        // Process WebSocket handshake
                        string handshakeRequest = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                        // Use QueueOnMainThread for Unity Debug.Log calls
                        QueueOnMainThread(() => Debug.Log("Received handshake request"));

                        if (handshakeRequest.Contains("GET") && handshakeRequest.Contains("Upgrade: websocket"))
                        {
                            string key = Regex.Match(handshakeRequest, "Sec-WebSocket-Key: (.*)").Groups[1].Value
                                .Trim();
                            string acceptKey = AcceptKey(key);

                            string handshakeResponse =
                                "HTTP/1.1 101 Switching Protocols\r\n" +
                                "Upgrade: websocket\r\n" +
                                "Connection: Upgrade\r\n" +
                                "Sec-WebSocket-Accept: " + acceptKey + "\r\n\r\n";

                            byte[] responseBytes = Encoding.UTF8.GetBytes(handshakeResponse);
                            stream.Write(responseBytes, 0, responseBytes.Length);
                            handshakeComplete = true;

                            QueueOnMainThread(() => Debug.Log("Handshake completed"));
                        }
                        else
                        {
                            QueueOnMainThread(() => Debug.LogError("Invalid WebSocket handshake request"));
                            break;
                        }
                    }
                    else
                    {
                        // Process WebSocket message
                        string message = DecodeWebSocketMessage(buffer, bytesRead);
                        QueueOnMainThread(() => Debug.Log($"Received message: {message}"));

                        if (!string.IsNullOrEmpty(message))
                        {
                            try
                            {
                                JObject request = JObject.Parse(message);
                                // Process the request using the main thread for Unity operations
                                QueueOnMainThread(() => ProcessRequest(client, request));
                            }
                            catch (Exception e)
                            {
                                QueueOnMainThread(() => Debug.LogError($"Error processing message: {e.Message}"));
                                SendError(client, "Invalid JSON");
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    QueueOnMainThread(() => Debug.LogError($"Error handling client data: {e.Message}"));
                    break;
                }
            }
        }
        catch (Exception e)
        {
            QueueOnMainThread(() => Debug.LogError($"Client handler error: {e.Message}"));
        }
        finally
        {
            clients.Remove(client);
            client.Close();
            QueueOnMainThread(() => Debug.Log("Client disconnected"));
        }
    }

    private string AcceptKey(string key)
    {
        string magic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        string combined = key + magic;

        byte[] hashBytes;
        using (SHA1 sha1 = SHA1.Create())
        {
            hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(combined));
        }

        return Convert.ToBase64String(hashBytes);
    }

    private string DecodeWebSocketMessage(byte[] buffer, int length)
    {
        bool fin = (buffer[0] & 0x80) != 0;
        bool mask = (buffer[1] & 0x80) != 0;
        int opcode = buffer[0] & 0x0F;
        int offset = 2;
        int payloadLength = buffer[1] & 0x7F;

        if (opcode == 8) // Close
        {
            return null;
        }

        if (payloadLength == 126)
        {
            payloadLength = BitConverter.ToUInt16(new byte[] { buffer[3], buffer[2] }, 0);
            offset = 4;
        }
        else if (payloadLength == 127)
        {
            // 64-bit length is not fully supported here
            Debug.LogError("64-bit payload length not supported");
            return null;
        }

        if (mask)
        {
            byte[] maskBytes = new byte[4];
            Buffer.BlockCopy(buffer, offset, maskBytes, 0, 4);
            offset += 4;

            byte[] decoded = new byte[payloadLength];
            for (int i = 0; i < payloadLength; i++)
            {
                decoded[i] = (byte)(buffer[offset + i] ^ maskBytes[i % 4]);
            }

            return Encoding.UTF8.GetString(decoded);
        }
        else
        {
            return Encoding.UTF8.GetString(buffer, offset, payloadLength);
        }
    }

    private void SendWebSocketMessage(TcpClient client, string message)
    {
        NetworkStream stream = client.GetStream();
        byte[] payload = Encoding.UTF8.GetBytes(message);

        byte[] header;
        if (payload.Length < 126)
        {
            header = new byte[2];
            header[0] = 0x81; // FIN + Text opcode
            header[1] = (byte)payload.Length;
        }
        else if (payload.Length < 65536)
        {
            header = new byte[4];
            header[0] = 0x81; // FIN + Text opcode
            header[1] = 126;
            header[2] = (byte)(payload.Length >> 8);
            header[3] = (byte)(payload.Length & 0xFF);
        }
        else
        {
            header = new byte[10];
            header[0] = 0x81; // FIN + Text opcode
            header[1] = 127;
            // We're assuming payload length < 2^32
            header[2] = header[3] = header[4] = header[5] = 0;
            header[6] = (byte)(payload.Length >> 24);
            header[7] = (byte)(payload.Length >> 16);
            header[8] = (byte)(payload.Length >> 8);
            header[9] = (byte)(payload.Length & 0xFF);
        }

        stream.Write(header, 0, header.Length);
        stream.Write(payload, 0, payload.Length);
    }

private void ProcessRequest(TcpClient client, JObject request)
{
    string requestId = request["id"]?.ToString();
    if (string.IsNullOrEmpty(requestId))
    {
        SendError(client, "Missing request ID");
        return;
    }

    string action = request["action"]?.ToString();
    if (string.IsNullOrEmpty(action))
    {
        SendError(client, "Missing action");
        return;
    }

    // Replace UnityMainThreadDispatcher with your QueueOnMainThread method
    QueueOnMainThread(() =>
    {
        JObject response = new JObject();
        response["id"] = requestId;

        try
        {
            JObject result = null;

            switch (action)
            {
                case "createPrimitive":
                    result = HandleCreatePrimitive(request["params"] as JObject);
                    break;
                case "getSceneInfo":
                    result = HandleGetSceneInfo();
                    break;
                case "addComponent":
                    result = HandleAddComponent(request["params"] as JObject);
                    break;
                case "setTransform":
                    result = HandleSetTransform(request["params"] as JObject);
                    break;
                case "createEmpty":
                    result = HandleCreateEmpty(request["params"] as JObject);
                    break;
                case "deleteObject":
                    result = HandleDeleteObject(request["params"] as JObject);
                    break;
                case "setMaterial":
                    result = HandleSetMaterial(request["params"] as JObject);
                    break;
                case "instantiatePrefab":
                    result = HandleInstantiatePrefab(request["params"] as JObject);
                    break;
                case "createScript":
                    result = HandleCreateScript(request["params"] as JObject);
                    break;
                case "addScript":
                    result = HandleAddScript(request["params"] as JObject);
                    break;
                default:
                    SendError(client, $"Unknown action: {action}");
                    return;
            }

            response["result"] = result;
            SendResponse(client, response);
        }
        catch (Exception e)
        {
            Debug.LogError($"Error handling action {action}: {e.Message}");
            Debug.LogError(e.StackTrace);
            SendError(client, e.Message);
        }
    });
}
    private JObject HandleAddScript(JObject jObject)
    {
        // Create a result object
        JObject result = new JObject();

        try
        {
            // Parse required parameters from the input JSON
            string objectName = jObject["objectName"]?.ToString();
            string scriptName = jObject["scriptName"]?.ToString();

            // Check if required parameters are present
            if (string.IsNullOrEmpty(objectName) || string.IsNullOrEmpty(scriptName))
            {
                result["success"] = false;
                result["error"] = "Missing required parameters: objectName and scriptName are required.";
                return result;
            }

            // Find the target GameObject
            GameObject targetObject = GameObject.Find(objectName);
            if (targetObject == null)
            {
                result["success"] = false;
                result["error"] = $"GameObject '{objectName}' not found in the scene.";
                return result;
            }

            // Get the script type (assuming it's in the current assembly)
            Type scriptType = Type.GetType(scriptName + ", Assembly-CSharp");
            if (scriptType == null)
            {
                // Try to find the type in all loaded assemblies if not found in the main assembly
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    scriptType = assembly.GetType(scriptName);
                    if (scriptType != null)
                        break;
                }
            }

            // Check if script type was found
            if (scriptType == null)
            {
                result["success"] = false;
                result["error"] =
                    $"Script type '{scriptName}' not found. Make sure the script exists and has been compiled.";
                return result;
            }

            // Check if the script is a MonoBehaviour (required for components)
            if (!typeof(MonoBehaviour).IsAssignableFrom(scriptType))
            {
                result["success"] = false;
                result["error"] = $"Script '{scriptName}' is not a MonoBehaviour and cannot be added as a component.";
                return result;
            }

            // Check if the component already exists
            if (targetObject.GetComponent(scriptType) != null)
            {
                result["success"] = false;
                result["error"] = $"Component '{scriptName}' already exists on '{objectName}'.";
                return result;
            }

            // Parse optional properties if provided
            JObject properties = jObject["properties"] as JObject;

            // Add the component to the GameObject
            Component addedComponent = targetObject.AddComponent(scriptType);

            // Set properties if provided
            if (properties != null && addedComponent != null)
            {
                foreach (var property in properties)
                {
                    string propertyName = property.Key;
                    JToken propertyValue = property.Value;

                    // Get the property info
                    System.Reflection.PropertyInfo propInfo = scriptType.GetProperty(propertyName);
                    if (propInfo != null && propInfo.CanWrite)
                    {
                        // Convert the JSON value to the appropriate type and set the property
                        object convertedValue = ConvertJTokenToType(propertyValue, propInfo.PropertyType);
                        propInfo.SetValue(addedComponent, convertedValue);
                    }

                    // Try field if property not found
                    System.Reflection.FieldInfo fieldInfo = scriptType.GetField(propertyName);
                    if (fieldInfo != null)
                    {
                        // Convert the JSON value to the appropriate type and set the field
                        object convertedValue = ConvertJTokenToType(propertyValue, fieldInfo.FieldType);
                        fieldInfo.SetValue(addedComponent, convertedValue);
                    }
                }
            }

            // Return success
            result["success"] = true;
            result["message"] = $"Successfully added '{scriptName}' component to '{objectName}'.";
        }
        catch (System.Exception ex)
        {
            // Handle any exceptions
            result["success"] = false;
            result["error"] = $"Error adding script: {ex.Message}";
        }

        return result;
    }

// Helper method to convert JToken values to the appropriate type
    private object ConvertJTokenToType(JToken token, Type targetType)
    {
        if (token == null || token.Type == JTokenType.Null)
            return null;

        if (targetType == typeof(int) || targetType == typeof(Int32))
            return token.ToObject<int>();
        else if (targetType == typeof(float) || targetType == typeof(Single))
            return token.ToObject<float>();
        else if (targetType == typeof(double))
            return token.ToObject<double>();
        else if (targetType == typeof(bool) || targetType == typeof(Boolean))
            return token.ToObject<bool>();
        else if (targetType == typeof(string) || targetType == typeof(String))
            return token.ToString();
        else if (targetType == typeof(Vector2))
            return token.ToObject<Vector2>();
        else if (targetType == typeof(Vector3))
            return token.ToObject<Vector3>();
        else if (targetType == typeof(Vector4))
            return token.ToObject<Vector4>();
        else if (targetType == typeof(Quaternion))
            return token.ToObject<Quaternion>();
        else if (targetType == typeof(Color))
            return token.ToObject<Color>();
        else
            return token.ToObject(targetType);
    }

    private JObject HandleCreateScript(JObject parameters)
    {
        string scriptName = parameters["scriptName"].ToString();

        // Try to find the script type in all loaded assemblies
        Type scriptType = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            scriptType = assembly.GetType(scriptName);
            if (scriptType != null)
                break;
        }

        if (scriptType == null)
        {
            throw new Exception($"Script type '{scriptName}' not found");
        }

        // Ensure it's a MonoBehaviour-derived class (optional, can be removed if not needed)
        if (!typeof(MonoBehaviour).IsAssignableFrom(scriptType))
        {
            throw new Exception($"Script '{scriptName}' is not a MonoBehaviour");
        }

        // We do not create or attach it to any GameObject.
        // We can only modify static fields or properties
        if (parameters["properties"] != null)
        {
            JObject props = parameters["properties"] as JObject;
            foreach (var prop in props.Properties())
            {
                var property = scriptType.GetProperty(prop.Name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.CanWrite)
                {
                    object value = ConvertValue(prop.Value, property.PropertyType);
                    property.SetValue(null, value); // null for static
                    continue;
                }

                var field = scriptType.GetField(prop.Name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    object value = ConvertValue(prop.Value, field.FieldType);
                    field.SetValue(null, value); // null for static
                }
            }
        }

        return new JObject
        {
            ["success"] = true,
            ["scriptName"] = scriptName
        };
    }
    
    private object ConvertValue(JToken token, Type targetType)
    {
        if (targetType == typeof(Vector3) && token is JObject vecObj)
        {
            return new Vector3(
                vecObj["x"]?.Value<float>() ?? 0,
                vecObj["y"]?.Value<float>() ?? 0,
                vecObj["z"]?.Value<float>() ?? 0
            );
        }
        else if (targetType == typeof(Color) && token is JObject colorObj)
        {
            return new Color(
                colorObj["r"]?.Value<float>() ?? 0,
                colorObj["g"]?.Value<float>() ?? 0,
                colorObj["b"]?.Value<float>() ?? 0,
                colorObj["a"]?.Value<float>() ?? 1
            );
        }
        else if (targetType.IsEnum)
        {
            return Enum.Parse(targetType, token.ToString());
        }

        return token.ToObject(targetType);
    }

    private JObject HandleCreatePrimitive(JObject parameters)
    {
        string primitiveType = parameters["type"].ToString();
        string name = parameters["name"].ToString();
        Vector3 position = new Vector3();
        if (parameters["position"] != null)
        {
            JObject posObj = parameters["position"] as JObject;
            position.x = posObj["x"]?.Value<float>() ?? 0;
            position.y = posObj["y"]?.Value<float>() ?? 0;
            position.z = posObj["z"]?.Value<float>() ?? 0;
        }

        PrimitiveType type = (PrimitiveType)Enum.Parse(typeof(PrimitiveType), primitiveType, true);
        GameObject primitive = GameObject.CreatePrimitive(type);
        primitive.transform.position = position;
        primitive.name = name;

        Debug.Log($"Created primitive: {primitive.name} at position {position}");

        return new JObject
        {
            ["success"] = true,
            ["objectId"] = primitive.GetInstanceID(),
            ["name"] = primitive.name
        };
    }

    private JObject HandleGetSceneInfo()
    {
        JObject result = new JObject();
        JArray objects = new JArray();

        GameObject[] rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();

        foreach (GameObject obj in rootObjects)
        {
            objects.Add(new JObject
            {
                ["name"] = obj.name,
                ["id"] = obj.GetInstanceID(),
                ["position"] = new JObject
                {
                    ["x"] = obj.transform.position.x,
                    ["y"] = obj.transform.position.y,
                    ["z"] = obj.transform.position.z
                }
            });
        }

        result["success"] = true;
        result["objects"] = objects;
        result["sceneName"] = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        return result;
    }

    private JObject HandleAddComponent(JObject parameters)
    {
        string objectName = parameters["objectName"].ToString();
        string componentType = parameters["componentType"].ToString();

        GameObject targetObject = GameObject.Find(objectName);
        if (targetObject == null)
        {
            throw new Exception($"Object '{objectName}' not found");
        }

        Type type = Type.GetType($"UnityEngine.{componentType}, UnityEngine");
        if (type == null)
        {
            throw new Exception($"Component type '{componentType}' not found");
        }

        Component component = targetObject.AddComponent(type);

        return new JObject
        {
            ["success"] = true,
            ["componentId"] = component.GetInstanceID(),
            ["componentType"] = componentType
        };
    }

    private JObject HandleSetTransform(JObject parameters)
    {
        string objectName = parameters["objectName"].ToString();

        GameObject targetObject = GameObject.Find(objectName);
        if (targetObject == null)
        {
            throw new Exception($"Object '{objectName}' not found");
        }

        if (parameters["position"] != null)
        {
            JObject posObj = parameters["position"] as JObject;
            if (posObj != null)
            {
                Vector3 position = new Vector3(
                    posObj["x"]?.Value<float>() ?? targetObject.transform.position.x,
                    posObj["y"]?.Value<float>() ?? targetObject.transform.position.y,
                    posObj["z"]?.Value<float>() ?? targetObject.transform.position.z
                );
                targetObject.transform.position = position;
            }
        }

        if (parameters["rotation"] != null)
        {
            JObject rotObj = parameters["rotation"] as JObject;
            if (rotObj != null)
            {
                Vector3 rotation = new Vector3(
                    rotObj["x"]?.Value<float>() ?? targetObject.transform.eulerAngles.x,
                    rotObj["y"]?.Value<float>() ?? targetObject.transform.eulerAngles.y,
                    rotObj["z"]?.Value<float>() ?? targetObject.transform.eulerAngles.z
                );
                targetObject.transform.eulerAngles = rotation;
            }
        }

        if (parameters["scale"] != null)
        {
            JObject scaleObj = parameters["scale"] as JObject;
            if (scaleObj != null)
            {
                Vector3 scale = new Vector3(
                    scaleObj["x"]?.Value<float>() ?? targetObject.transform.localScale.x,
                    scaleObj["y"]?.Value<float>() ?? targetObject.transform.localScale.y,
                    scaleObj["z"]?.Value<float>() ?? targetObject.transform.localScale.z
                );
                targetObject.transform.localScale = scale;
            }
        }

        return new JObject
        {
            ["success"] = true,
            ["message"] = $"Updated transform of {objectName}"
        };
    }

    private JObject HandleCreateEmpty(JObject parameters)
    {
        string name = parameters["name"]?.ToString() ?? "New GameObject";
        Vector3 position = new Vector3();

        if (parameters["position"] != null)
        {
            JObject posObj = parameters["position"] as JObject;
            position.x = posObj["x"]?.Value<float>() ?? 0;
            position.y = posObj["y"]?.Value<float>() ?? 0;
            position.z = posObj["z"]?.Value<float>() ?? 0;
        }

        GameObject newObject = new GameObject(name);
        newObject.transform.position = position;

        return new JObject
        {
            ["success"] = true,
            ["objectId"] = newObject.GetInstanceID(),
            ["name"] = newObject.name
        };
    }

    private JObject HandleDeleteObject(JObject parameters)
    {
        string objectName = parameters["objectName"].ToString();

        GameObject targetObject = GameObject.Find(objectName);
        if (targetObject == null)
        {
            throw new Exception($"Object '{objectName}' not found");
        }

        Destroy(targetObject);

        return new JObject
        {
            ["success"] = true,
            ["message"] = $"Deleted object {objectName}"
        };
    }

    private JObject HandleSetMaterial(JObject parameters)
    {
        string objectName = parameters["objectName"].ToString();
        string materialName = parameters["materialName"].ToString();
        Color color = Color.white;

        if (parameters["color"] != null)
        {
            JObject colorObj = parameters["color"] as JObject;
            color = new Color(
                colorObj["r"]?.Value<float>() ?? 1.0f,
                colorObj["g"]?.Value<float>() ?? 1.0f,
                colorObj["b"]?.Value<float>() ?? 1.0f,
                colorObj["a"]?.Value<float>() ?? 1.0f
            );
        }

        GameObject targetObject = GameObject.Find(objectName);
        if (targetObject == null)
        {
            throw new Exception($"Object '{objectName}' not found");
        }

        Renderer renderer = targetObject.GetComponent<Renderer>();
        if (renderer == null)
        {
            throw new Exception($"Object '{objectName}' does not have a Renderer component");
        }

        // Create a new material with standard shader
        Material material = new Material(Shader.Find("Standard"));
        material.name = materialName;
        material.color = color;

        renderer.material = material;

        return new JObject
        {
            ["success"] = true,
            ["message"] = $"Set material for {objectName}"
        };
    }

    private JObject HandleInstantiatePrefab(JObject parameters)
    {
        string prefabPath = parameters["prefabPath"].ToString();
        Vector3 position = new Vector3();

        if (parameters["position"] != null)
        {
            JObject posObj = parameters["position"] as JObject;
            position.x = posObj["x"]?.Value<float>() ?? 0;
            position.y = posObj["y"]?.Value<float>() ?? 0;
            position.z = posObj["z"]?.Value<float>() ?? 0;
        }

        // Try to load the prefab from Resources folder
        UnityEngine.Object prefab = Resources.Load(prefabPath);
        if (prefab == null)
        {
            throw new Exception($"Prefab at path '{prefabPath}' not found in Resources folder");
        }

        GameObject instance = Instantiate(prefab as GameObject, position, Quaternion.identity);

        return new JObject
        {
            ["success"] = true,
            ["objectId"] = instance.GetInstanceID(),
            ["name"] = instance.name
        };
    }

    private void SendResponse(TcpClient client, JObject response)
    {
        try
        {
            string responseStr = JsonConvert.SerializeObject(response);
            Debug.Log($"Sending response: {responseStr}");
            SendWebSocketMessage(client, responseStr);
        }
        catch (Exception e)
        {
            Debug.LogError($"Error sending response: {e.Message}");
        }
    }

    private void SendError(TcpClient client, string errorMessage)
    {
        JObject response = new JObject
        {
            ["error"] = errorMessage
        };

        SendResponse(client, response);
    }
}