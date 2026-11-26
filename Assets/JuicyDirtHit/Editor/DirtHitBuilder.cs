using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace JuicyDirtHit.Editor
{
    public static class DirtHitBuilder
    {
        private const string Base = "Assets/JuicyDirtHit";
        private const string Out = Base + "/Generated";
        private const string PrefabPath = Out + "/JuicyDirtHit.prefab";

        [MenuItem("Tools/Juicy Dirt Hit/Create Prefab", false, 20)]
        public static void CreatePrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
                !EditorUtility.DisplayDialog("Rebuild dirt hit?", "This replaces the generated prefab and resets its particle settings. Generated materials are refreshed for the Built-in pipeline (custom material settings are reset).", "Rebuild", "Cancel")) return;
            var prefab = Build();
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            Debug.Log("Dirt hit prefab ready: " + PrefabPath, prefab);
        }

        private static GameObject Build()
        {
            Directory.CreateDirectory(Out);
            AssetDatabase.Refresh();
            var shader = Shader.Find("JuicyDirtHit/Unlit Particle");
            if (shader == null || !shader.isSupported) throw new System.InvalidOperationException("JuicyDirtHit shader is missing or unsupported. Check shader import errors in Console and use the Built-in Render Pipeline.");
            var dust = MaterialFor("Dust", "Dust", shader, 1.0f, false);
            var mist = MaterialFor("Mist", "Mist", shader, 1.05f, false);
            var chunk = MaterialFor("Chunk", "Chunk", shader, 1.0f, false);
            var pebble = MaterialFor("Pebble", "Pebble", shader, 1.0f, false);
            var ring = MaterialFor("Ring", "Ring", shader, 0.85f, false);

            GameObject root = new GameObject("JuicyDirtHit");
            try
            {
                root.AddComponent<DirtHitFX>();

                var p = SystemOn(root, dust, 1, 1, 0.18f, 0.25f, 0f, 0f, 0.35f, 0.50f, 0f, -1, 0f);
                Fade(p, Keys(new Color(.72f, .58f, .37f), new Color(.54f, .40f, .23f), new Color(.36f, .28f, .18f)), Alpha(0, .85f, .45f, .65f, 1, 0));
                Size(p, Curve(0, .50f, .18f, 1.0f, 1, 1.7f));
                Rotate(p, 0, 360);

                p = Child(root, "01 - Dirt chunks", chunk, 10, 16, .28f, .60f, 2.5f, 5.2f, .08f, .15f, .95f, 42f, 0f);
                Fade(p, Keys(new Color(.53f,.40f,.25f), new Color(.45f,.34f,.21f), new Color(.31f,.24f,.16f)), Alpha(0, 1, .7f, .92f, 1, 0));
                Size(p, Curve(0, 1f, .75f, .92f, 1, .78f));
                Drag(p, .18f);
                Rotate(p, 0, 360);
                var noise = p.noise; noise.enabled = true; noise.strength = .08f; noise.frequency = 1.4f;
                var limit = p.limitVelocityOverLifetime; limit.separateAxes = false;
                var collision = p.collision; collision.enabled = true; collision.type = ParticleSystemCollisionType.World; collision.mode = ParticleSystemCollisionMode.Collision3D; collision.dampen = .35f; collision.bounce = .25f; collision.lifetimeLoss = .08f; collision.collidesWith = ~0;

                p = Child(root, "02 - Pebble spray", pebble, 18, 28, .22f, .48f, 3.8f, 7.0f, .025f, .05f, 1.05f, 56f, 0f);
                Stretch(p, 1.45f, .05f);
                Fade(p, Keys(new Color(.60f,.47f,.31f), new Color(.43f,.32f,.21f), new Color(.25f,.19f,.14f)), Alpha(0, 1, .7f, .88f, 1, 0));
                Size(p, Curve(0, 1f, 1, .82f));
                Drag(p, .12f);
                collision = p.collision; collision.enabled = true; collision.type = ParticleSystemCollisionType.World; collision.mode = ParticleSystemCollisionMode.Collision3D; collision.dampen = .25f; collision.bounce = .18f; collision.lifetimeLoss = .1f; collision.collidesWith = ~0;

                p = Child(root, "03 - Fine dust plume", dust, 16, 24, .45f, .90f, 1.0f, 2.5f, .20f, .34f, -.06f, 58f, .01f);
                Fade(p, Keys(new Color(.67f,.55f,.36f), new Color(.49f,.39f,.25f), new Color(.30f,.25f,.20f)), Alpha(0, .20f, .12f, .82f, .58f, .50f, 1, 0));
                Size(p, Curve(0, .35f, .32f, .9f, 1, 1.85f));
                Drag(p, .35f);
                noise = p.noise; noise.enabled = true; noise.strength = .28f; noise.frequency = 0.7f;
                var vel = p.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local; vel.orbitalZ = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);

                p = Child(root, "04 - Ground mist", mist, 6, 10, .70f, 1.25f, .30f, 1.1f, .22f, .46f, -.02f, 70f, .02f);
                Fade(p, Keys(new Color(.56f,.47f,.34f), new Color(.38f,.32f,.24f), new Color(.22f,.20f,.18f)), Alpha(0, .12f, .15f, .48f, .55f, .30f, 1, 0));
                Size(p, Curve(0, .5f, .45f, 1.25f, 1, 2.1f));
                Rotate(p, 0, 360);
                Drag(p, .26f);
                noise = p.noise; noise.enabled = true; noise.strength = .34f; noise.frequency = .45f;

                p = Child(root, "05 - Dust ring", ring, 1, 1, .24f, .34f, 0f, 0f, .26f, .34f, 0f, -1f, 0f);
                Fade(p, Keys(new Color(.67f,.55f,.36f), new Color(.48f,.37f,.25f), new Color(.31f,.24f,.18f)), Alpha(0, .18f, .16f, .12f, 1, 0));
                Size(p, Curve(0, .35f, .45f, 1.4f, 1, 1.9f));
                Rotate(p, 0, 360);

                p = Child(root, "06 - Secondary debris", pebble, 6, 10, .20f, .45f, 2.8f, 5.2f, .018f, .036f, 1.0f, 35f, .045f);
                Fade(p, Keys(new Color(.55f,.43f,.28f), new Color(.41f,.31f,.21f), new Color(.26f,.20f,.15f)), Alpha(0, 1, .65f, .86f, 1, 0));
                Size(p, Curve(0, 1, 1, .78f));
                Drag(p, .12f);
                collision = p.collision; collision.enabled = true; collision.type = ParticleSystemCollisionType.World; collision.mode = ParticleSystemCollisionMode.Collision3D; collision.dampen = .25f; collision.bounce = .16f; collision.lifetimeLoss = .12f; collision.collidesWith = ~0;

                p = Child(root, "07 - Settling dust", mist, 8, 12, .85f, 1.45f, .16f, .55f, .10f, .20f, -.01f, 78f, .06f);
                Fade(p, Keys(new Color(.42f,.36f,.28f), new Color(.28f,.25f,.22f), new Color(.16f,.16f,.16f)), Alpha(0, .04f, .15f, .28f, .52f, .18f, 1, 0));
                Size(p, Curve(0, .45f, .45f, 1.15f, 1, 1.7f));
                Rotate(p, 0, 360);
                Drag(p, .2f);
                noise = p.noise; noise.enabled = true; noise.strength = .18f; noise.frequency = .55f;

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                return prefab;
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Material MaterialFor(string name, string texture, Shader shader, float intensity, bool additive)
        {
            string path = Out + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            string texPath = Base + "/Textures/" + texture + ".png";
            var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (importer == null) throw new FileNotFoundException("Missing particle texture", texPath);
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            var mat = existing != null ? existing : new Material(shader) { name = name };
            mat.shader = shader;
            mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            mat.SetColor("_Tint", Color.white);
            mat.SetFloat("_Intensity", intensity);
            mat.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            mat.renderQueue = 3000;
            if (existing == null) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            return mat;
        }

        private static ParticleSystem Child(GameObject root, string name, Material mat, short lo, short hi,
            float lifeLo, float lifeHi, float speedLo, float speedHi, float sizeLo, float sizeHi, float gravity, float angle, float delay)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            return SystemOn(go, mat, lo, hi, lifeLo, lifeHi, speedLo, speedHi, sizeLo, sizeHi, gravity, angle, delay);
        }

        private static ParticleSystem SystemOn(GameObject go, Material mat, short lo, short hi,
            float lifeLo, float lifeHi, float speedLo, float speedHi, float sizeLo, float sizeHi, float gravity, float angle, float delay)
        {
            var p = go.AddComponent<ParticleSystem>();
            p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = p.main;
            main.loop = false; main.duration = 1.6f; main.playOnAwake = true;
            main.startDelay = delay; main.startLifetime = new ParticleSystem.MinMaxCurve(lifeLo, lifeHi);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedLo, speedHi);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeLo, sizeHi);
            main.startColor = Color.white; main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 192; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; main.stopAction = ParticleSystemStopAction.None;
            var emission = p.emission; emission.enabled = true; emission.rateOverTime = 0; emission.rateOverDistance = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, lo, hi, 1, .01f) });
            var shape = p.shape; shape.enabled = angle >= 0;
            if (angle >= 0) { shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = angle; shape.radius = .022f; shape.radiusThickness = 1; shape.randomDirectionAmount = 0f; }
            var renderer = p.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat; renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View; renderer.maxParticleSize = .8f;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.SetActiveVertexStreams(new System.Collections.Generic.List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV });
            return p;
        }

        private static void Stretch(ParticleSystem p, float length, float velocity)
        {
            var r = p.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch;
            r.lengthScale = length; r.velocityScale = velocity; r.cameraVelocityScale = 0;
        }
        private static void Drag(ParticleSystem p, float drag)
        {
            var m = p.limitVelocityOverLifetime; m.enabled = true; m.limit = 100f; m.dampen = 0f; m.drag = drag;
            m.multiplyDragByParticleSize = false; m.multiplyDragByParticleVelocity = true;
        }
        private static void Rotate(ParticleSystem p, float lo, float hi)
        {
            var m = p.main; m.startRotation = new ParticleSystem.MinMaxCurve(lo * Mathf.Deg2Rad, hi * Mathf.Deg2Rad);
        }
        private static void Size(ParticleSystem p, AnimationCurve curve)
        {
            var m = p.sizeOverLifetime; m.enabled = true; m.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }
        private static AnimationCurve Curve(params float[] pairs)
        {
            var keys = new Keyframe[pairs.Length / 2];
            for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(pairs[i * 2], pairs[i * 2 + 1]);
            return new AnimationCurve(keys);
        }
        private static GradientColorKey[] Keys(Color a, Color b, Color c) => new[] { new GradientColorKey(a, 0), new GradientColorKey(b, .45f), new GradientColorKey(c, 1) };
        private static GradientAlphaKey[] Alpha(params float[] pairs)
        {
            var keys = new GradientAlphaKey[pairs.Length / 2];
            for (int i = 0; i < keys.Length; i++) keys[i] = new GradientAlphaKey(pairs[i * 2 + 1], pairs[i * 2]);
            return keys;
        }
        private static void Fade(ParticleSystem p, GradientColorKey[] colors, GradientAlphaKey[] alpha)
        {
            var g = new Gradient(); g.SetKeys(colors, alpha);
            var m = p.colorOverLifetime; m.enabled = true; m.color = new ParticleSystem.MinMaxGradient(g);
        }

        [MenuItem("Tools/Juicy Dirt Hit/Create Preview Scene", false, 21)]
        public static void CreatePreviewScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) prefab = Build();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var camera = Camera.main;
            if (camera == null)
            {
                var camGo = new GameObject("Main Camera");
                camera = camGo.AddComponent<Camera>();
                camGo.tag = "MainCamera";
            }
            camera.transform.position = new Vector3(0f, 1.1f, -4.4f);
            camera.transform.rotation = Quaternion.Euler(8f, 0f, 0f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.67f, 0.83f);

            var light = Object.FindObjectOfType<Light>();
            if (light != null)
            {
                light.transform.rotation = Quaternion.Euler(50f, -25f, 0f);
                light.intensity = 1.1f;
            }
            else
            {
                var lightGo = new GameObject("Directional Light");
                light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(50f, -25f, 0f);
                light.intensity = 1.1f;
            }

            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "Ground";
            plane.transform.localScale = new Vector3(2f, 1f, 2f);
            var planeRenderer = plane.GetComponent<Renderer>();
            if (planeRenderer != null)
            {
                var planeMat = new Material(Shader.Find("Standard"));
                planeMat.color = new Color(0.50f, 0.42f, 0.28f);
                planeRenderer.sharedMaterial = planeMat;
            }

            var anchor = new GameObject("Impact Anchor");
            anchor.transform.position = new Vector3(0f, 0.04f, 0f);
            anchor.transform.rotation = Quaternion.FromToRotation(Vector3.forward, Vector3.up);

            var preview = anchor.AddComponent<DirtHitPreview>();
            preview.prefab = prefab.GetComponent<DirtHitFX>();

            Selection.activeGameObject = anchor;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("Created dirt hit preview scene. Enter Play mode and use the preview UI.");
        }
    }
}
