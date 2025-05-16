using UnityEngine;

namespace MCP
{
    public class FloatingObject : MonoBehaviour
    {
        [Tooltip("Maximum distance to move up/down")]
        public float floatHeight = 0.5f;
        
        [Tooltip("Speed of the floating motion")]
        public float floatSpeed = 1.0f;
        
        // The original y position of the object
        private float originalY;
        
        // Use this for initialization
        void Start()
        {
            // Store the original y position
            originalY = transform.position.y;
        }
        
        // Update is called once per frame
        void Update()
        {
            // Calculate the new y position using a sine wave
            float newY = originalY + Mathf.Sin(Time.time * floatSpeed) * floatHeight;
            
            // Update the object's position
            Vector3 position = transform.position;
            position.y = newY;
            transform.position = position;
        }
    }
}
