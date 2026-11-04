using UnityEngine;

namespace Marionette
{
    /// <summary>
    /// An explicit attachment point on a node. Lives as a child transform so it can be moved
    /// in the editor to tune where a string pulls without rebuilding the puppet.
    /// </summary>
    public class PuppetAttachment : MonoBehaviour
    {
        public string attachmentId;
        public AttachmentType type;
        public PuppetNode node;
        [Tooltip("Max force this attachment holds before it lets go (suction / grapple). 0 = unbreakable.")]
        public float strength;

        /// <summary>Position in the node's local space, which is what joints want as an anchor.</summary>
        public Vector3 LocalToNode => node.transform.InverseTransformPoint(transform.position);

        void OnDrawGizmos()
        {
            Gizmos.color = type switch
            {
                AttachmentType.ExternalControl => Color.white,
                AttachmentType.Grapple => Color.red,
                AttachmentType.Suction => Color.cyan,
                _ => Color.gray
            };
            Gizmos.DrawSphere(transform.position, 0.008f);
        }
    }
}
