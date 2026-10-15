using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Defensora.FX.Editor
{
    /// <summary>
    /// Fixes the Bouquet 1 (Fatima.fbx) materials, which import untextured and flat grey, and
    /// builds colour variants as prefabs. Each variant gets recoloured copies of the hand-painted
    /// petal textures (hue swapped, brush strokes kept) plus its own flower, pollen and ribbon
    /// colours. Stems are shared. Re-running updates everything in place.
    /// </summary>
    public static class BouquetBuilder
    {
        private const string Root = "Assets/3d assets/Bouquet 1";
        private const string ModelPath = Root + "/source/Fatima.fbx";
        private const string TexturesDir = Root + "/textures";
        private const string VariantsDir = Root + "/Variants";
        private const string PrefabsDir = "Assets/Prefabs/Bouquets";

        // The model is ~7.5 units tall; this makes it a ~0.6 m hand-held bouquet.
        private const float ModelScale = 0.08f;

        // Performance: the model is ~97k vertices in 57 parts, so prefabs use one merged mesh per
        // LOD, no shadow casting, and a lighter LOD1. Screen-height fractions: a 0.6 m bouquet
        // switches to LOD1 beyond ~3.5 m and is culled beyond ~17 m.
        private const bool CastShadows = false;
        private const float Lod1Below = 0.15f;
        private const float CullBelow = 0.03f;

        // Source material names (Turkish): sap = stem, yaprak/leaf = petal, polen = pollen,
        // kurdele = ribbon, acik = light, koyu = dark, sari = yellow, mavi = blue, beyaz = white.
        private const string PetalA = "A leaf shader";       // pink oval petals
        private const string PetalI = "I leaf shader";       // purple oval petals
        private const string PetalM = "M leaf shader";       // blue oval petals
        private const string FlowerBlue = "acik mavi aq";    // light-blue five-petal flowers
        private const string FlowerPink = "morumsu pembe aq"; // purple-pink five-petal flowers
        private const string PollenWhite = "beyaz polen aq";
        private const string PollenYellow = "sari polen aq";
        private const string Ribbon = "KURDELE";

        private static readonly Dictionary<string, string> PetalTextures = new Dictionary<string, string>
        {
            { PetalA, "A_leaf" },
            { PetalI, "I_leaf_text" },
            { PetalM, "M_leaf_text" },
        };

        // Original look, applied to the source materials themselves.
        private static readonly Dictionary<string, Color> SourceColors = new Dictionary<string, Color>
        {
            { "sari sap aq", Hex(0xA8B54A) },
            { "green sap aq", Hex(0x3C8F3A) },
            { "koyu green sap aq", Hex(0x1E5A2A) },
            { "acik green sap aq", Hex(0x7DCB5C) },
            { FlowerBlue, Hex(0xA9DCFF) },
            { FlowerPink, Hex(0xC964B4) },
            { PollenWhite, Hex(0xFFF6E0) },
            { PollenYellow, Hex(0xFFC21A) },
            { Ribbon, Hex(0xD61F2C) },
        };

        /// <summary>Hue in degrees (negative = keep the painted hue), saturation and value multipliers.</summary>
        private struct Recolor
        {
            public float Hue, Sat, Val;
            public Recolor(float hue, float sat = 1f, float val = 1f) { Hue = hue; Sat = sat; Val = val; }
            public static Recolor Keep => new Recolor(-1f);
        }

        private class Variant
        {
            public string Name;
            public Recolor A, I, M;
            public Color FlowerBlue, FlowerPink, PollenWhite, PollenYellow, Ribbon;
            public float Emission; // soft self-glow, for night scenes
        }

        private static readonly Variant[] Variants =
        {
            new Variant
            {
                Name = "Original", A = Recolor.Keep, I = Recolor.Keep, M = Recolor.Keep,
                FlowerBlue = Hex(0xA9DCFF), FlowerPink = Hex(0xC964B4),
                PollenWhite = Hex(0xFFF6E0), PollenYellow = Hex(0xFFC21A), Ribbon = Hex(0xD61F2C),
            },
            // Cempasuchil: the Dia de Muertos flower. Orange and gold, purple ribbon.
            new Variant
            {
                Name = "Marigold", A = new Recolor(28f, 2.6f, 1f), I = new Recolor(18f, 1.6f, 1.05f), M = new Recolor(45f, 1.3f, 1.05f),
                FlowerBlue = Hex(0xFFD23A), FlowerPink = Hex(0xFF7A12),
                PollenWhite = Hex(0xFFF2B0), PollenYellow = Hex(0xB5470B), Ribbon = Hex(0x6A1FB0),
            },
            // Rosa mexicano: hot pinks and violet, white flowers, gold ribbon.
            new Variant
            {
                Name = "Rosa", A = new Recolor(325f, 2.4f, 1f), I = new Recolor(300f, 1.4f, 0.95f), M = new Recolor(275f, 1.2f, 0.95f),
                FlowerBlue = Hex(0xFFF4FA), FlowerPink = Hex(0xFF1F8E),
                PollenWhite = Hex(0xFFD54A), PollenYellow = Hex(0xFFD54A), Ribbon = Hex(0xE8B230),
            },
            // Ofrenda: deep reds and plum, white flowers, gold ribbon.
            new Variant
            {
                Name = "Ofrenda", A = new Recolor(355f, 3f, 0.85f), I = new Recolor(285f, 1.5f, 0.75f), M = new Recolor(335f, 1.4f, 0.85f),
                FlowerBlue = Hex(0xF5F0E8), FlowerPink = Hex(0x9E1030),
                PollenWhite = Hex(0xFFC21A), PollenYellow = Hex(0x2B0A10), Ribbon = Hex(0xC9A227),
            },
            // Moonlight: pale ghostly whites, lavender and cyan with a faint glow.
            new Variant
            {
                Name = "Moonlight", A = new Recolor(250f, 0.35f, 0.95f), I = new Recolor(265f, 0.75f, 1f), M = new Recolor(190f, 0.85f, 1f),
                FlowerBlue = Hex(0xE6FBFF), FlowerPink = Hex(0xCDB8FF),
                PollenWhite = Hex(0xFFFFFF), PollenYellow = Hex(0xA8F0FF), Ribbon = Hex(0x20202A),
                Emission = 0.2f,
            },
        };

        [MenuItem("Tools/Defensora FX/6. Build Bouquet Variants", priority = 6)]
        public static void BuildAll()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[Defensora FX] Bouquet model not found at {ModelPath}.");
                return;
            }

            EnsureFolder(VariantsDir);
            EnsureFolder(PrefabsDir);

            var sources = FixSourceMaterials(model);
            combinedCache = null;

            foreach (var variant in Variants)
                BuildVariant(variant, model, sources);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Defensora FX] Fixed bouquet materials and built {Variants.Length} bouquet prefabs under {PrefabsDir}.");
        }

        // ---------------------------------------------------------------- materials

        private static Dictionary<string, Material> FixSourceMaterials(GameObject model)
        {
            var sources = new Dictionary<string, Material>();

            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || sources.ContainsKey(material.name))
                        continue;

                    sources[material.name] = material;
                    Undo.RecordObject(material, "Fix Bouquet Materials");

                    if (PetalTextures.TryGetValue(material.name, out string texName))
                    {
                        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexturesDir}/{texName}.png"));
                        material.SetColor("_BaseColor", Color.white);
                    }
                    else if (SourceColors.TryGetValue(material.name, out Color color))
                    {
                        material.SetColor("_BaseColor", color);
                    }

                    ApplyLook(material, IsFlat(material.name));
                    EditorUtility.SetDirty(material);
                }
            }

            return sources;
        }

        /// <summary>Petals and flowers are single planes: render both sides.</summary>
        private static bool IsFlat(string name) =>
            PetalTextures.ContainsKey(name) || name == FlowerBlue || name == FlowerPink || name == Ribbon;

        private static void ApplyLook(Material material, bool doubleSided)
        {
            material.SetFloat("_Cull", doubleSided ? 0f : 2f);
            material.doubleSidedGI = doubleSided;
            material.SetFloat("_Smoothness", 0.2f);
            material.SetFloat("_Metallic", 0f);
            material.enableInstancing = true;
        }

        private static void BuildVariant(Variant v, GameObject model, Dictionary<string, Material> sources)
        {
            string dir = $"{VariantsDir}/{v.Name}";
            EnsureFolder(dir);

            var map = new Dictionary<string, Material>();

            // Petals: recoloured copies of the painted textures.
            foreach (var pair in PetalTextures)
            {
                Recolor recolor = pair.Key == PetalA ? v.A : pair.Key == PetalI ? v.I : v.M;
                Texture2D texture = recolor.Hue < 0f
                    ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexturesDir}/{pair.Value}.png")
                    : RecolorTexture($"{TexturesDir}/{pair.Value}.png", $"{dir}/{pair.Value}_{v.Name}.png", recolor);

                var mat = VariantMaterial(sources[pair.Key], $"{dir}/M_Petal{pair.Key[0]}_{v.Name}.mat");
                mat.SetTexture("_BaseMap", texture);
                mat.SetColor("_BaseColor", Color.white);
                SetEmission(mat, texture, Color.white, v.Emission);
                map[pair.Key] = mat;
            }

            // Flat-coloured parts.
            var colors = new Dictionary<string, Color>
            {
                { FlowerBlue, v.FlowerBlue },
                { FlowerPink, v.FlowerPink },
                { PollenWhite, v.PollenWhite },
                { PollenYellow, v.PollenYellow },
                { Ribbon, v.Ribbon },
            };

            foreach (var pair in colors)
            {
                var mat = VariantMaterial(sources[pair.Key], $"{dir}/M_{Safe(pair.Key)}_{v.Name}.mat");
                mat.SetColor("_BaseColor", pair.Value);
                SetEmission(mat, null, pair.Value, pair.Key == Ribbon ? 0f : v.Emission);
                map[pair.Key] = mat;
            }

            // Anything the variant doesn't recolour (the stems) keeps its source material.
            foreach (var pair in sources)
                if (!map.ContainsKey(pair.Key))
                    map[pair.Key] = pair.Value;

            BuildPrefab(v, model, map);
        }

        private static Material VariantMaterial(Material source, string path)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(source);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.CopyPropertiesFromMaterial(source);
            }

            material.name = Path.GetFileNameWithoutExtension(path);
            // Variant prefabs use two-sided meshes for flat parts, so normal back-face culling is right.
            ApplyLook(material, false);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetEmission(Material material, Texture texture, Color color, float strength)
        {
            if (strength <= 0f)
            {
                material.DisableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                return;
            }

            material.EnableKeyword("_EMISSION");
            material.SetTexture("_EmissionMap", texture);
            material.SetColor("_EmissionColor", color * strength);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        /// <summary>Swaps the hue of every pixel, scaling saturation and value, so the painted strokes survive.</summary>
        private static Texture2D RecolorTexture(string sourcePath, string destPath, Recolor recolor)
        {
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            source.LoadImage(File.ReadAllBytes(sourcePath));

            Color[] pixels = source.GetPixels();
            float hue = recolor.Hue / 360f;

            for (int i = 0; i < pixels.Length; i++)
            {
                Color.RGBToHSV(pixels[i], out _, out float s, out float val);
                Color c = Color.HSVToRGB(hue, Mathf.Clamp01(s * recolor.Sat), Mathf.Clamp01(val * recolor.Val));
                c.a = pixels[i].a;
                pixels[i] = c;
            }

            source.SetPixels(pixels);
            File.WriteAllBytes(destPath, source.EncodeToPNG());
            Object.DestroyImmediate(source);

            AssetDatabase.ImportAsset(destPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(destPath);
        }

        // ---------------------------------------------------------------- prefab

        private static void BuildPrefab(Variant v, GameObject model, Dictionary<string, Material> map)
        {
            var meshes = BuildCombinedMeshes(model);
            var root = new GameObject($"Bouquet_{v.Name}");

            var lods = new LOD[2];
            for (int i = 0; i < 2; i++)
            {
                var (mesh, materialNames) = meshes[i];
                var go = new GameObject($"LOD{i}");
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;

                var renderer = go.AddComponent<MeshRenderer>();
                var mats = new Material[materialNames.Count];
                for (int m = 0; m < mats.Length; m++)
                    mats[m] = map.TryGetValue(materialNames[m], out Material mat) ? mat : null;
                renderer.sharedMaterials = mats;
                renderer.shadowCastingMode = CastShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

                lods[i] = new LOD(i == 0 ? Lod1Below : CullBelow, new Renderer[] { renderer });
            }

            var group = root.AddComponent<LODGroup>();
            group.SetLODs(lods);
            group.RecalculateBounds();

            PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabsDir}/Bouquet_{v.Name}.prefab");
            Object.DestroyImmediate(root);
        }

        private static (Mesh mesh, List<string> materials)[] combinedCache;

        /// <summary>
        /// Merges the model's ~57 parts into one mesh per LOD, with one submesh per source
        /// material, scaled and with the bottom of the stems at the origin. Shared by every variant.
        ///   LOD0: everything, flat parts made two-sided.
        ///   LOD1: single-sided, without the (very dense) ribbon and the pollen beads.
        /// </summary>
        private static (Mesh mesh, List<string> materials)[] BuildCombinedMeshes(GameObject model)
        {
            if (combinedCache != null && combinedCache[0].mesh != null)
                return combinedCache;

            var instance = Object.Instantiate(model);
            instance.transform.localScale = Vector3.one * ModelScale;

            var renderers = instance.GetComponentsInChildren<MeshRenderer>();
            var bounds = renderers[0].bounds;
            foreach (var r in renderers)
                bounds.Encapsulate(r.bounds);
            instance.transform.position = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);

            var result = new (Mesh, List<string>)[2];
            for (int lod = 0; lod < 2; lod++)
            {
                var byMaterial = new SortedDictionary<string, List<CombineInstance>>();
                var temp = new List<Mesh>();

                foreach (var r in renderers)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null)
                        continue;

                    var mats = r.sharedMaterials;
                    for (int sub = 0; sub < mats.Length && sub < mf.sharedMesh.subMeshCount; sub++)
                    {
                        string name = mats[sub] != null ? mats[sub].name : "";
                        if (lod == 1 && (name == Ribbon || name == PollenWhite || name == PollenYellow))
                            continue;

                        Mesh mesh = mf.sharedMesh;
                        int subMesh = sub;
                        if (lod == 0 && IsFlat(name))
                        {
                            mesh = DoubleSidedMesh(mesh);
                            temp.Add(mesh);
                        }

                        if (!byMaterial.TryGetValue(name, out var list))
                            byMaterial[name] = list = new List<CombineInstance>();
                        list.Add(new CombineInstance { mesh = mesh, subMeshIndex = subMesh, transform = r.localToWorldMatrix });
                    }
                }

                // One merged mesh per material, then those become the submeshes of the final mesh.
                var parts = new List<CombineInstance>();
                var names = new List<string>();
                foreach (var pair in byMaterial)
                {
                    var part = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    part.CombineMeshes(pair.Value.ToArray(), true, true);
                    temp.Add(part);
                    parts.Add(new CombineInstance { mesh = part, transform = Matrix4x4.identity });
                    names.Add(pair.Key);
                }

                EnsureFolder($"{VariantsDir}/Meshes");
                string path = $"{VariantsDir}/Meshes/Bouquet_LOD{lod}.asset";
                var combined = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                bool create = combined == null;
                if (create)
                    combined = new Mesh();
                else
                    combined.Clear();

                combined.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                combined.CombineMeshes(parts.ToArray(), false, false);
                combined.name = $"Bouquet_LOD{lod}";
                combined.RecalculateBounds();

                if (create)
                    AssetDatabase.CreateAsset(combined, path);
                else
                    EditorUtility.SetDirty(combined);

                foreach (var m in temp)
                    Object.DestroyImmediate(m);

                result[lod] = (combined, names);
            }

            Object.DestroyImmediate(instance);
            combinedCache = result;
            return result;
        }

        /// <summary>
        /// URP/Lit doesn't flip normals on back faces, so single-sided planes go dark when seen
        /// from behind. This returns an in-memory copy with every triangle
        /// duplicated, reversed and given flipped normals/tangents.
        /// </summary>
        private static Mesh DoubleSidedMesh(Mesh source)
        {
            var mesh = new Mesh();

            int n = source.vertexCount;
            Vector3[] verts = source.vertices;
            Vector3[] normals = source.normals;
            Vector4[] tangents = source.tangents;
            Vector2[] uv = source.uv;
            Color[] colors = source.colors;

            var v2 = new Vector3[n * 2];
            var n2 = new Vector3[n * 2];
            var uv2 = new Vector2[n * 2];
            for (int i = 0; i < n; i++)
            {
                v2[i] = v2[i + n] = verts[i];
                n2[i] = normals.Length == n ? normals[i] : Vector3.up;
                n2[i + n] = -n2[i];
                uv2[i] = uv2[i + n] = uv.Length == n ? uv[i] : Vector2.zero;
            }

            mesh.indexFormat = n * 2 > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = v2;
            mesh.normals = n2;
            mesh.uv = uv2;

            if (tangents.Length == n)
            {
                var t2 = new Vector4[n * 2];
                for (int i = 0; i < n; i++)
                {
                    t2[i] = tangents[i];
                    t2[i + n] = new Vector4(-tangents[i].x, -tangents[i].y, -tangents[i].z, tangents[i].w);
                }
                mesh.tangents = t2;
            }

            if (colors.Length == n)
            {
                var c2 = new Color[n * 2];
                for (int i = 0; i < n; i++)
                    c2[i] = c2[i + n] = colors[i];
                mesh.colors = c2;
            }

            mesh.subMeshCount = source.subMeshCount;
            for (int s = 0; s < source.subMeshCount; s++)
            {
                int[] tris = source.GetTriangles(s);
                var t = new int[tris.Length * 2];
                for (int i = 0; i < tris.Length; i += 3)
                {
                    t[i] = tris[i];
                    t[i + 1] = tris[i + 1];
                    t[i + 2] = tris[i + 2];
                    // Back side: reversed winding, offset into the flipped-normal copy.
                    t[tris.Length + i] = tris[i] + n;
                    t[tris.Length + i + 1] = tris[i + 2] + n;
                    t[tris.Length + i + 2] = tris[i + 1] + n;
                }
                mesh.SetTriangles(t, s);
            }

            mesh.RecalculateBounds();
            mesh.name = source.name + "_2Sided";
            return mesh;
        }

        // ---------------------------------------------------------------- helpers

        private static string Safe(string name) => name.Replace(" aq", "").Replace(' ', '_');

        private static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
