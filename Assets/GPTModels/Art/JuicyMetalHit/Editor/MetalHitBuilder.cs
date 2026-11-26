using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace JuicyMetalHit.Editor
{
    public static class MetalHitBuilder
    {
        private const string Base = "Assets/JuicyMetalHit";
        private const string Out = Base + "/Generated";
        private const string PrefabPath = Out + "/JuicyMetalHit.prefab";

        [MenuItem("Tools/Juicy Metal Hit/Create Prefab", false, 10)]
        public static void CreatePrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
                !EditorUtility.DisplayDialog("Rebuild metal hit?", "This replaces the generated prefab and resets its particle settings. Generated materials are refreshed for the Built-in pipeline (custom material settings are reset).", "Rebuild", "Cancel")) return;
            var prefab = Build();
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            Debug.Log("Metal hit prefab ready: " + PrefabPath, prefab);
        }

        private static GameObject Build()
        {
            Directory.CreateDirectory(Out);
            AssetDatabase.Refresh();
            var shader = Shader.Find("JuicyMetalHit/Unlit Particle");
            if (shader == null || !shader.isSupported) throw new System.InvalidOperationException("JuicyMetalHit shader is missing or unsupported. Check shader import errors in Console and use the Built-in Render Pipeline.");
            var flash = MaterialFor("Flash", "Flash", shader, 5.0f, true);
            var glow = MaterialFor("Glow", "Glow", shader, 2.4f, true);
            var spark = MaterialFor("Spark", "Spark", shader, 4.0f, true);
            var ember = MaterialFor("Ember", "Glow", shader, 2.0f, true);
            var ring = MaterialFor("Ring", "Ring", shader, 2.0f, true);
            var smoke = MaterialFor("Smoke", "Smoke", shader, 1.0f, false);
            GameObject root = new GameObject("JuicyMetalHit");
            try
            {
                root.AddComponent<MetalHitFX>();
                var p = SystemOn(root, flash, 1, 1, 0.06f, 0.085f, 0, 0, 0.34f, 0.50f, 0, -1, 0);
                Fade(p, Keys(new Color(1f, 0.95f, 0.77f), new Color(1f, 0.38f, 0.045f), new Color(1f, 0.12f, 0.01f)),
                    Alpha(0, 1, .32f, .85f, 1, 0));
                Size(p, Curve(0, .55f, .16f, 1, 1, .3f));
                Rotate(p, 0, 360);

                p = Child(root, "01 - Hot glow", glow, 1, 2, .12f, .19f, 0, 0, .42f, .65f, 0, -1, 0);
                Fade(p, Keys(new Color(1, .61f, .16f), new Color(1, .23f, .025f), new Color(.8f, .08f, .01f)), Alpha(0, .8f, .25f, .5f, 1, 0));
                Size(p, Curve(0, .45f, .35f, 1, 1, 1.3f));

                p = Child(root, "02 - Needle sparks", spark, 32, 46, .22f, .52f, 5.0f, 11.5f, .017f, .036f, .82f, 66, 0);
                Stretch(p, 3.2f, .038f);
                Fade(p, Hot(), Alpha(0, 1, .48f, 1, .8f, .65f, 1, 0));
                Size(p, Curve(0, 1, .35f, .9f, .8f, .48f, 1, 0));
                Drag(p, .85f);

                p = Child(root, "03 - Wide ricochet", spark, 13, 22, .13f, .32f, 3.5f, 8.5f, .012f, .025f, .48f, 83, 0);
                Stretch(p, 2.6f, .025f);
                Fade(p, Hot(), Alpha(0, 1, .35f, .9f, 1, 0));
                Size(p, Curve(0, 1, .45f, .8f, 1, 0));
                Drag(p, 1.3f);

                p = Child(root, "04 - Cooling embers", ember, 15, 25, .48f, .95f, 1.2f, 4.2f, .025f, .062f, .72f, 70, .012f);
                Fade(p, Keys(new Color(1, .79f, .26f), new Color(1, .24f, .035f), new Color(.42f, .035f, .005f)), Alpha(0, 1, .2f, .85f, .75f, .4f, 1, 0));
                Size(p, Curve(0, 1, .3f, .7f, 1, .1f));
                Drag(p, 1.4f);
                var noise = p.noise; noise.enabled = true; noise.strength = .13f; noise.frequency = 2.5f;

                p = Child(root, "05 - Pressure flicker", ring, 1, 1, .09f, .14f, 0, 0, .35f, .42f, 0, -1, 0);
                Fade(p, Keys(new Color(1, .65f, .2f), new Color(1, .25f, .03f), new Color(.7f, .1f, .01f)), Alpha(0, .6f, .18f, .46f, 1, 0));
                Size(p, Curve(0, .25f, .4f, 1.3f, 1, 2.0f));
                Rotate(p, 0, 360);

                p = Child(root, "06 - Secondary crackle", spark, 5, 9, .12f, .28f, 2.5f, 6f, .012f, .023f, .65f, 65, .055f);
                Stretch(p, 2.4f, .03f);
                Fade(p, Hot(), Alpha(0, 1, .35f, .9f, 1, 0));
                Size(p, Curve(0, 1, .5f, .75f, 1, 0));
                Drag(p, 1.2f);

                p = Child(root, "07 - Trace smoke", smoke, 3, 5, .45f, .85f, .2f, .65f, .12f, .23f, -.10f, 48, .035f);
                Fade(p, Keys(new Color(.35f, .30f, .25f), new Color(.19f, .18f, .17f), new Color(.12f, .12f, .12f)), Alpha(0, 0, .16f, .23f, .5f, .14f, 1, 0));
                Size(p, Curve(0, .5f, .4f, 1.4f, 1, 2.2f));
                Rotate(p, 0, 360);
                var spin = p.rotationOverLifetime; spin.enabled = true; spin.z = new ParticleSystem.MinMaxCurve(-.6f, .6f);
                Drag(p, .55f);

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
            mat.renderQueue = additive ? 3000 : 2995;
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
            main.loop = false; main.duration = 1.1f; main.playOnAwake = true;
            main.startDelay = delay; main.startLifetime = new ParticleSystem.MinMaxCurve(lifeLo, lifeHi);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedLo, speedHi);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeLo, sizeHi);
            main.startColor = Color.white; main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 128; main.stopAction = ParticleSystemStopAction.None;
            var emission = p.emission; emission.enabled = true; emission.rateOverTime = 0; emission.rateOverDistance = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, lo, hi, 1, .01f) });
            var shape = p.shape; shape.enabled = angle >= 0;
            if (angle >= 0) { shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = angle; shape.radius = .018f; shape.radiusThickness = 1; shape.randomDirectionAmount = 0; }
            var renderer = p.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat; renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View; renderer.maxParticleSize = .6f;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
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
        private static GradientColorKey[] Keys(Color a, Color b, Color c) => new[] { new GradientColorKey(a, 0), new GradientColorKey(b, .43f), new GradientColorKey(c, 1) };
        private static GradientColorKey[] Hot() => Keys(new Color(1, .94f, .73f), new Color(1, .52f, .075f), new Color(1, .075f, .008f));
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

        [MenuItem("Tools/Juicy Metal Hit/Create Preview Scene", false, 11)]
        public static void CreatePreviewScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) prefab = Build();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Preview camera");
            var camera = cameraObject.AddComponent<Camera>(); cameraObject.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f, .032f, .045f); camera.allowHDR = true;
            cameraObject.transform.position = new Vector3(2.5f, 1.3f, 5.4f); cameraObject.transform.LookAt(new Vector3(0, -.2f, .55f));
            camera.fieldOfView = 48;
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube); plate.name = "Metal target";
            plate.transform.position = new Vector3(0, 0, -.13f); plate.transform.localScale = new Vector3(2.8f, 2.2f, .2f);
            var surface = AssetDatabase.LoadAssetAtPath<Material>(Out + "/PreviewMetal.mat");
            if (surface == null)
            {
                var shader = GraphicsSettings.currentRenderPipeline == null
                    ? Shader.Find("Standard")
                    : Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                if (shader != null)
                {
                    surface = new Material(shader);
                    if (surface.HasProperty("_BaseColor")) surface.SetColor("_BaseColor", new Color(.12f, .15f, .20f));
                    if (surface.HasProperty("_Color")) surface.SetColor("_Color", new Color(.12f, .15f, .20f));
                    if (surface.HasProperty("_Metallic")) surface.SetFloat("_Metallic", .45f);
                    if (surface.HasProperty("_Smoothness")) surface.SetFloat("_Smoothness", .55f);
                    if (surface.HasProperty("_Glossiness")) surface.SetFloat("_Glossiness", .55f);
                    AssetDatabase.CreateAsset(surface, Out + "/PreviewMetal.mat");
                }
            }
            if (surface != null) plate.GetComponent<Renderer>().sharedMaterial = surface;
            var light = new GameObject("Key light").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.3f;
            light.transform.rotation = Quaternion.Euler(35, 155, 0);
            var demo = new GameObject("Metal hit preview").AddComponent<MetalHitPreview>();
            demo.prefab = prefab.GetComponent<MetalHitFX>(); demo.transform.position = new Vector3(0, 0, -.03f);
            string scenePath = AssetDatabase.GenerateUniqueAssetPath(Out + "/MetalHitPreview.unity");
            EditorSceneManager.SaveScene(scene, scenePath); AssetDatabase.SaveAssets();
            Selection.activeGameObject = demo.gameObject;
            Debug.Log("Preview scene ready. Press Play to test.");
        }
    }
}
