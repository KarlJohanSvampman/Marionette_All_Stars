using System.Collections;
using Marionette.Course;
using UnityEngine;

namespace Marionette.Room
{
    /// <summary>
    /// A place the puppet can be beamed to, shown as a pin on the globe. Stages live far from the room and
    /// stay switched off until the puppet arrives. The room's platform is the home destination.
    /// </summary>
    public class BeamDestination : MonoBehaviour
    {
        public string displayName = "Somewhere";
        [Range(-90f, 90f)] public float latitude;
        [Range(-180f, 180f)] public float longitude;
        [Tooltip("Where the puppet stands on arrival.")]
        public Transform spawnPoint;
        [Tooltip("This stage's course (checkpoints, falls), switched on while the puppet is here.")]
        public CourseManager course;
        [Tooltip("Puppet height on arrival, metres. 0 = keep its size.")]
        public float puppetHeight;
        [Tooltip("The room's platform: the puppet comes home here.")]
        public bool isHome;
        [Tooltip("Switched on when the puppet arrives and off when it leaves. Empty for the room.")]
        public GameObject stage;
    }

    /// <summary>
    /// Beams whoever is on the platform to the destination chosen on the globe, in a column of light.
    /// From a stage the puppet can be beamed onward or home from anywhere; from the room it has to be
    /// standing on the platform.
    /// </summary>
    public class Beamer : MonoBehaviour
    {
        public PuppetSpawner spawner;
        public BeamDestination home;
        [Tooltip("Platform top radius, metres: the puppet must be within it to be beamed from the room.")]
        public float platformRadius = 0.5f;
        public float beamSeconds = 1.2f;
        public Material beamMaterial;

        public BeamDestination Current { get; private set; }
        public bool Busy { get; private set; }
        public string Status { get; private set; } = "";

        void Start()
        {
            Current = home;
            foreach (var d in FindObjectsByType<BeamDestination>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!d.isHome && d.stage != null) d.stage.SetActive(false);
        }

        /// <summary>Is the puppet standing on the platform?</summary>
        public bool PuppetOnPlatform()
        {
            var rig = spawner != null ? spawner.rig : null;
            if (rig == null || home == null) return false;
            Vector3 c = rig.CenterOfMass, p = home.spawnPoint.position;
            return Vector2.Distance(new Vector2(c.x, c.z), new Vector2(p.x, p.z)) < platformRadius && Mathf.Abs(c.y - p.y) < rig.height * 1.5f;
        }

        public bool Beam(BeamDestination dest)
        {
            if (Busy) return false;
            if (dest == null) { Status = "Pick a place on the globe"; return false; }
            if (dest == Current && !dest.isHome) { Status = "Already there"; return false; }
            if (Current == home && !PuppetOnPlatform()) { Status = "Nobody on the platform"; return false; }
            StartCoroutine(Run(dest));
            return true;
        }

        IEnumerator Run(BeamDestination dest)
        {
            Busy = true;
            Status = $"Beaming to {dest.displayName}...";
            var from = spawner.rig != null ? spawner.rig.GroundPoint : transform.position;
            float h = spawner.rig != null ? spawner.rig.height : spawner.startHeight;
            StartCoroutine(Column(from, h));
            yield return new WaitForSeconds(beamSeconds * 0.5f);

            if (dest.stage != null) dest.stage.SetActive(true);
            var old = Current;
            float height = dest.puppetHeight > 0f ? dest.puppetHeight : h;
            spawner.Spawn(dest.spawnPoint.position, height);
            if (old != null && old != dest)
            {
                if (old.course != null) old.course.enabled = false;
                if (old.stage != null && !old.isHome) old.stage.SetActive(false);
            }
            if (dest.course != null) { dest.course.enabled = true; dest.course.Restart(); }
            Current = dest;
            StartCoroutine(Column(dest.spawnPoint.position, height));
            yield return new WaitForSeconds(beamSeconds * 0.5f);
            Status = $"Arrived: {dest.displayName}";
            Busy = false;
        }

        IEnumerator Column(Vector3 ground, float height)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "BeamColumn";
            Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<Renderer>();
            if (beamMaterial != null) r.sharedMaterial = beamMaterial;
            var block = new MaterialPropertyBlock();
            float width = height * 0.8f, tall = height * 3f;
            for (float t = 0f; t < beamSeconds; t += Time.deltaTime)
            {
                float k = t / beamSeconds;
                float a = Mathf.Sin(k * Mathf.PI);
                go.transform.position = ground + Vector3.up * (tall * 0.5f);
                go.transform.localScale = new Vector3(width * (0.3f + a), tall * 0.5f, width * (0.3f + a));
                block.SetColor("_BaseColor", new Color(0.5f, 0.9f, 1f, 0.6f * a));
                r.SetPropertyBlock(block);
                yield return null;
            }
            Destroy(go);
        }
    }
}
