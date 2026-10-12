using Defensora.FX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Defensora.FX.Editor
{
    /// <summary>
    /// Generates the Dia de los Muertos look: materials, light prefabs, hit VFX prefab,
    /// the night post-processing profile, and applies the dark atmosphere to the open scene.
    /// Re-running is safe: existing assets are updated in place (GUIDs and references survive).
    /// </summary>
    public static class HalloweenFXBuilder
    {
        private const string Root = "Assets/HalloweenFX";
        private const string MaterialsDir = Root + "/Materials";
        private const string PrefabsDir = Root + "/Prefabs";
        private const string SettingsDir = Root + "/Settings";

        public const string ProfilePath = SettingsDir + "/VP_DiaDeMuertosNight.asset";
        public const string HitPrefabPath = PrefabsDir + "/VFX_Hit_Confetti.prefab";
        public const string LampPrefabPath = PrefabsDir + "/Light_Lamp.prefab";
        public const string CandlePrefabPath = PrefabsDir + "/Light_Candle.prefab";

        // Night palette.
        private static readonly Color AmbientColor = new Color(0.035f, 0.025f, 0.06f);
        private static readonly Color FogColor = new Color(0.03f, 0.018f, 0.045f);
        private static readonly Color MoonColor = new Color(0.55f, 0.62f, 1f);

        // Papel picado palette for confetti.
        private static readonly Color[] ConfettiColors =
        {
            Hex(0xFF1F8E), // rosa mexicano
            Hex(0xFF9F1C), // marigold
            Hex(0xFFE11A), // sun yellow
            Hex(0x1BE7D6), // turquoise
            Hex(0x8F3BFF), // violet
            Hex(0x9BFF2E), // lime
            Hex(0xFF2E3B), // chile red
            Hex(0x2E7BFF), // cobalt
        };

        private static readonly Color[] PetalColors =
        {
            Hex(0xFF8C00), Hex(0xFFA500), Hex(0xFFB300), Hex(0xE8590C),
        };

        [MenuItem("Tools/Defensora FX/1. Build Materials, Lights and VFX Prefabs", priority = 1)]
        public static void BuildAll()
        {
            EnsureFolder(MaterialsDir);
            EnsureFolder(PrefabsDir);
            EnsureFolder(SettingsDir);

            var materials = BuildMaterials();
            BuildVolumeProfile();
            BuildLightPrefab(LampPrefabPath, "Light_Lamp", isCandle: false);
            BuildLightPrefab(CandlePrefabPath, "Light_Candle", isCandle: true);
            BuildHitPrefab(materials);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Defensora FX] Built materials, light prefabs, hit VFX and post profile under {Root}.");
        }

        [MenuItem("Tools/Defensora FX/2. Apply Dark Atmosphere To Open Scene", priority = 2)]
        public static void ApplyAtmosphere()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                BuildAll();
                profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            }

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = AmbientColor;
            RenderSettings.reflectionIntensity = 0.15f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogDensity = 0.045f;
            RenderSettings.skybox = null;
            RenderSettings.sun = null;
            DynamicGI.UpdateEnvironment();

            // Moonlight: the directional light becomes a faint cold fill.
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (light.type != LightType.Directional)
                    continue;
                Undo.RecordObject(light, "Apply Dark Atmosphere");
                light.color = MoonColor;
                light.intensity = 0.08f;
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 0.6f;
            }

            foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Undo.RecordObject(camera, "Apply Dark Atmosphere");
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = FogColor;
                var data = camera.GetUniversalAdditionalCameraData();
                Undo.RecordObject(data, "Apply Dark Atmosphere");
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            }

            const string volumeName = "Dia de Muertos Night Volume";
            var volumeGo = GameObject.Find(volumeName);
            if (volumeGo == null)
            {
                volumeGo = new GameObject(volumeName);
                Undo.RegisterCreatedObjectUndo(volumeGo, "Apply Dark Atmosphere");
            }
            var volume = volumeGo.GetComponent<Volume>();
            if (volume == null)
                volume = Undo.AddComponent<Volume>(volumeGo);
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;

            EditorSceneManager.MarkSceneDirty(volumeGo.scene);
            Debug.Log("[Defensora FX] Dark atmosphere applied to the open scene (not saved - save the scene to keep it).");
        }

        [MenuItem("Assets/Defensora FX/Convert Material(s) To Dark Surface")]
        private static void ConvertSelectedMaterials()
        {
            var shader = Shader.Find("Defensora/Dark Surface");
            int count = 0;
            foreach (var material in Selection.GetFiltered<Material>(SelectionMode.Assets))
            {
                Undo.RecordObject(material, "Convert To Dark Surface");
                // URP/Lit shares property names (_BaseMap, _BaseColor, _BumpMap, _Metallic,
                // _Smoothness, _EmissionMap, _EmissionColor), so values carry over.
                material.shader = shader;
                EditorUtility.SetDirty(material);
                count++;
            }
            Debug.Log($"[Defensora FX] Converted {count} material(s) to Defensora/Dark Surface.");
        }

        [MenuItem("Assets/Defensora FX/Convert Material(s) To Dark Surface", true)]
        private static bool ConvertSelectedMaterialsValidate() => Selection.GetFiltered<Material>(SelectionMode.Assets).Length > 0;

        // ---------------------------------------------------------------- materials

        private struct HitMaterials
        {
            public Material Confetti, Petals, Glow, Ring, Starburst, Spark;
        }

        private static HitMaterials BuildMaterials()
        {
            var dark = GetOrCreateMaterial("M_DarkSurface", "Defensora/Dark Surface");
            dark.SetColor("_BaseColor", new Color(0.55f, 0.5f, 0.48f));
            EditorUtility.SetDirty(dark);

            var m = new HitMaterials
            {
                Confetti = GetOrCreateMaterial("M_Confetti", "Defensora/Particles/Confetti"),
                Petals = GetOrCreateMaterial("M_MarigoldPetals", "Defensora/Particles/Confetti"),
                Glow = GetOrCreateMaterial("M_HitGlow", "Defensora/Particles/Hit Flash"),
                Ring = GetOrCreateMaterial("M_HitRing", "Defensora/Particles/Hit Flash"),
                Starburst = GetOrCreateMaterial("M_HitStarburst", "Defensora/Particles/Hit Flash"),
                Spark = GetOrCreateMaterial("M_HitSpark", "Defensora/Particles/Hit Flash"),
            };

            m.Confetti.SetFloat("_ForceShape", -1f);
            m.Confetti.SetFloat("_Glow", 2.4f);
            m.Petals.SetFloat("_ForceShape", 3f);
            m.Petals.SetFloat("_Glow", 1.8f);
            m.Petals.SetFloat("_GlintStrength", 0.8f);

            m.Glow.SetFloat("_Mode", 0f);
            m.Glow.SetFloat("_Intensity", 3.5f);
            m.Glow.SetFloat("_Falloff", 2.2f);

            m.Ring.SetFloat("_Mode", 1f);
            m.Ring.SetFloat("_Intensity", 6f);
            m.Ring.SetFloat("_RingWidth", 0.14f);

            m.Starburst.SetFloat("_Mode", 2f);
            m.Starburst.SetFloat("_Intensity", 6f);
            m.Starburst.SetFloat("_Rays", 8f);
            m.Starburst.SetFloat("_CoreWhite", 0.8f);

            m.Spark.SetFloat("_Mode", 0f);
            m.Spark.SetFloat("_Intensity", 12f);
            m.Spark.SetFloat("_Falloff", 1.5f);
            m.Spark.SetFloat("_CoreWhite", 0.3f);

            foreach (var material in new[] { m.Confetti, m.Petals, m.Glow, m.Ring, m.Starburst, m.Spark })
                EditorUtility.SetDirty(material);
            return m;
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

        // ---------------------------------------------------------------- post-processing

        private static void BuildVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            else
            {
                foreach (var component in profile.components.ToArray())
                {
                    profile.Remove(component.GetType());
                    Object.DestroyImmediate(component, true);
                }
            }

            var tonemapping = Add<Tonemapping>(profile);
            tonemapping.mode.value = TonemappingMode.ACES;

            var colorAdjustments = Add<ColorAdjustments>(profile);
            colorAdjustments.postExposure.value = -0.35f;
            colorAdjustments.contrast.value = 18f;
            colorAdjustments.saturation.value = 12f;
            colorAdjustments.colorFilter.value = new Color(0.92f, 0.9f, 1f);

            // Purple-blue shadows, warm candle-gold highlights.
            var smh = Add<ShadowsMidtonesHighlights>(profile);
            smh.shadows.value = new Vector4(0.9f, 0.82f, 1.18f, -0.04f);
            smh.midtones.value = new Vector4(1f, 0.97f, 1.03f, 0f);
            smh.highlights.value = new Vector4(1.12f, 1f, 0.86f, 0f);

            var bloom = Add<Bloom>(profile);
            bloom.threshold.value = 1f;
            bloom.intensity.value = 1.3f;
            bloom.scatter.value = 0.68f;
            bloom.tint.value = new Color(1f, 0.88f, 0.8f);
            bloom.highQualityFiltering.value = true;

            var vignette = Add<Vignette>(profile);
            vignette.color.value = new Color(0.06f, 0f, 0.09f);
            vignette.intensity.value = 0.42f;
            vignette.smoothness.value = 0.45f;

            var grain = Add<FilmGrain>(profile);
            grain.type.value = FilmGrainLookup.Medium1;
            grain.intensity.value = 0.18f;
            grain.response.value = 0.85f;

            EditorUtility.SetDirty(profile);
        }

        private static T Add<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var component = profile.Add<T>(true);
            component.name = typeof(T).Name;
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        // ---------------------------------------------------------------- lights

        private static void BuildLightPrefab(string path, string name, bool isCandle)
        {
            var go = new GameObject(name);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.renderMode = LightRenderMode.ForcePixel;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.92f;
            light.shadowNearPlane = 0.1f;
            light.shadowBias = 0.05f;
            light.shadowNormalBias = 0.4f;
            go.AddComponent<UniversalAdditionalLightData>();

            var flicker = go.AddComponent<FlickerLight>();
            var so = new SerializedObject(flicker);
            if (isCandle)
            {
                light.color = new Color(1f, 0.55f, 0.2f);
                light.range = 4f;
                light.intensity = 2.6f;
                Set(so, "baseIntensity", 2.6f);
                Set(so, "flickerAmount", 0.38f);
                Set(so, "flickerSpeed", 9f);
                Set(so, "gutterRate", 0.12f);
                Set(so, "gutterDepth", 0.6f);
                Set(so, "gutterDuration", 0.3f);
                Set(so, "dimColor", new Color(1f, 0.34f, 0.08f));
                Set(so, "brightColor", new Color(1f, 0.64f, 0.28f));
                Set(so, "rangeVariation", 0.08f);
                Set(so, "positionJitter", 0.015f);
                Set(so, "swayAmount", 0f);
            }
            else
            {
                // Oil lamp / lantern: brighter, wider, calmer flame, gentle sway.
                light.color = new Color(1f, 0.7f, 0.38f);
                light.range = 10f;
                light.intensity = 5f;
                Set(so, "baseIntensity", 5f);
                Set(so, "flickerAmount", 0.14f);
                Set(so, "flickerSpeed", 4f);
                Set(so, "gutterRate", 0.02f);
                Set(so, "gutterDepth", 0.3f);
                Set(so, "gutterDuration", 0.45f);
                Set(so, "dimColor", new Color(1f, 0.5f, 0.2f));
                Set(so, "brightColor", new Color(1f, 0.76f, 0.46f));
                Set(so, "rangeVariation", 0.04f);
                Set(so, "positionJitter", 0.006f);
                Set(so, "swayAmount", 0.015f);
                Set(so, "swaySpeed", 0.45f);
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
        }

        // ---------------------------------------------------------------- hit VFX

        private static void BuildHitPrefab(HitMaterials m)
        {
            var root = new GameObject("VFX_Hit_Confetti");

            var flashGo = new GameObject("Flash_Light");
            flashGo.transform.SetParent(root.transform, false);
            flashGo.transform.localPosition = new Vector3(0f, 0f, 0.3f);
            var flash = flashGo.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.range = 4.5f;
            flash.intensity = 0f;
            flash.shadows = LightShadows.None;
            flash.renderMode = LightRenderMode.ForcePixel;
            flash.enabled = false;
            flashGo.AddComponent<UniversalAdditionalLightData>();

            var hit = root.AddComponent<HitEffect>();
            var so = new SerializedObject(hit);
            so.FindProperty("flashLight").objectReferenceValue = flash;
            so.ApplyModifiedPropertiesWithoutUndo();

            // The root sits on the hit surface facing along its normal. Lift the flash
            // sprites and light off the surface so they aren't half-buried in the enemy.
            var flashOffset = new GameObject("Flash");
            flashOffset.transform.SetParent(root.transform, false);
            flashOffset.transform.localPosition = new Vector3(0f, 0f, 0.15f);
            BuildStarburst(flashOffset.transform, m.Starburst);
            BuildRing(flashOffset.transform, m.Ring);
            BuildGlow(flashOffset.transform, m.Glow);
            BuildSparks(root.transform, m.Spark);
            BuildConfetti(root.transform, m.Confetti);
            BuildPetals(root.transform, m.Petals);

            PrefabUtility.SaveAsPrefabAsset(root, HitPrefabPath);
            Object.DestroyImmediate(root);
        }

        private static ParticleSystem NewSystem(Transform parent, string name, Material material, int maxParticles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
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

        private static void Burst(ParticleSystem ps, short min, short max, float time = 0f)
        {
            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(time, min, max) });
        }

        private static void Cone(ParticleSystem ps, float angle, float radius)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = radius;
        }

        private static void FadeOut(ParticleSystem ps, float fadeStart)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, fadeStart), new GradientAlphaKey(0f, 1f) });
            col.color = gradient;
        }

        private static void GrowCurve(ParticleSystem ps, float from, float to)
        {
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            var curve = new AnimationCurve(new Keyframe(0f, from, 0f, 3f), new Keyframe(1f, to, 0f, 0f));
            sol.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        private static ParticleSystem.MinMaxGradient RandomFrom(Color[] colors)
        {
            int n = Mathf.Min(colors.Length, 8);
            var keys = new GradientColorKey[n];
            for (int i = 0; i < n; i++)
                keys[i] = new GradientColorKey(colors[i], (i + 1f) / n);
            var gradient = new Gradient { mode = GradientMode.Fixed };
            gradient.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return new ParticleSystem.MinMaxGradient(gradient) { mode = ParticleSystemGradientMode.RandomColor };
        }

        private static void BuildStarburst(Transform parent, Material material)
        {
            var ps = NewSystem(parent, "Flash_Starburst", material, 2);
            var main = ps.main;
            main.startLifetime = 0.08f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1.0f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(1f, 0.92f, 0.7f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            Burst(ps, 1, 1);
            GrowCurve(ps, 0.6f, 1.3f);
            FadeOut(ps, 0.3f);
        }

        private static void BuildRing(Transform parent, Material material)
        {
            var ps = NewSystem(parent, "Flash_Ring", material, 2);
            var main = ps.main;
            main.startLifetime = 0.22f;
            main.startSpeed = 0f;
            main.startSize = 1.2f;
            main.startColor = RandomFrom(ConfettiColors);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            Burst(ps, 1, 1);
            GrowCurve(ps, 0.12f, 1f);
            FadeOut(ps, 0.2f);
        }

        private static void BuildGlow(Transform parent, Material material)
        {
            var ps = NewSystem(parent, "Flash_Glow", material, 4);
            var main = ps.main;
            main.startLifetime = 0.12f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 1.6f);
            main.startColor = RandomFrom(ConfettiColors);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            Burst(ps, 2, 2);
            GrowCurve(ps, 0.8f, 1.1f);
            FadeOut(ps, 0f);
        }

        private static void BuildSparks(Transform parent, Material material)
        {
            var ps = NewSystem(parent, "Sparks", material, 40);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.6f), new Color(1f, 0.45f, 0.1f));
            main.gravityModifier = 0.4f;
            Burst(ps, 14, 22);
            Cone(ps, 50f, 0.02f);
            FadeOut(ps, 0.5f);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.025f;
            renderer.lengthScale = 1f;
        }

        private static void BuildConfetti(Transform parent, Material material)
        {
            var ps = NewSystem(parent, "Confetti", material, 200);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.11f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = RandomFrom(ConfettiColors);
            main.gravityModifier = 0.55f;
            Burst(ps, 45, 65);
            Cone(ps, 38f, 0.03f);
            FadeOut(ps, 0.7f);
            AddPaperPhysics(ps, drag: 2.4f, spin: 12f, noise: 0.6f);

            ConfigureCardRenderer(ps);
        }

        private static void BuildPetals(Transform parent, Material material)
        {
            var ps = NewSystem(parent, "Marigold_Petals", material, 40);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.12f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = RandomFrom(PetalColors);
            main.gravityModifier = 0.25f;
            Burst(ps, 8, 14);
            Cone(ps, 45f, 0.03f);
            FadeOut(ps, 0.75f);
            AddPaperPhysics(ps, drag: 3f, spin: 6f, noise: 0.9f);

            ConfigureCardRenderer(ps);
        }

        private static void AddPaperPhysics(ParticleSystem ps, float drag, float spin, float noise)
        {
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 100f;
            limit.dampen = 0f;
            limit.drag = drag;
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = true;

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            rotation.x = new ParticleSystem.MinMaxCurve(-spin, spin);
            rotation.y = new ParticleSystem.MinMaxCurve(-spin, spin);
            rotation.z = new ParticleSystem.MinMaxCurve(-spin, spin);

            var noiseModule = ps.noise;
            noiseModule.enabled = true;
            noiseModule.strength = noise;
            noiseModule.frequency = 1.2f;
            noiseModule.scrollSpeed = 0.5f;
            noiseModule.quality = ParticleSystemNoiseQuality.Medium;

            var collision = ps.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.dampen = 0.6f;
            collision.bounce = 0.15f;
            collision.radiusScale = 0.5f;
        }

        private static void ConfigureCardRenderer(ParticleSystem ps)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.World; // cards tumble in 3D and go edge-on
            renderer.SetActiveVertexStreams(new System.Collections.Generic.List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position,
                ParticleSystemVertexStream.Normal,
                ParticleSystemVertexStream.Color,
                ParticleSystemVertexStream.UV,
                ParticleSystemVertexStream.StableRandomX,
            });
        }

        // ---------------------------------------------------------------- helpers

        private static void Set(SerializedObject so, string property, float value) => so.FindProperty(property).floatValue = value;
        private static void Set(SerializedObject so, string property, Color value) => so.FindProperty(property).colorValue = value;

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
