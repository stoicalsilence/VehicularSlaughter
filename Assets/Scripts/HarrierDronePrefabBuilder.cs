// HARrier Drone V3 — editor-only procedural enemy model builder.
// INSTALL: Put this ONE file in Assets/Editor/. Delete earlier versions of
// HarrierDronePrefabBuilder.cs first; do NOT keep duplicate copies of the class.
// The upgrade command changes ART ONLY on the existing HarrierDrone prefab.
// No gun/muzzle/projectile components are created.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class HarrierDronePrefabBuilder
{
    // Change only if your current Harrier Drone prefab lives elsewhere.
    private const string RootFolder = "Assets/HarrierDrone";
    private const string MaterialFolder = RootFolder + "/Materials";
    private const string MeshFolder = RootFolder + "/Meshes";
    private const string V3MeshFolder = MeshFolder + "/V3";
    private const string PrefabPath = RootFolder + "/HarrierDrone.prefab";
    private const string VisualName = "Drone Visuals";
    private const int EnemyLayer = 7;
    private const int RadialSides = 12;

    private sealed class Colors
    {
        internal Material shadow, deepMetal, armor, paleArmor, gunmetal;
        internal Material visor, redEye, cyan, deepCyan, caution, flash;
    }

    private struct Slice
    {
        internal float y;
        internal float scale;
        internal Slice(float yValue, float scaleValue) { y = yValue; scale = scaleValue; }
    }

    [MenuItem("Tools/Horde Shooter/Harrier Drone/Upgrade Existing Model to V3")]
    public static void UpgradeModel()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            EditorUtility.DisplayDialog("Harrier Drone V3",
                "No existing prefab found at:\n" + PrefabPath +
                "\n\nIf the drone already exists elsewhere, change PrefabPath " +
                "at the top of this script. Otherwise use 'Create New V3 Prefab'.", "OK");
            return;
        }

        PrepareFolders();
        Colors colors = CreateColors();
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null) throw new InvalidOperationException("Could not load Harrier Drone prefab.");

        bool changed = false;
        try
        {
            HordeFlyingAttack flying = root.GetComponent<HordeFlyingAttack>();
            if (flying == null || root.GetComponent<HordeEnemy>() == null)
                throw new InvalidOperationException(
                    "Existing prefab must have HordeEnemy AND HordeFlyingAttack on its root.");

            BuildAndAttachVisuals(root, flying, colors);
            GameObject result = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (result == null) throw new InvalidOperationException("Could not save upgraded prefab.");
            changed = true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        AssetDatabase.SaveAssets();
        if (changed)
        {
            SelectPrefab();
            Debug.Log("Harrier Drone V3: upgraded visuals IN PLACE. AI, " +
                      "health, collider, prefab GUID and spawn configuration were retained.");
        }
    }

    [MenuItem("Tools/Horde Shooter/Harrier Drone/Create New V3 Prefab")]
    public static void CreatePrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            EditorUtility.DisplayDialog("Harrier Drone V3",
                "A prefab already exists. Use 'Upgrade Existing Model to V3' " +
                "to preserve your component tuning and scene references.", "OK");
            return;
        }

        PrepareFolders();
        Colors colors = CreateColors();
        GameObject root = new GameObject("Harrier Drone");
        root.layer = EnemyLayer;

        try
        {
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.mass = 4f;
            body.drag = 0.1f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
            collider.direction = 1;
            collider.radius = 0.88f;
            collider.height = 1.7f;
            // Wings/ducts are decorative. Keep a single simple physics collider.

            HordeEnemy enemy = root.AddComponent<HordeEnemy>();
            HordeFlyingAttack flying = root.AddComponent<HordeFlyingAttack>();
            BuildAndAttachVisuals(root, flying, colors);

            SerializedObject enemySO = new SerializedObject(enemy);
            SetFloat(enemySO, "maxHealth", 22f);
            SetFloat(enemySO, "xpDropValue", 14f);
            SetFloat(enemySO, "contactDamage", 0f);
            SetFloat(enemySO, "vehicleContactDamageMultiplier", 0f);
            SetFloat(enemySO, "knockbackResistancePercent", 0.22f);
            SetBool(enemySO, "allowEliteVariant", true);
            SetBool(enemySO, "allowEmpVariant", false);
            SerializedProperty flash = enemySO.FindProperty("hitFlashMaterial");
            if (flash != null) flash.objectReferenceValue = colors.flash;
            enemySO.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject flyingSO = new SerializedObject(flying);
            SetInt(flyingSO, "xpGroundLayers", 1);  // Set to your terrain layer in Inspector.
            flyingSO.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (saved == null) throw new InvalidOperationException("Could not create Harrier Drone prefab.");
            AssetDatabase.SaveAssets();
            RegisterWithManager(saved.GetComponent<HordeEnemy>());
            SelectPrefab();
            Debug.Log("Harrier Drone V3: new enemy prefab created at " + PrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void BuildAndAttachVisuals(GameObject enemyRoot,
        HordeFlyingAttack flying, Colors c)
    {
        // Only the named visual child is destroyed. The prefab root, its behavior,
        // hitbox, custom difficulty tuning, external references, etc. stay intact.
        Transform oldVisuals = enemyRoot.transform.Find(VisualName);
        if (oldVisuals != null) Object.DestroyImmediate(oldVisuals.gameObject);
        Transform visuals = Child(enemyRoot.transform, VisualName).transform;

        BuildStrikeHull(visuals, c);
        BuildHoverEngines(visuals, c);
        BuildTailAndArmor(visuals, c);
        Transform warning = BuildDiveWarning(visuals, c);

        // Merge all stationary parts by material. The warning must stay separate
        // so HordeFlyingAttack can switch it on/off while telegraphing the dive.
        CombineStaticMeshes(visuals, warning);

        SerializedObject so = new SerializedObject(flying);
        SerializedProperty rootField = so.FindProperty("visualRoot");
        SerializedProperty warningField = so.FindProperty("windupIndicator");
        SerializedProperty rotorField = so.FindProperty("rotorPivots");
        if (rootField == null || warningField == null)
            throw new InvalidOperationException("HordeFlyingAttack is missing the expected art fields.");
        rootField.objectReferenceValue = visuals;
        warningField.objectReferenceValue = warning.gameObject;
        if (rotorField != null) rotorField.arraySize = 0;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // MODEL FACES +Z. The flight AI already rotates the Rigidbody towards
    // velocity, so the wedge and nose naturally lead during a diving attack.
    private static void BuildStrikeHull(Transform t, Colors c)
    {
        // Distinctive shape: small aft body; broad, HEAVY, sloped armored front.
        Vector2[] outline = {
            P(-0.77f, -0.78f), P(-1.01f, -0.43f),
            P(-0.99f, 0.26f), P(-0.70f, 0.91f),
            P(-0.37f, 1.16f), P(0.37f, 1.16f),
            P(0.70f, 0.91f), P(0.99f, 0.26f),
            P(1.01f, -0.43f), P(0.77f, -0.78f)
        };
        Mesh darkCore = Hull("CoreHull", outline, new[] {
            S(-0.46f, 0.78f), S(-0.38f, 0.91f),
            S(-0.11f, 1.0f), S(0.16f, 0.98f), S(0.24f, 0.89f)
        });
        Part(t, "Pressure-resistant inner body", darkCore, c.deepMetal);
        Mesh upperArmor = Hull("UpperArmor", outline, new[] {
            S(-0.08f, 0.96f), S(0.10f, 1.05f),
            S(0.33f, 0.94f), S(0.43f, 0.73f)
        });
        Part(t, "Swept upper armor cowling", upperArmor, c.armor);

        // Front bumper is a WIDE solid metal blade, deliberately not a gun barrel.
        // The front edge is the strike point of the enemy.
        Vector2[] prowShape = {
            P(-0.66f, -0.02f), P(-0.65f, -0.24f),
            P(-0.37f, -0.42f), P(0.37f, -0.42f),
            P(0.65f, -0.24f), P(0.66f, -0.02f),
            P(0.51f, 0.13f), P(-0.51f, 0.13f)
        };
        Part(t, "Forward impact ram (solid shield)",
            Plate("ArmorRam", prowShape, 1.015f, 1.435f), c.paleArmor);
        Part(t, "Ram underside reinforcement",
            Plate("RamUnder", new[] {
                P(-0.48f, -0.41f), P(0.48f, -0.41f),
                P(0.31f, -0.49f), P(-0.31f, -0.49f)
            }, 1.055f, 1.37f), c.shadow);

        // Recessed angular black face and TWO downward-slanted, angry eye slits.
        // Flat faceted panels rather than lens circles -> clearly hostile.
        Vector2[] visorOutline = {
            P(-0.55f, 0.07f), P(0.55f, 0.07f),
            P(0.48f, 0.28f), P(-0.48f, 0.28f)
        };
        Part(t, "Deep recessed visor", Plate("Visor", visorOutline,
            1.418f, 1.463f), c.visor);

        Vector2[] leftEye = {
            P(-0.47f, 0.207f), P(-0.10f, 0.145f),
            P(-0.10f, 0.096f), P(-0.45f, 0.148f)
        };
        Vector2[] rightEye = {
            P(0.10f, 0.145f), P(0.47f, 0.207f),
            P(0.45f, 0.148f), P(0.10f, 0.096f)
        };
        Part(t, "Left hostile sensor slit",
            Plate("EyeLeft", leftEye, 1.461f, 1.476f), c.redEye);
        Part(t, "Right hostile sensor slit",
            Plate("EyeRight", rightEye, 1.461f, 1.476f), c.redEye);

        // Thick angular brows shade the red lenses, like a striking predator.
        // They also reinforce that the face is armored, not a projectile weapon.
        Part(t, "Left brow plate",
            Plate("BrowLeft", new[] {
                P(-0.58f, 0.225f), P(-0.08f, 0.143f),
                P(-0.08f, 0.235f), P(-0.58f, 0.334f)
            }, 1.45f, 1.55f), c.gunmetal);
        Part(t, "Right brow plate",
            Plate("BrowRight", new[] {
                P(0.08f, 0.143f), P(0.58f, 0.225f),
                P(0.58f, 0.334f), P(0.08f, 0.235f)
            }, 1.45f, 1.55f), c.gunmetal);

        // A shallow dorsal spine connects nose -> tail. No protruding tube.
        Beam(t, "Dorsal armored spine", new Vector3(0f, 0.395f, -0.67f),
            new Vector3(0f, 0.48f, 0.62f), 0.14f, 0.21f, c.gunmetal);
        Box(t, "Reinforced nose-top shield",
            new Vector3(0f, 0.313f, 0.77f),
            new Vector3(0.72f, 0.10f, 0.50f), c.paleArmor);

        // Layered plates beside the ram form short blade-like cheeks, not cannons.
        for (int side = -1; side <= 1; side += 2)
        {
            Vector2[] cheek = {
                P(side * 0.49f, 0.46f),
                P(side * 0.92f, 0.20f),
                P(side * 1.07f, 0.73f),
                P(side * 0.75f, 1.30f),
                P(side * 0.55f, 1.34f)
            };
            Part(t, side < 0 ? "Left ram-cheek armor" : "Right ram-cheek armor",
                Hull(side < 0 ? "CheekLeft" : "CheekRight", cheek,
                    new[] { S(-0.24f, 0.95f), S(-0.13f, 1.0f), S(0.14f, 0.85f) }),
                c.gunmetal);
        }

        // Visible lower belly armor doubles as a forward-facing contact area.
        Part(t, "Belly reinforcement",
            Lathe("BellyHousing", new[] {
                P(0.48f, -0.51f), P(0.64f, -0.45f),
                P(0.68f, -0.37f), P(0.49f, -0.30f)
            }, true), c.shadow, new Vector3(0f, 0f, -0.12f));
        Part(t, "Blue power-strip below nose",
            Plate("CoreStatus", new[] {
                P(-0.26f, -0.37f), P(0.26f, -0.37f),
                P(0.21f, -0.30f), P(-0.21f, -0.30f)
            }, 1.438f, 1.452f), c.deepCyan);
    }

    private static void BuildHoverEngines(Transform t, Colors c)
    {
        for (int side = -1; side <= 1; side += 2)
        {
            string sideName = side < 0 ? "Left" : "Right";
            // Engines are further BACK than the nose; they visibly propel a ram.
            GameObject pod = Child(t, sideName + " angled anti-grav duct");
            pod.transform.localPosition = new Vector3(side * 1.64f, -0.06f, -0.22f);
            pod.transform.localRotation = Quaternion.Euler(0f, 0f, -side * 13f);
            Transform e = pod.transform;

            // Primary outer intake: a thick, 12-faceted HOLLOW ring.
            Part(e, "Thick outer impact-resistant duct",
                Ring("ArmoredDuct", new[] {
                    P(0.39f, -0.24f), P(0.53f, -0.17f),
                    P(0.55f, 0.15f), P(0.47f, 0.25f),
                    P(0.33f, 0.25f), P(0.30f, 0.14f),
                    P(0.30f, -0.17f), P(0.35f, -0.24f)
                }), c.paleArmor);
            Part(e, "Jet inner mechanical wall",
                Ring("InnerMotorBand", new[] {
                    P(0.32f, -0.14f), P(0.345f, -0.10f),
                    P(0.345f, 0.10f), P(0.315f, 0.15f),
                    P(0.290f, 0.15f), P(0.290f, -0.14f)
                }), c.deepMetal);
            Part(e, "Recessed turbine floor",
                Lathe("TurbineBasin", new[] {
                    P(0.10f, -0.18f), P(0.30f, -0.18f), P(0.30f, -0.10f)
                }, true), c.shadow);
            Part(e, "Bright top glow ring",
                Ring("TopGlowBand", new[] {
                    P(0.285f, -0.095f), P(0.313f, -0.095f),
                    P(0.313f, -0.068f), P(0.285f, -0.068f)
                }), c.cyan);
            Part(e, "Blue power inside motor",
                Lathe("MotorGlow", new[] {
                    P(0.20f, -0.162f), P(0.268f, -0.148f),
                    P(0.268f, -0.122f)
                }, true), c.deepCyan);
            Part(e, "Lower engine shock housing",
                Lathe("EngineLower", new[] {
                    P(0.32f, -0.39f), P(0.45f, -0.33f),
                    P(0.47f, -0.22f), P(0.41f, -0.20f)
                }, true), c.gunmetal);
            Part(e, "Underbody cyan thrust corona",
                Ring("HoverCorona", new[] {
                    P(0.325f, -0.41f), P(0.40f, -0.41f),
                    P(0.415f, -0.365f), P(0.325f, -0.365f)
                }), c.cyan);
            Part(e, "Hover exhaust disc",
                Lathe("ExhaustDisk", new[] {
                    P(0.12f, -0.425f), P(0.28f, -0.415f),
                    P(0.31f, -0.398f)
                }, true), c.deepCyan);

            // Angular rear-and-front support forms an aggressive swept wing.
            Beam(t, sideName + " load-bearing spar",
                new Vector3(side * 0.75f, -0.03f, -0.23f),
                new Vector3(side * 1.55f, -0.05f, -0.23f),
                0.21f, 0.24f, c.armor);
            Beam(t, sideName + " forward sweep brace",
                new Vector3(side * 0.80f, 0.12f, 0.50f),
                new Vector3(side * 1.50f, 0.02f, -0.03f),
                0.15f, 0.18f, c.gunmetal);
            Box(t, sideName + " spar attachment armor",
                new Vector3(side * 0.98f, 0f, -0.25f),
                new Vector3(0.37f, 0.25f, 0.36f), c.paleArmor);

            // A protective, swept strike fin outside the duct: no rotor blades.
            Vector2[] wingOutline = {
                P(side * 1.77f, -0.54f), P(side * 2.12f, -0.65f),
                P(side * 2.25f, 0.25f), P(side * 2.04f, 0.61f),
                P(side * 1.89f, 0.23f)
            };
            Part(t, sideName + " forward-swept armor fin",
                Hull(side < 0 ? "WingLeft" : "WingRight", wingOutline,
                    new[] { S(-0.16f, 0.96f), S(-0.055f, 1.0f), S(0.085f, 0.88f) }),
                c.deepMetal);
        }
    }

    private static void BuildTailAndArmor(Transform t, Colors c)
    {
        // Two angular V-shaped tail stabilizers. Chunky silhouette at long range.
        for (int side = -1; side <= 1; side += 2)
        {
            string label = side < 0 ? "Left" : "Right";
            Vector2[] finOutline = {
                P(side * 0.42f, 0.25f), P(side * 0.62f, 0.34f),
                P(side * 0.82f, 0.91f), P(side * 0.50f, 0.80f)
            };
            GameObject fin = Part(t, label + " vertical tail spike",
                Plate(side < 0 ? "TailFinLeft" : "TailFinRight",
                    finOutline, -0.86f, -0.72f), c.armor);
            // Tail plates are solid fins, not separate hover motors.
            fin.transform.localRotation = Quaternion.Euler(0f, side * 13f, 0f);

            Box(t, label + " engine-side armor rivet",
                new Vector3(side * 0.75f, 0.27f, -0.38f),
                new Vector3(0.14f, 0.055f, 0.17f), c.gunmetal);
        }

        // Top warning-chevron paint is always visible, independent of windup.
        // The eye and attacking colors are red/amber; motors remain cyan.
        for (int side = -1; side <= 1; side += 2)
        {
            Beam(t, side < 0 ? "Left hazard slash" : "Right hazard slash",
                new Vector3(side * 0.26f, 0.445f, 0.03f),
                new Vector3(side * 0.51f, 0.358f, 0.33f),
                0.055f, 0.08f, c.caution);
        }
    }

    private static Transform BuildDiveWarning(Transform t, Colors c)
    {
        Transform warning = Child(t, "DIVE WARNING — toggled by HordeFlyingAttack").transform;
        Part(warning, "Orange underbody warning halo",
            Ring("ChargeHalo", new[] {
                P(0.77f, -0.58f), P(0.835f, -0.58f),
                P(0.835f, -0.535f), P(0.77f, -0.535f)
            }), c.caution);

        // Warning visible both to the person on foot and from a moving car.
        for (int side = -1; side <= 1; side += 2)
        {
            Beam(warning, side < 0 ? "Left flashing attack stripe" : "Right flashing attack stripe",
                new Vector3(side * 0.65f, 0.03f, 1.455f),
                new Vector3(side * 0.30f, -0.28f, 1.455f),
                0.095f, 0.035f, c.caution);
            Part(warning, side < 0 ? "Left engine caution crown" : "Right engine caution crown",
                Ring("EngineChargeBand", new[] {
                    P(0.486f, 0.25f), P(0.529f, 0.25f),
                    P(0.529f, 0.275f), P(0.486f, 0.275f)
                }), c.caution, new Vector3(side * 1.64f, -0.06f, -0.22f));
        }
        warning.gameObject.SetActive(false);
        return warning;
    }

    // ----------------------------------------------------------------------
    // Procedural flat-shaded meshes. Hull uses clockwise XY-in-XZ outlines,
    // and plate uses CCW XY outlines, with winding normalized automatically.
    // ----------------------------------------------------------------------
    private static Mesh Hull(string name, Vector2[] points, Slice[] slices)
    {
        if (slices == null || slices.Length < 2 || points == null || points.Length < 3)
            throw new ArgumentException("Hull needs a polygon and at least two profile slices.");
        Vector2[] poly = (Vector2[])points.Clone();
        if (SignedArea(poly) > 0f) Array.Reverse(poly); // CW in XZ.
        Vector2 pivot = Centroid(poly);
        List<Vector3> verts = new List<Vector3>();
        List<int> triangles = new List<int>();
        for (int layer = 0; layer < slices.Length - 1; layer++)
        {
            for (int i = 0; i < poly.Length; i++)
            {
                int j = (i + 1) % poly.Length;
                Vector3 a = HullPoint(poly[i], pivot, slices[layer]);
                Vector3 b = HullPoint(poly[j], pivot, slices[layer]);
                Vector3 u = HullPoint(poly[i], pivot, slices[layer + 1]);
                Vector3 v = HullPoint(poly[j], pivot, slices[layer + 1]);
                Quad(verts, triangles, a, b, u, v);
            }
        }
        Slice bottom = slices[0];
        Slice top = slices[slices.Length - 1];
        Vector3 bottomCenter = new Vector3(pivot.x, bottom.y, pivot.y);
        Vector3 topCenter = new Vector3(pivot.x, top.y, pivot.y);
        for (int i = 0; i < poly.Length; i++)
        {
            int j = (i + 1) % poly.Length;
            Triangle(verts, triangles, topCenter,
                HullPoint(poly[i], pivot, top), HullPoint(poly[j], pivot, top));
            Triangle(verts, triangles, bottomCenter,
                HullPoint(poly[j], pivot, bottom), HullPoint(poly[i], pivot, bottom));
        }
        return MeshAsset("Hull_" + name, verts, triangles);
    }

    private static Mesh Plate(string name, Vector2[] points, float rearZ, float frontZ)
    {
        Vector2[] poly = (Vector2[])points.Clone();
        if (SignedArea(poly) < 0f) Array.Reverse(poly); // CCW in XY.
        Vector2 center = Centroid(poly);
        List<Vector3> verts = new List<Vector3>();
        List<int> triangles = new List<int>();
        for (int i = 0; i < poly.Length; i++)
        {
            int j = (i + 1) % poly.Length;
            Vector3 a = new Vector3(poly[i].x, poly[i].y, rearZ);
            Vector3 b = new Vector3(poly[j].x, poly[j].y, rearZ);
            Vector3 c = new Vector3(poly[i].x, poly[i].y, frontZ);
            Vector3 d = new Vector3(poly[j].x, poly[j].y, frontZ);
            Quad(verts, triangles, a, b, c, d);
            Triangle(verts, triangles, new Vector3(center.x, center.y, frontZ), c, d);
            Triangle(verts, triangles, new Vector3(center.x, center.y, rearZ), b, a);
        }
        return MeshAsset("Plate_" + name, verts, triangles);
    }

    private static Mesh Lathe(string name, Vector2[] profile, bool cap)
    {
        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();
        for (int i = 0; i + 1 < profile.Length; i++)
            Revolve(verts, tris, profile[i], profile[i + 1]);
        if (cap)
        {
            for (int k = 0; k < RadialSides; k++)
            {
                float a = k * Mathf.PI * 2f / RadialSides;
                float b = (k + 1) * Mathf.PI * 2f / RadialSides;
                Vector2 low = profile[0], high = profile[profile.Length - 1];
                Triangle(verts, tris,
                    new Vector3(0f, low.y, 0f), Radial(low, a), Radial(low, b));
                Triangle(verts, tris,
                    new Vector3(0f, high.y, 0f), Radial(high, b), Radial(high, a));
            }
        }
        return MeshAsset("Lathe_" + name, verts, tris);
    }

    private static Mesh Ring(string name, Vector2[] profile)
    {
        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();
        for (int i = 0; i < profile.Length; i++)
            Revolve(verts, tris, profile[i], profile[(i + 1) % profile.Length]);
        return MeshAsset("Ring_" + name, verts, tris);
    }

    private static void Revolve(List<Vector3> verts, List<int> tris, Vector2 a, Vector2 b)
    {
        for (int i = 0; i < RadialSides; i++)
        {
            float angleA = i * 2f * Mathf.PI / RadialSides;
            float angleB = (i + 1) * 2f * Mathf.PI / RadialSides;
            Quad(verts, tris, Radial(a, angleA), Radial(b, angleA),
                Radial(a, angleB), Radial(b, angleB));
        }
    }

    private static Vector3 HullPoint(Vector2 p, Vector2 center, Slice slice)
    {
        return new Vector3(center.x + (p.x - center.x) * slice.scale,
            slice.y, center.y + (p.y - center.y) * slice.scale);
    }

    private static Vector3 Radial(Vector2 r, float angle)
    {
        return new Vector3(Mathf.Cos(angle) * r.x, r.y, Mathf.Sin(angle) * r.x);
    }

    private static Vector2 Centroid(Vector2[] points)
    {
        Vector2 sum = Vector2.zero;
        for (int i = 0; i < points.Length; i++) sum += points[i];
        return sum / points.Length;
    }

    private static float SignedArea(Vector2[] points)
    {
        float area = 0f;
        for (int i = 0; i < points.Length; i++)
        {
            int j = (i + 1) % points.Length;
            area += points[i].x * points[j].y - points[j].x * points[i].y;
        }
        return area * 0.5f;
    }

    private static void Quad(List<Vector3> verts, List<int> tris,
        Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int index = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
        tris.Add(index); tris.Add(index + 1); tris.Add(index + 2);
        tris.Add(index + 2); tris.Add(index + 1); tris.Add(index + 3);
    }

    private static void Triangle(List<Vector3> verts, List<int> tris,
        Vector3 a, Vector3 b, Vector3 c)
    {
        int index = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c);
        tris.Add(index); tris.Add(index + 1); tris.Add(index + 2);
    }

    private static Mesh MeshAsset(string name, List<Vector3> verts, List<int> tris)
    {
        Mesh mesh = new Mesh { name = "HarrierV3_" + name };
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return StoreMesh(V3MeshFolder + "/HarrierV3_" + name + ".asset", mesh);
    }

    private static Mesh StoreMesh(string path, Mesh mesh)
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        EditorUtility.CopySerialized(mesh, existing); // Repeated upgrades keep GUIDs.
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(mesh);
        return existing;
    }

    private static GameObject Part(Transform parent, string name, Mesh mesh,
        Material material, Vector3? position = null)
    {
        GameObject go = Child(parent, name);
        if (position.HasValue) go.transform.localPosition = position.Value;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    private static GameObject Box(Transform parent, string name,
        Vector3 position, Vector3 scale, Material material)
    {
        // Reuse Unity's shared cube mesh; never retain the primitive collider.
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.layer = EnemyLayer;
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = position;
        cube.transform.localScale = scale;
        Collider collider = cube.GetComponent<Collider>();
        if (collider != null) Object.DestroyImmediate(collider);
        cube.GetComponent<MeshRenderer>().sharedMaterial = material;
        return cube;
    }

    private static void Beam(Transform parent, string name, Vector3 a,
        Vector3 b, float width, float depth, Material material)
    {
        Vector3 direction = b - a;
        GameObject piece = Box(parent, name, (a + b) * 0.5f,
            new Vector3(direction.magnitude, width, depth), material);
        if (direction.sqrMagnitude > 0.00001f)
            piece.transform.localRotation = Quaternion.FromToRotation(Vector3.right, direction);
    }

    private static GameObject Child(Transform parent, string name)
    {
        GameObject obj = new GameObject(name);
        obj.layer = EnemyLayer;
        obj.transform.SetParent(parent, false);
        return obj;
    }

    private static void CombineStaticMeshes(Transform root, Transform warning)
    {
        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
        Dictionary<Material, List<CombineInstance>> batches =
            new Dictionary<Material, List<CombineInstance>>();
        List<GameObject> partsToDestroy = new List<GameObject>(filters.Length);
        Matrix4x4 toRoot = root.worldToLocalMatrix;
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];
            if (filter == null || filter.sharedMesh == null ||
                filter.transform.IsChildOf(warning)) continue;
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || renderer.sharedMaterial == null) continue;
            Material material = renderer.sharedMaterial;
            List<CombineInstance> group;
            if (!batches.TryGetValue(material, out group))
            {
                group = new List<CombineInstance>();
                batches.Add(material, group);
            }
            group.Add(new CombineInstance {
                mesh = filter.sharedMesh,
                transform = toRoot * filter.transform.localToWorldMatrix
            });
            partsToDestroy.Add(filter.gameObject);
        }

        foreach (KeyValuePair<Material, List<CombineInstance>> entry in batches)
        {
            Mesh merged = new Mesh { name = "HarrierV3_Combined_" + entry.Key.name };
            merged.indexFormat = IndexFormat.UInt32;
            merged.CombineMeshes(entry.Value.ToArray(), true, true, false);
            merged.RecalculateBounds();
            string path = V3MeshFolder + "/Combined_" + entry.Key.name + ".asset";
            Mesh asset = StoreMesh(path, merged);
            Part(root, "Combined " + entry.Key.name, asset, entry.Key);
        }
        for (int i = 0; i < partsToDestroy.Count; i++)
            Object.DestroyImmediate(partsToDestroy[i]);
        RemoveEmptyVisualGroups(root, warning);
    }

    // The combined mesh renderers are now direct children of Drone Visuals.
    // Remove unused construction scaffolding without touching the warning child.
    private static void RemoveEmptyVisualGroups(Transform parent, Transform warning)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            if (child == warning || child.IsChildOf(warning)) continue;
            RemoveEmptyVisualGroups(child, warning);
            if (child.childCount == 0 && child.GetComponents<Component>().Length == 1)
                Object.DestroyImmediate(child.gameObject);
        }
    }

    private static Colors CreateColors()
    {
        return new Colors {
            shadow     = MaterialAsset("V3_AlmostBlackMechanisms", new Color(.085f, .105f, .12f), false),
            deepMetal  = MaterialAsset("V3_DarkBody", new Color(.17f, .20f, .24f), false),
            armor      = MaterialAsset("V3_BattleshipSteel", new Color(.36f, .40f, .45f), false),
            paleArmor  = MaterialAsset("V3_LightArmorEdges", new Color(.54f, .58f, .61f), false),
            gunmetal   = MaterialAsset("V3_ArmoredTrim", new Color(.25f, .29f, .33f), false),
            visor      = MaterialAsset("V3_VisorBlack", new Color(.035f, .045f, .065f), false),
            redEye     = MaterialAsset("V3_HostileRedEyes", new Color(1f, .085f, .038f), true),
            cyan       = MaterialAsset("V3_CyanPropulsion", new Color(.055f, .83f, 1f), true),
            deepCyan   = MaterialAsset("V3_BlueCore", new Color(.035f, .38f, .92f), true),
            caution    = MaterialAsset("V3_DiveAmber", new Color(1f, .34f, .015f), true),
            flash      = MaterialAsset("V3_HitWhite", Color.white, true)
        };
    }

    private static Material MaterialAsset(string name, Color color, bool emissive)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) throw new InvalidOperationException("No supported URP/Built-in shader available.");
        Material material = new Material(shader) { name = name };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.18f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", emissive ? 0f : 0.24f);
        if (emissive && material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 3.3f);
        }
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void PrepareFolders()
    {
        EnsureFolder("Assets", "HarrierDrone");
        EnsureFolder(RootFolder, "Materials");
        EnsureFolder(RootFolder, "Meshes");
        EnsureFolder(MeshFolder, "V3");
    }

    private static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + child))
            AssetDatabase.CreateFolder(parent, child);
    }

    private static void SetFloat(SerializedObject s, string field, float value)
    {
        SerializedProperty p = s.FindProperty(field);
        if (p != null) p.floatValue = value;
    }
    private static void SetBool(SerializedObject s, string field, bool value)
    {
        SerializedProperty p = s.FindProperty(field);
        if (p != null) p.boolValue = value;
    }
    private static void SetInt(SerializedObject s, string field, int value)
    {
        SerializedProperty p = s.FindProperty(field);
        if (p != null) p.intValue = value;
    }
    private static Vector2 P(float a, float b) { return new Vector2(a, b); }
    private static Slice S(float y, float size) { return new Slice(y, size); }

    private static void SelectPrefab()
    {
        Object asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Selection.activeObject = asset;
        if (asset != null) EditorGUIUtility.PingObject(asset);
    }

    private static void RegisterWithManager(HordeEnemy enemyPrefab)
    {
        HordeManager manager = Object.FindObjectOfType<HordeManager>();
        if (manager == null)
        {
            Debug.Log("Harrier V3 built. No HordeManager in the open scene. " +
                      "Add the prefab to Additional Enemy Types manually.");
            return;
        }
        SerializedObject managerSO = new SerializedObject(manager);
        SerializedProperty types = managerSO.FindProperty("additionalEnemyTypes");
        if (types == null) return;
        for (int i = 0; i < types.arraySize; i++)
        {
            SerializedProperty item = types.GetArrayElementAtIndex(i);
            if (item.FindPropertyRelative("prefab").objectReferenceValue == enemyPrefab)
                return;
        }
        Undo.RecordObject(manager, "Register Harrier Drone V3");
        int index = types.arraySize;
        types.InsertArrayElementAtIndex(index);
        SerializedProperty entry = types.GetArrayElementAtIndex(index);
        entry.FindPropertyRelative("prefab").objectReferenceValue = enemyPrefab;
        entry.FindPropertyRelative("spawnWeight").floatValue = .07f;
        entry.FindPropertyRelative("unlockSeconds").floatValue = 75f;
        entry.FindPropertyRelative("maxActive").intValue = 10;
        entry.FindPropertyRelative("prewarm").intValue = 4;
        managerSO.ApplyModifiedProperties();
        EditorUtility.SetDirty(manager);
        if (manager.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
    }
}
