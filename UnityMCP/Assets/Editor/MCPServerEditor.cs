using UnityEngine;
using UnityEditor;

public class MCPServerWindow : EditorWindow
{
    private bool isServerRunning = false;
    private string serverStatus = "Server not started";

    [MenuItem("Window/MCP Server Window")]
    public static void ShowWindow()
    {
        GetWindow<MCPServerWindow>("MCP Server");
    }

    void OnGUI()
    {
        GUILayout.Label("MCP Server Control", EditorStyles.boldLabel);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Status", serverStatus);

        EditorGUILayout.Space();

        if (Application.isPlaying)
        {
            if (!isServerRunning)
            {
                if (GUILayout.Button("Start Server"))
                {
                    isServerRunning = true;
                    serverStatus = "Server running";
                }
            }
            else
            {
                if (GUILayout.Button("Stop Server"))
                {
                    isServerRunning = false;
                    serverStatus = "Server stopped";
                }
            }
        }
        else
        {
            EditorGUILayout.HelpBox("Enter Play Mode to start the MCP server", MessageType.Info);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Configuration");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Claude Desktop Configuration:");
        EditorGUILayout.TextArea(GetClaudeConfigJson(), GUILayout.Height(100));
    }

    private string GetClaudeConfigJson()
    {
        return @"{
  ""mcpServers"": {
    ""unity-mcp"": {
      ""serverUrl"": ""http://localhost:8090""
    }
  }
}";
    }
}