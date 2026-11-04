using UnityEngine;

namespace Marionette
{
    /// <summary>One of the six rigid bodies of the puppet.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PuppetNode : MonoBehaviour
    {
        public PuppetNodeId id;

        Rigidbody m_Body;
        public Rigidbody Body => m_Body != null ? m_Body : m_Body = GetComponent<Rigidbody>();

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, 0.015f);
        }
    }
}
