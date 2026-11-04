using System;
using UnityEngine;

namespace Marionette
{
    /// <summary>
    /// Owns the puppet's lifetime. Lives outside the puppet so it can rebuild it, which is how
    /// respawning at a checkpoint and changing size both work. Tuning values carry over.
    /// </summary>
    public class PuppetSpawner : MonoBehaviour
    {
        [Tooltip("Optional puppet already in the scene. If empty, one is built at this transform on Start.")]
        public MarionetteRig rig;
        public ControlInputSource input;
        [Tooltip("Height in metres for a puppet built on Start.")]
        public float startHeight = 0.12f;
        public MarionetteBuilder.Materials materials;

        public event Action<MarionetteRig> Spawned;

        void Start()
        {
            if (rig != null && rig.IsOutdated)
            {
                // Saved with an older puppet (head and center strings, no limb limits): rebuild it in place.
                Debug.Log("[Marionette] Puppet in this scene is from an older version; rebuilding it with hand and foot strings only.");
                Spawn(rig.transform.position, rig.height);
            }
            else if (rig != null)
                Adopt(rig);
            else
                Spawn(transform.position, startHeight);
        }

        /// <summary>Replace the current puppet with a new one standing on groundPoint.</summary>
        public MarionetteRig Spawn(Vector3 groundPoint, float height)
        {
            string tuning = null;
            if (rig != null)
            {
                var old = rig.GetComponent<MarionetteTuning>();
                if (old != null) tuning = JsonUtility.ToJson(old);
                // Deactivate first so its colliders don't shove the new puppet during this frame.
                rig.gameObject.SetActive(false);
                Destroy(rig.gameObject);
            }

            var built = MarionetteBuilder.Build(groundPoint, materials, new MarionetteBuilder.Settings { height = height });
            if (tuning != null)
            {
                var t = built.GetComponent<MarionetteTuning>();
                JsonUtility.FromJsonOverwrite(tuning, t);
                t.Apply();
            }
            Adopt(built);
            return built;
        }

        public MarionetteRig Respawn(Vector3 groundPoint) => Spawn(groundPoint, rig != null ? rig.height : startHeight);

        public MarionetteRig Resize(float height) => Spawn(rig.GroundPoint, height);

        void Adopt(MarionetteRig r)
        {
            rig = r;
            if (rig.GetComponent<StepTurn>() == null) rig.gameObject.AddComponent<StepTurn>(); // puppets saved before turning existed
            if (input != null)
            {
                rig.leftCross.input = input;
                rig.rightCross.input = input;
                input.puppetHeight = rig.height;
                input.ResetPose();
            }
            Spawned?.Invoke(rig);
        }
    }
}
