using UnityEngine;

namespace JuicyDirtHit
{
    /// <summary>Optional demo only. No dependency on either Unity input package.</summary>
    public sealed class DirtHitPreview : MonoBehaviour
    {
        public DirtHitFX prefab;
        [Min(0.3f)] public float interval = 1.0f;
        public bool autoRepeat = true;
        private float next;

        private void Update()
        {
            if (autoRepeat && Time.time >= next) Strike();
        }

        private void Strike()
        {
            DirtHitFX.Spawn(prefab, transform.position, transform.forward, 1f);
            next = Time.time + interval;
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(18, 18, 260, 110), GUI.skin.box);
            GUILayout.Label("JUICY DIRT HIT / VFX PREVIEW");
            if (GUILayout.Button("Strike dirt")) Strike();
            autoRepeat = GUILayout.Toggle(autoRepeat, "Auto-repeat");
            GUILayout.EndArea();
        }
    }
}
