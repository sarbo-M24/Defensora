using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Defensora.FX.Editor
{
    /// <summary>
    /// Generates the ghost enemy variants: a glowing see-through sphere with wind-tunnel
    /// streamlines, a soft wake, a halo, embers and a light, plus a GhostMover tuned per variant.
    /// Re-running updates assets in place (GUIDs survive) but OVERWRITES the prefabs, so move
    /// tuning you want to keep into the Variants table below.
    /// </summary>
    public static class GhostBuilder
    {
        private const string MaterialsDir = "Assets/HalloweenFX/Materials";
        private const string PrefabsDir = "Assets/Prefabs/Ghosts";
        private const int EnemyLayer = 8;

        private class Variant
        {
            public string Name;
            public Color Core, Rim;
            public float Radius = 0.5f;
            public float Health = 100f;

            // Body shader.
            public float CoreIntensity = 2f, RimIntensity = 4f, Opacity = 0.35f;
            public float TailStretch = 0.25f, Wobble = 0.04f, FlowSpeed = 1.5f;

            // Trails.
            public float StreamRate = 40f;      // streamlines per second
            public float StreamDrift = 0.6f;    // backwards drift, m/s
            public float StreamSwirl = 0f;      // vortex spin around the wake
            public float StreamConverge = 0.35f;
            public float StreamWidth = 0.025f;
            public float WakeTime = 0.5f;
            public float EmberRate = 6f;
            public float LightIntensity = 1.5f;

            // Flight.
            public float RiseSpeed = 4f, CruiseSpeed = 3.5f, Altitude = 6f, TurnRate = 140f;
            public float SpiralStart = 8f, SpiralAngular = 90f, SpiralTightening = 0.6f, SpiralInward = 1.2f;
            public float BobHeight = 0.15f;
        }

        private static readonly Variant[] Variants =
        {
            // Small, quick, icy. Lots of fine streamlines.
            new Variant
            {
                Name = "Wisp", Core = Hex(0x9FF4FF), Rim = Hex(0x2E8BFF), Radius = 0.35f, Health = 60f,
                CoreIntensity = 2.4f, RimIntensity = 5f, Opacity = 0.3f, TailStretch = 0.45f, FlowSpeed = 2.5f,
                StreamRate = 55f, StreamDrift = 0.4f, StreamConverge = 0.3f, StreamWidth = 0.018f, WakeTime = 0.45f,
                EmberRate = 4f, LightIntensity = 1.2f,
                RiseSpeed = 5.5f, CruiseSpeed = 5f, Altitude = 6f, TurnRate = 180f,
                SpiralStart = 7f, SpiralAngular = 120f, SpiralTightening = 0.7f, SpiralInward = 1.6f, BobHeight = 0.1f,
            },
            // The baseline. Marigold gold, embers trailing like a candle.
            new Variant
            {
                Name = "Marigold", Core = Hex(0xFFD27A), Rim = Hex(0xFF6A00), Radius = 0.5f, Health = 100f,
                CoreIntensity = 2.2f, RimIntensity = 4.5f, Opacity = 0.35f, TailStretch = 0.3f,
                StreamRate = 40f, StreamDrift = 0.6f, StreamConverge = 0.35f, StreamWidth = 0.025f, WakeTime = 0.55f,
                EmberRate = 14f, LightIntensity = 1.8f,
                RiseSpeed = 4f, CruiseSpeed = 3.5f, Altitude = 7f,
                SpiralStart = 8f, SpiralAngular = 90f, SpiralTightening = 0.6f, SpiralInward = 1.2f,
            },
            // Big slow tank. Wide wake, heavy wobble, lazy wide spiral.
            new Variant
            {
                Name = "Rosa", Core = Hex(0xFF8FD8), Rim = Hex(0xD41BFF), Radius = 0.8f, Health = 260f,
                CoreIntensity = 1.8f, RimIntensity = 3.5f, Opacity = 0.42f, TailStretch = 0.2f, Wobble = 0.07f, FlowSpeed = 1f,
                StreamRate = 50f, StreamDrift = 0.8f, StreamConverge = 0.6f, StreamWidth = 0.035f, WakeTime = 0.9f,
                EmberRate = 5f, LightIntensity = 2.4f,
                RiseSpeed = 2.5f, CruiseSpeed = 2.2f, Altitude = 5f, TurnRate = 70f,
                SpiralStart = 10f, SpiralAngular = 45f, SpiralTightening = 0.4f, SpiralInward = 0.7f, BobHeight = 0.25f,
            },
            // Ectoplasm wraith. Flies high, vortex-twisted trail, spirals tight and fast.
            new Variant
            {
                Name = "Ecto", Core = Hex(0xD8FF7A), Rim = Hex(0x1BFF8A), Radius = 0.45f, Health = 80f,
                CoreIntensity = 2.6f, RimIntensity = 5.5f, Opacity = 0.3f, TailStretch = 0.6f, FlowSpeed = 3f,
                StreamRate = 45f, StreamDrift = 0.5f, StreamSwirl = 4f, StreamConverge = 0.2f, StreamWidth = 0.022f,
                WakeTime = 0.6f, EmberRate = 8f, LightIntensity = 1.5f,
                RiseSpeed = 6f, CruiseSpeed = 4f, Altitude = 9f, TurnRate = 160f,
                SpiralStart = 12f, SpiralAngular = 160f, SpiralTightening = 1f, SpiralInward = 1f, BobHeight = 0.12f,
            },
        };

        private struct SharedMaterials
        {
            public Material Ribbon, Wake, Dot, Halo;
        }

        [MenuItem("Tools/Defensora FX/3. Build Ghost Enemies", priority = 3)]
        public static void BuildAll()
        {
            EnsureFolder(MaterialsDir);
            EnsureFolder(PrefabsDir);

            var shared = BuildSharedMaterials();
            var hitEffect = AssetDatabase.LoadAssetAtPath<HitEffect>(HalloweenFXBuilder.HitPrefabPath);
            if (hitEffect == null)
                Debug.LogWarning("[Defensora FX] Hit VFX prefab missing - run 'Tools/Defensora FX/1. Build...' first. Ghosts will have no hit effect.");

            foreach (var variant in Variants)
                BuildGhost(variant, shared, hitEffect);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Defensora FX] Built {Variants.Length} ghost variants under {PrefabsDir}.");
        }

        [MenuItem("Tools/Defensora FX/4. Add Ghost Test Rig To Open Scene", priority = 4)]
        public static void AddTestRig()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(Variants[0])) == null)
                BuildAll();

            var rig = new GameObject("Ghost Test Rig");
            Undo.RegisterCreatedObjectUndo(rig, "Add Ghost Test Rig");

            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null)
            {
                Vector3 pivot = sceneView.pivot;
                rig.transform.position = new Vector3(pivot.x, 0f, pivot.z);
            }

            var goal = new GameObject("Test Goal");
            goal.transform.SetParent(rig.transform, false);
            goal.transform.localPosition = new Vector3(0f, 0f, 18f);

            float spacing = 3f;
            float start = -spacing * (Variants.Length - 1) * 0.5f;

            for (int i = 0; i < Variants.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(Variants[i]));
                var ghost = (GameObject)PrefabUtility.InstantiatePrefab(prefab, rig.transform);
                ghost.transform.localPosition = new Vector3(start + i * spacing, 0.5f, 0f);

                var so = new SerializedObject(ghost.GetComponent<GhostMover>());
                so.FindProperty("testGoal").objectReferenceValue = goal.transform;
                so.FindProperty("altitudeRelativeToSpawn").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            Selection.activeGameObject = rig;
            EditorSceneManager.MarkSceneDirty(rig.scene);
            Debug.Log("[Defensora FX] Ghost test rig added. Press Play: each ghost loops rise -> cruise -> spiral. Tune GhostMover live.");
        }

        private static string PrefabPath(Variant v) => $"{PrefabsDir}/Ghost_{v.Name}.prefab";

        // ---------------------------------------------------------------- materials

        private static SharedMaterials BuildSharedMaterials()
        {
            var m = new SharedMaterials
            {
                Ribbon = GetOrCreateMaterial("M_GhostStreamline", "Defensora/Particles/Ghost Wisp"),
                Wake = GetOrCreateMaterial("M_GhostWake", "Defensora/Particles/Ghost Wisp"),
                Dot = GetOrCreateMaterial("M_GhostEmber", "Defensora/Particles/Ghost Wisp"),
                Halo = GetOrCreateMaterial("M_GhostHalo", "Defensora/Particles/Ghost Wisp"),
            };

            m.Ribbon.SetFloat("_Mode", 0f);
            m.Ribbon.SetFloat("_Intensity", 4f);
            m.Ribbon.SetFloat("_Softness", 1.5f);
            m.Ribbon.SetFloat("_NoiseScale", 8f);
            m.Ribbon.SetFloat("_NoiseScroll", 3f);
            m.Ribbon.SetFloat("_NoiseAmount", 0.55f);

            m.Wake.SetFloat("_Mode", 0f);
            m.Wake.SetFloat("_Intensity", 1.6f);
            m.Wake.SetFloat("_Softness", 2.5f);
            m.Wake.SetFloat("_NoiseScale", 4f);
            m.Wake.SetFloat("_NoiseScroll", 2f);
            m.Wake.SetFloat("_NoiseAmount", 0.7f);

            m.Dot.SetFloat("_Mode", 1f);
            m.Dot.SetFloat("_Intensity", 6f);
            m.Dot.SetFloat("_Softness", 2f);

            m.Halo.SetFloat("_Mode", 1f);
            m.Halo.SetFloat("_Intensity", 1.2f);
            m.Halo.SetFloat("_Softness", 3f);

            foreach (var material in new[] { m.Ribbon, m.Wake, m.Dot, m.Halo })
                EditorUtility.SetDirty(material);
            return m;
        }

        private static Material BuildBodyMaterial(Variant v)
        {
            var mat = GetOrCreateMaterial($"M_Ghost_{v.Name}", "Defensora/Ghost");
            mat.SetColor("_BaseColor", v.Core);
            mat.SetColor("_RimColor", v.Rim);
            mat.SetFloat("_CoreIntensity", v.CoreIntensity);
            mat.SetFloat("_RimIntensity", v.RimIntensity);
            mat.SetFloat("_Opacity", v.Opacity);
            mat.SetFloat("_TailStretch", v.TailStretch);
            mat.SetFloat("_WobbleAmount", v.Wobble);
            mat.SetFloat("_FlowSpeed", v.FlowSpeed);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material GetOrCreateMaterial(string name, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
                throw new System.InvalidOperationException($"Shader '{shaderName}' not found - check the console for shader compile errors.");

            string path = $"{MaterialsDir}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }
            material.enableInstancing = true;
            return material;
        }

        // ---------------------------------------------------------------- prefab

        private static void BuildGhost(Variant v, SharedMaterials shared, HitEffect hitEffect)
        {
            var root = new GameObject($"Ghost_{v.Name}");

            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var collider = root.AddComponent<SphereCollider>();
            collider.radius = v.Radius;

            var health = root.AddComponent<TargetHealth>();
            var healthSo = new SerializedObject(health);
            Set(healthSo, "maxHealth", v.Health);
            healthSo.ApplyModifiedPropertiesWithoutUndo();

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            BuildBody(visual, v);
            BuildHalo(visual, v, shared.Halo);
            BuildStreamlines(visual, v, shared);
            BuildWake(visual, v, shared.Wake);
            BuildEmbers(visual, v, shared.Dot);
            BuildLight(visual, v);

            var reaction = root.AddComponent<EnemyHitReaction>();
            var reactionSo = new SerializedObject(reaction);
            reactionSo.FindProperty("hitEffectPrefab").objectReferenceValue = hitEffect;
            reactionSo.FindProperty("deathEffectPrefab").objectReferenceValue = hitEffect;
            reactionSo.FindProperty("body").objectReferenceValue = visual;
            Set(reactionSo, "punchAmount", 0.18f);
            reactionSo.ApplyModifiedPropertiesWithoutUndo();

            var mover = root.AddComponent<GhostMover>();
            var so = new SerializedObject(mover);
            so.FindProperty("visual").objectReferenceValue = visual;
            Set(so, "riseSpeed", v.RiseSpeed);
            Set(so, "cruiseAltitude", v.Altitude);
            Set(so, "cruiseSpeed", v.CruiseSpeed);
            Set(so, "turnRate", v.TurnRate);
            Set(so, "spiralStartDistance", v.SpiralStart);
            Set(so, "spiralAngularSpeed", v.SpiralAngular);
            Set(so, "spiralTightening", v.SpiralTightening);
            Set(so, "spiralInwardSpeed", v.SpiralInward);
            Set(so, "bobHeight", v.BobHeight);
            so.ApplyModifiedPropertiesWithoutUndo();

            SetLayerRecursively(root, EnemyLayer);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(v));
            Object.DestroyImmediate(root);
        }

        private static void BuildBody(Transform parent, Variant v)
        {
            var go = new GameObject("Body");
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * (v.Radius * 2f);

            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = BuildBodyMaterial(v);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static void BuildHalo(Transform parent, Variant v, Material material)
        {
            // A few big, overlapping soft dots fading in and out: a shimmering glow around the body.
            var ps = NewSystem(parent, "Halo", material, 6);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = 1.4f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(v.Radius * 3.6f, v.Radius * 4.4f);
            main.startColor = WithAlpha(v.Rim, 0.45f);

            var emission = ps.emission;
            emission.rateOverTime = 3f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Fade(Color.white, 0.4f, 0.6f);
        }

        private static void BuildStreamlines(Transform parent, Variant v, SharedMaterials m)
        {
            // Wind tunnel smoke lines: emitted on a ring around the body (perpendicular to travel)
            // and left behind in world space, so they stream past the ghost and pinch in behind it.
            var ps = NewSystem(parent, "Wind_Streamlines", m.Dot, 300);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
            main.startSpeed = -v.StreamConverge; // negative = inward from the ring
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.05f);
            main.startColor = new ParticleSystem.MinMaxGradient(v.Core, v.Rim);

            var emission = ps.emission;
            emission.rateOverTime = v.StreamRate;
            emission.rateOverDistance = v.StreamRate * 0.25f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = v.Radius * 1.05f;
            shape.radiusThickness = 0f;
            shape.arcMode = ParticleSystemShapeMultiModeValue.Random;

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = 0f;
            velocity.y = 0f;
            velocity.z = -v.StreamDrift;
            velocity.orbitalX = 0f;
            velocity.orbitalY = 0f;
            velocity.orbitalZ = v.StreamSwirl;

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 1.4f;
            noise.scrollSpeed = 0.8f;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Fade(Color.white, 0.1f, 0.5f);

            var trails = ps.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.PerParticle;
            trails.ratio = 1f;
            trails.lifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            trails.minVertexDistance = 0.06f;
            trails.worldSpace = true;
            trails.dieWithParticles = true;
            trails.textureMode = ParticleSystemTrailTextureMode.Stretch;
            trails.sizeAffectsWidth = false;
            trails.sizeAffectsLifetime = false;
            trails.inheritParticleColor = true;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(v.StreamWidth,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));
            trails.colorOverTrail = Fade(Color.white, 0f, 0f);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.trailMaterial = m.Ribbon;
        }

        private static void BuildWake(Transform parent, Variant v, Material material)
        {
            var go = new GameObject("Wake");
            go.transform.SetParent(parent, false);

            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = material;
            trail.time = v.WakeTime;
            trail.minVertexDistance = 0.05f;
            trail.numCapVertices = 4;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.widthMultiplier = v.Radius * 1.7f;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(v.Core, 0f), new GradientColorKey(v.Rim, 1f) },
                new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;

            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.lightProbeUsage = LightProbeUsage.Off;
            trail.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static void BuildEmbers(Transform parent, Variant v, Material material)
        {
            var ps = NewSystem(parent, "Embers", material, 80);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            main.startColor = new ParticleSystem.MinMaxGradient(v.Core, v.Rim);
            main.gravityModifier = -0.04f;

            var emission = ps.emission;
            emission.rateOverTime = v.EmberRate * 0.5f;
            emission.rateOverDistance = v.EmberRate;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = v.Radius * 0.9f;

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.5f;
            noise.frequency = 1f;
            noise.scrollSpeed = 0.5f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Fade(Color.white, 0.1f, 0.4f);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));
        }

        private static void BuildLight(Transform parent, Variant v)
        {
            if (v.LightIntensity <= 0f)
                return;

            var go = new GameObject("Glow_Light");
            go.transform.SetParent(parent, false);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Color.Lerp(v.Core, v.Rim, 0.5f);
            light.range = v.Radius * 9f;
            light.intensity = v.LightIntensity;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.Auto;
            go.AddComponent<UniversalAdditionalLightData>();
        }

        // ---------------------------------------------------------------- helpers

        private static ParticleSystem NewSystem(Transform parent, string name, Material material, int maxParticles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = maxParticles;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.enabled = false;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return ps;
        }

        /// <summary>Alpha fades in over [0, fadeIn] and out over [fadeOutStart, 1].</summary>
        private static ParticleSystem.MinMaxGradient Fade(Color color, float fadeIn, float fadeOutStart)
        {
            var alphas = fadeIn > 0f
                ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(1f, fadeOutStart), new GradientAlphaKey(0f, 1f) }
                : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, fadeOutStart), new GradientAlphaKey(0f, 1f) };

            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) }, alphas);
            return new ParticleSystem.MinMaxGradient(gradient);
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        private static void Set(SerializedObject so, string property, float value) => so.FindProperty(property).floatValue = value;

        private static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
