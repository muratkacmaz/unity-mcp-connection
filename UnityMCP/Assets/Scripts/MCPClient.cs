using System;
using System.Text;
using NativeWebSocket;
using UnityEngine;
using Newtonsoft.Json;

public class MCPClient : MonoBehaviour
{
    WebSocket websocket;

    async void Start()
    {
        websocket = new WebSocket("ws://localhost:8766");

        websocket.OnOpen += () =>
        {
            Debug.Log("✅ Connected to MCP Server");
            SendGetTools(); // example call on connect
        };

        websocket.OnError += (e) =>
        {
            Debug.LogError("WebSocket Error: " + e);
        };

        websocket.OnClose += (e) =>
        {
            Debug.Log("WebSocket closed");
        };

        websocket.OnMessage += (bytes) =>
        {
            var message = Encoding.UTF8.GetString(bytes);
            Debug.Log("📥 Received: " + message);

            // TODO: handle JSON-RPC messages here
        };

        await websocket.Connect();
    }

    async void SendGetTools()
    {
        var msg = new
        {
            jsonrpc = "2.0",
            method = "get_tools",
            id = 1
        };
        string json = JsonConvert.SerializeObject(msg);
        await websocket.SendText(json);
        Debug.Log("📤 Sent get_tools");
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        websocket?.DispatchMessageQueue();
#endif
    }

    private async void OnApplicationQuit()
    {
        await websocket.Close();
    }
}