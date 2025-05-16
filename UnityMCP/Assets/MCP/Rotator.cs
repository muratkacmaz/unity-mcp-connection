using UnityEngine;

namespace MCP
{
    public class Rotator : MonoBehaviour
    {
        [Tooltip("Speed of rotation in degrees per second")]
        public float rotationSpeed = 30.0f;

        // Update is called once per frame
        void Update()
        {
            // Rotate the object around its Y-axis
            transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
        }
    }
}
