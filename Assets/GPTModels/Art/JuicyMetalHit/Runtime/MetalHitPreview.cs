using UnityEngine;

namespace JuicyMetalHit
{
    /// <summary>Optional demo only. No dependency on either Unity input package.</summary>
    public sealed class MetalHitPreview : MonoBehaviour
    {
        public MetalHitFX prefab;
        [Min(0.3f)] public float interval = 1.5f;
        public bool autoRepeat = true;
        private float next;

        private void Update()
        {
            if (autoRepeat && Time.time >= next) Strike();
        }

        private void Strike()
        {
            MetalHitFX.Spawn(prefab, transform.position, transform.forward);
            next = Time.time + interval;
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(18, 18, 240, 110), GUI.skin.box);
            GUILayout.Label("JUICY METAL HIT / VFX PREVIEW");
            if (GUILayout.Button("Strike metal")) Strike();
            autoRepeat = GUILayout.Toggle(autoRepeat, "Auto-repeat");
            GUILayout.EndArea();
        }
    }
}
