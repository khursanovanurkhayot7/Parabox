using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Parabox.EditorTools
{
    // ONE-TIME setup wizard (URP / WebGL, polished sprite look).
    // Run "Tools > Parabox > Create Everything (Run Once)".
    //
    // It sets up URP, generates rounded-rectangle sprites procedurally, builds
    // sprite tile prefabs, 30 level prefabs, the MainMenu and Game scenes and adds
    // them to Build Settings. Safe to run again — it regenerates everything.
    //
    // After running it you can delete the Assets/Parabox/Editor folder.
    // DO NOT delete Assets/Parabox/Scripts — the game needs it!
    public static class ParaboxSetupWizard
    {
        const string Root = "Assets/Parabox";
        const string SpriteDir = Root + "/Sprites";
        const string PrefabDir = Root + "/Prefabs";
        const string LevelDir = Root + "/Prefabs/Levels";
        const string SceneDir = Root + "/Scenes";
        const string SettingsDir = Root + "/Settings";

        // ---------------------------------------------------------- palette
        static readonly Color BG = Hex("0B0E17");
        static readonly Color Accent = Hex("FF9E5E");
        static readonly Color DimText = Hex("8A92B5");
        static readonly Color ButtonCol = Hex("1C4C60");   // deep ocean-slate for secondary/back/quit buttons

        // Room floor colors, indexed by room id. Bright & saturated like the real game.
        static readonly Color[] RoomColors =
        {
            Hex("2E6FD8"), // 0 blue   (main rooms)
            Hex("C43F82"), // 1 magenta (interiors)
            Hex("E0912F"), // 2 orange
            Hex("3FA06A"), // 3 green
            Hex("7E57D8"), // 4 purple
        };

        static readonly Color BoxColor = Hex("F2A03E");
        static readonly Color PlayerColor = Hex("FF5FA0");
        static readonly Color EyeColor = Hex("241019");
        static readonly Color WallColor = Hex("141A28");
        static readonly Color ShadowCol = new Color(0f, 0f, 0f, 0.30f);

        // Sorting orders.
        const int OrderBackingInBox = -50;
        const int OrderFloor = 0;
        const int OrderBorder = 1;
        const int OrderGoal = 2;
        const int OrderWall = 4;
        const int OrderBox = 6;
        const int OrderPlayer = 8;
        const int OrderFrameInBox = 50;

        const int Res = 256; // sprite resolution (also pixels-per-unit → 1 sprite = 1 world unit)

        class Sprites { public Sprite fill, tile, grid, ringThin, ringThick, shaded, shadow, lockIcon, star, glow, vignette, disc, ringCircle, iconPlay, texGrass, texWater, texRock, texStars, iconTri, iconDrop, iconTreeR, iconDropR, iconStarR, iconWaveR, iconCoral, iconJelly, godRay; }

        // Rounded sprite + top-light gloss used to make every button look premium (set once sprites exist).
        static Sprite s_roundBtn;
        static Sprite s_gloss;
        class Tiles { public GameObject floor, grid, border, wall, box, metaBox, player, boxGoal, playerGoal; }

        [MenuItem("Tools/Parabox/Create Everything (Run Once)")]
        public static void CreateEverything()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CleanGenerated();
            CreateFolders();

            SetupUrpPipeline();

            var spr = CreateSprites();
            s_roundBtn = spr.fill;
            s_gloss = spr.shaded;
            var tiles = CreateTilePrefabs(spr);
            var levels = CreateLevelPrefabs(tiles);
            string gamePath = CreateGameScene(spr, tiles, levels);
            string menuPath = CreateMainMenuScene(spr, levels.Length);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(menuPath, true),
                new EditorBuildSettingsScene(gamePath, true)
            };

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(menuPath);

            EditorUtility.DisplayDialog("Parabox",
                "Setup complete!\n\n" +
                "URP assigned, rounded sprites generated, tile prefabs, 30 level\n" +
                "prefabs, MainMenu + Game scenes created and added to Build Settings.\n\n" +
                "You can now DELETE the folder Assets/Parabox/Editor.\n" +
                "Keep Assets/Parabox/Scripts — the game needs it!",
                "OK");
        }

        // ================================================= URP pipeline
        static void SetupUrpPipeline()
        {
            string rendererPath = SettingsDir + "/Parabox_URP_Renderer.asset";
            string pipelinePath = SettingsDir + "/Parabox_URP.asset";

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                TryReloadResources(rendererData);
                AssetDatabase.CreateAsset(rendererData, rendererPath);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipeline, pipelinePath);
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);
            AssetDatabase.SaveAssets();
        }

        static void TryReloadResources(Object target)
        {
            try
            {
                var type = System.Type.GetType(
                    "UnityEngine.Rendering.ResourceReloader, Unity.RenderPipelines.Core.Editor");
                var method = type?.GetMethod("ReloadAllNullIn",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                method?.Invoke(null, new object[] { target, "Packages/com.unity.render-pipelines.universal" });
            }
            catch { /* URP fills most of this in automatically */ }
        }

        // ================================================= folders / cleanup
        static void CleanGenerated()
        {
            AssetDatabase.DeleteAsset(SpriteDir);
            AssetDatabase.DeleteAsset(PrefabDir);
            AssetDatabase.DeleteAsset(SceneDir);
            AssetDatabase.DeleteAsset(SettingsDir);
            AssetDatabase.DeleteAsset(Root + "/Materials"); // leftover from the first HDRP run
        }

        static void CreateFolders()
        {
            EnsureFolder(Root);
            EnsureFolder(SpriteDir);
            EnsureFolder(PrefabDir);
            EnsureFolder(LevelDir);
            EnsureFolder(SceneDir);
            EnsureFolder(SettingsDir);
            EnsureFolder(Root + "/Art");   // drop a custom MenuBG.png here (kept across re-runs)
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int i = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, i));
            AssetDatabase.CreateFolder(path.Substring(0, i), path.Substring(i + 1));
        }

        // ================================================= procedural sprites
        static Sprites CreateSprites()
        {
            return new Sprites
            {
                // filled rounded square — boxes, player (sliced border for the floor base)
                fill = MakeSprite("Fill", 0.22f, 0f, Mathf.RoundToInt(0.22f * Res) + 10, true),
                // barely-rounded square — walls (solid blocks that fill their cell)
                tile = MakeSprite("Tile", 0.11f, 0f, 0, false),
                // hairline grid, tiled over the flat floor (thin lines on cell edges)
                grid = MakeGridSprite(),
                // thin rounded ring — room border (sliced to room size)
                ringThin = MakeSprite("RingThin", 0.18f, 0.05f, Mathf.RoundToInt(0.23f * Res) + 8, true),
                // thick rounded ring — goals and meta-box frame (uniform scale)
                ringThick = MakeSprite("RingThick", 0.24f, 0.13f, 0, false),
                // top-lit gradient rounded square — boxes & player look rounded / 3D
                shaded = MakeShadedSprite("Shaded", 0.22f, 1.0f, 0.72f),
                // soft dark blob — drop shadow under boxes & player
                shadow = MakeShadowSprite(),
                // premium padlock icon — locked levels on the level-select screen
                lockIcon = MakeLockSprite(),
                // 5-point star — the Advanced difficulty icon
                star = MakeStarSprite(),
                // soft radial glow + vignette — premium atmospheric backgrounds
                glow = MakeGlowSprite(),
                vignette = MakeVignetteSprite(),
                // filled circle + circular ring — the premium radial countdown timer
                disc = MakeDiscSprite(),
                ringCircle = MakeRingCircleSprite(),
                iconPlay = MakePlaySprite(),
                // tileable per-tier floor surfaces: grass mottle / water ripples / cracked rock
                texGrass = MakeFloorTexSprite("TexGrass", 0),
                texWater = MakeFloorTexSprite("TexWater", 1),
                texRock = MakeFloorTexSprite("TexRock", 2),
                texStars = MakeFloorTexSprite("TexStars", 3),
                // world icons: triangle (tree canopy) + teardrop (droplet / flame)
                iconTri = MakeTriSprite(),
                iconDrop = MakeDropSprite(),
                // realistic shaded world icons (colour + lighting baked in)
                iconTreeR = MakeTreeIconSprite(),
                iconDropR = MakeDropIconSprite(),
                iconStarR = MakeStarIconSprite(),
                iconWaveR = MakeWaveIconSprite(),
                // ocean-region icons + underwater god-ray shaft
                iconCoral = MakeCoralIconSprite(),
                iconJelly = MakeJellyIconSprite(),
                godRay = MakeGodRaySprite(),
            };
        }

        static Sprite MakeSprite(string name, float radiusFrac, float ringFrac, int borderPx, bool sliced)
        {
            var tex = RoundedTex(Res, radiusFrac, ringFrac);
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            string assetPath = SpriteDir + "/" + name + ".png";
            string absPath = Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
            File.WriteAllBytes(absPath, png);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            var imp = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = Res;
            imp.mipmapEnabled = false;
            imp.filterMode = FilterMode.Bilinear;
            imp.alphaIsTransparency = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.wrapMode = TextureWrapMode.Clamp;

            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteBorder = sliced ? new Vector4(borderPx, borderPx, borderPx, borderPx) : Vector4.zero;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteExtrude = 0;
            imp.SetTextureSettings(settings);
            imp.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        // Signed-distance rounded rect, white with an anti-aliased alpha edge.
        // ringFrac <= 0 => filled; otherwise a ring of that thickness (fraction of size).
        static Texture2D RoundedTex(int s, float radiusFrac, float ringFrac)
        {
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float c = s * 0.5f;
            float half = s * 0.5f - 2f;
            float r = Mathf.Min(radiusFrac * s, half);
            float ring = ringFrac * s;

            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - c) - (half - r);
                    float dy = Mathf.Abs(y + 0.5f - c) - (half - r);
                    float outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) +
                                               Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
                    float d = outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - r; // <0 inside

                    float a = Mathf.Clamp01(0.5f - d);
                    if (ring > 0f) a *= Mathf.Clamp01(0.5f + (d + ring)); // subtract inner disc
                    px[y * s + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        // A 1-cell sprite with thin opaque lines on its left and bottom edges. Tiled over
        // the floor it draws a hairline grid at every cell boundary — no per-cell "plates".
        static Sprite MakeGridSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
            int lw = Mathf.Max(2, Mathf.RoundToInt(0.03f * s));
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                    px[y * s + x] = (x < lw || y < lw)
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
            tex.SetPixels32(px);
            tex.Apply();

            string assetPath = SpriteDir + "/Grid.png";
            string absPath = Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
            File.WriteAllBytes(absPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            var imp = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = Res;
            imp.mipmapEnabled = false;
            imp.filterMode = FilterMode.Point;
            imp.alphaIsTransparency = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.wrapMode = TextureWrapMode.Repeat;

            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteBorder = Vector4.zero;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            imp.SetTextureSettings(settings);
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        // Rounded square with a top-to-bottom brightness gradient baked in (white top,
        // grey bottom). Tinted by the object's color it reads as a soft, rounded 3D piece.
        static Sprite MakeShadedSprite(string name, float radiusFrac, float topMul, float botMul)
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float c = s * 0.5f, half = s * 0.5f - 2f, r = Mathf.Min(radiusFrac * s, half);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - c) - (half - r);
                    float dy = Mathf.Abs(y + 0.5f - c) - (half - r);
                    float outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) +
                                               Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
                    float d = outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - r;
                    float a = Mathf.Clamp01(0.5f - d);
                    float m = Mathf.Lerp(botMul, topMul, y / (s - 1f)); // y=0 bottom, y=s-1 top
                    byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(m) * 255f);
                    px[y * s + x] = new Color32(v, v, v, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return SaveTexAsSprite(tex, name, false, 0, FilterMode.Bilinear);
        }

        // Soft dark blob for a drop shadow (black, feathered edges).
        static Sprite MakeShadowSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float c = s * 0.5f, half = s * 0.5f - 2f, r = Mathf.Min(0.30f * s, half);
            float feather = 0.16f * s;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - c) - (half - r);
                    float dy = Mathf.Abs(y + 0.5f - c) - (half - r);
                    float outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) +
                                               Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
                    float d = outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - r;
                    float a = Mathf.Clamp01(0.5f - d / feather);
                    px[y * s + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return SaveTexAsSprite(tex, "Shadow", false, 0, FilterMode.Bilinear);
        }

        // A clean padlock icon (white, tinted gold at runtime): a rounded body with a
        // keyhole, topped by a semicircular shackle. Reads as "locked" on level cards.
        static Sprite MakeLockSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];

            float cx = s * 0.5f;
            float bodyTop = 0.60f * s, bodyBot = 0.14f * s;
            float bodyCx = cx, bodyCy = (bodyTop + bodyBot) * 0.5f;
            float bodyHalfW = 0.24f * s, bodyHalfH = (bodyTop - bodyBot) * 0.5f, bodyRad = 0.07f * s;

            float shCx = cx, shCy = bodyTop;          // shackle sits on the body's top edge
            float ro = 0.17f * s, ri = 0.105f * s;     // shackle outer / inner radius

            float khCx = cx, khCy = 0.36f * s, khR = 0.05f * s; // keyhole

            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;

                    // body: rounded-rect signed distance (<0 inside)
                    float dx = Mathf.Abs(fx - bodyCx) - (bodyHalfW - bodyRad);
                    float dy = Mathf.Abs(fy - bodyCy) - (bodyHalfH - bodyRad);
                    float outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) +
                                               Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
                    float dBody = outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - bodyRad;
                    float aBody = Mathf.Clamp01(0.5f - dBody);

                    // shackle: upper half of an annulus (the U on top)
                    float dr = Mathf.Sqrt((fx - shCx) * (fx - shCx) + (fy - shCy) * (fy - shCy));
                    float aRing = Mathf.Clamp01(0.5f + (ro - dr)) * Mathf.Clamp01(0.5f + (dr - ri));
                    if (fy < shCy) aRing = 0f;

                    float a = Mathf.Max(aBody, aRing);

                    // keyhole: circle + a short slot, cut out of the body
                    float dk = Mathf.Sqrt((fx - khCx) * (fx - khCx) + (fy - khCy) * (fy - khCy));
                    float cut = Mathf.Clamp01(0.5f - (khR - dk));
                    if (fx > khCx - 0.022f * s && fx < khCx + 0.022f * s && fy < khCy && fy > khCy - 0.12f * s)
                        cut = 0f;
                    a *= cut;

                    px[y * s + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }

            tex.SetPixels32(px);
            tex.Apply();
            return SaveTexAsSprite(tex, "Lock", false, 0, FilterMode.Bilinear);
        }

        // Shared texture -> Sprite importer used by the generators above.
        static Sprite SaveTexAsSprite(Texture2D tex, string name, bool sliced, int borderPx, FilterMode filter)
        {
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            string assetPath = SpriteDir + "/" + name + ".png";
            string absPath = Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
            File.WriteAllBytes(absPath, png);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            var imp = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = Res;
            imp.mipmapEnabled = false;
            imp.filterMode = filter;
            imp.alphaIsTransparency = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.wrapMode = TextureWrapMode.Clamp;

            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteBorder = sliced ? new Vector4(borderPx, borderPx, borderPx, borderPx) : Vector4.zero;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteExtrude = 0;
            imp.SetTextureSettings(settings);
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        // ================================================= tile prefabs
        static Tiles CreateTilePrefabs(Sprites s)
        {
            var t = new Tiles();

            // Floor — sliced sprite sized to the room at runtime.
            var floor = new GameObject("Floor");
            var fsr = floor.AddComponent<SpriteRenderer>();
            fsr.sprite = s.fill; fsr.color = RoomColors[0];
            fsr.drawMode = SpriteDrawMode.Sliced; fsr.size = Vector2.one; fsr.sortingOrder = OrderFloor;
            t.floor = SavePrefab(floor, PrefabDir + "/Floor.prefab");

            // Grid — a hairline overlay tiled over the flat floor at runtime. Gives the
            // room a subtle grid WITHOUT a rounded plate under every object.
            var grid = new GameObject("Grid");
            var gsr = grid.AddComponent<SpriteRenderer>();
            gsr.sprite = s.grid; gsr.color = new Color(0f, 0f, 0f, 0.12f);
            gsr.drawMode = SpriteDrawMode.Tiled; gsr.size = Vector2.one; gsr.sortingOrder = OrderFloor + 1;
            t.grid = SavePrefab(grid, PrefabDir + "/Grid.prefab");

            // Room border — thin ring, sliced to the room.
            var border = new GameObject("Border");
            var bsr = border.AddComponent<SpriteRenderer>();
            bsr.sprite = s.ringThin; bsr.color = Color.white;
            bsr.drawMode = SpriteDrawMode.Sliced; bsr.size = Vector2.one; bsr.sortingOrder = OrderBorder;
            t.border = SavePrefab(border, PrefabDir + "/Border.prefab");

            // Wall — dark block that fills its cell (reads as a solid barrier).
            var wall = new GameObject("Wall");
            SpriteChild("Sprite", wall.transform, s.tile, WallColor, OrderWall, Vector3.zero, V(1.0f));
            t.wall = SavePrefab(wall, PrefabDir + "/Wall.prefab");

            // Box — a soft drop shadow behind a top-lit rounded square (reads as 3D).
            var box = new GameObject("Box");
            box.AddComponent<EntityView>();
            SpriteChild("Shadow", box.transform, s.shadow, ShadowCol, OrderBox - 1, new Vector3(0f, -0.06f, 0f), V(1.0f));
            SpriteChild("Sprite", box.transform, s.shaded, BoxColor, OrderBox, Vector3.zero, V(0.92f));
            t.box = SavePrefab(box, PrefabDir + "/Box.prefab");

            // MetaBox — dark backing + colored frame. Interior room is nested at runtime;
            // a SortingGroup isolates the contents so nesting sorts correctly.
            var meta = new GameObject("MetaBox");
            meta.AddComponent<EntityView>();
            meta.AddComponent<SortingGroup>().sortingOrder = OrderBox;
            SpriteChild("Shadow", meta.transform, s.shadow, ShadowCol, OrderBackingInBox - 10, new Vector3(0f, -0.06f, 0f), V(1.0f));
            SpriteChild("Backing", meta.transform, s.shaded, Darken(RoomColors[1], 0.5f), OrderBackingInBox, Vector3.zero, V(0.9f));
            SpriteChild("Frame", meta.transform, s.ringThick, Lighten(RoomColors[1], 0.3f), OrderFrameInBox, Vector3.zero, V(0.9f));
            t.metaBox = SavePrefab(meta, PrefabDir + "/MetaBox.prefab");

            // Player — a shadow behind a top-lit rounded box with two eyes; eyes blink.
            var player = new GameObject("Player");
            player.AddComponent<EntityView>();
            SpriteChild("Shadow", player.transform, s.shadow, ShadowCol, OrderPlayer - 1, new Vector3(0f, -0.06f, 0f), V(0.98f));
            SpriteChild("Body", player.transform, s.shaded, PlayerColor, OrderPlayer, Vector3.zero, V(0.88f));
            var eyeL = SpriteChild("EyeL", player.transform, s.fill, EyeColor, OrderPlayer + 1, new Vector3(-0.17f, 0.07f, 0f), new Vector3(0.14f, 0.21f, 1f));
            var eyeR = SpriteChild("EyeR", player.transform, s.fill, EyeColor, OrderPlayer + 1, new Vector3(0.17f, 0.07f, 0f), new Vector3(0.14f, 0.21f, 1f));
            var blink = player.AddComponent<Blinker>();
            blink.eyeL = eyeL.transform;
            blink.eyeR = eyeR.transform;
            t.player = SavePrefab(player, PrefabDir + "/Player.prefab");

            // Goals — thick rounded outline in the target's color.
            var boxGoal = new GameObject("BoxGoal");
            SpriteChild("Ring", boxGoal.transform, s.ringThick, BoxColor, OrderGoal, Vector3.zero, V(0.72f));
            t.boxGoal = SavePrefab(boxGoal, PrefabDir + "/BoxGoal.prefab");

            var playerGoal = new GameObject("PlayerGoal");
            SpriteChild("Ring", playerGoal.transform, s.ringThick, PlayerColor, OrderGoal, Vector3.zero, V(0.72f));
            t.playerGoal = SavePrefab(playerGoal, PrefabDir + "/PlayerGoal.prefab");

            return t;
        }

        static GameObject SpriteChild(string name, Transform parent, Sprite sprite, Color color,
                                      int order, Vector3 localPos, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            return go;
        }

        static Vector3 V(float f) => new Vector3(f, f, 1f);

        static GameObject SavePrefab(GameObject go, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ================================================= level definitions
        // Rooms are borderless — the grid edge IS the boundary (no perimeter walls).
        //   # wall   . floor   P player   b box
        //   1..9 meta-box containing that room id
        //   x box goal   p player goal
        class LevelDef { public string name; public string[][] rooms; }

        static LevelDef[] Levels() => new[]
        {
            new LevelDef { name = "First Steps", rooms = new[] { new[] { "......", "......", "...b..", "..Px..", "......", "......", "......" } } },  // par 4
            new LevelDef { name = "Little Nudge", rooms = new[] { new[] { "...P..", "....b.", "......", "......", "......", "....x.", "......" } } },  // par 5
            new LevelDef { name = "Around the Bend", rooms = new[] { new[] { "...b.x", "......", "......", "..P...", "......", "......", "......" } } },  // par 5
            new LevelDef { name = "The Long Way", rooms = new[] { new[] { ".....", "..P..", "..b..", "...#.", "...x." } } },  // par 5
            new LevelDef { name = "Up and Over", rooms = new[] { new[] { ".......", ".......", ".....p.", "....Pb.", ".......", ".....x." } } },  // par 6
            new LevelDef { name = "Zigzag", rooms = new[] { new[] { ".......", ".......", ".......", "P......", ".b....x" } } },  // par 6
            new LevelDef { name = "Company", rooms = new[] { new[] { "....P..", "....b..", ".......", ".......", ".....x." } } },  // par 6
            new LevelDef { name = "Sidestep", rooms = new[] { new[] { "......x", "..#....", ".....bP", ".......", ".......", "......." } } },  // par 7
            new LevelDef { name = "Past the Wall", rooms = new[] { new[] { "#...P.", "......", "......", ".b....", "px...." } } },  // par 8
            new LevelDef { name = "Tight Corner", rooms = new[] { new[] { "..x...", "......", ".P....", "b.b...", "......", "x.....", "......" } } },  // par 8
            new LevelDef { name = "Double Trouble", rooms = new[]
            {
                new[] { "....1", "..P.b", "....." },
                new[] { ".x.", "..p", "..." },
            }},  // par 7
            new LevelDef { name = "Crossroads", rooms = new[]
            {
                new[] { "....P.", "..b...", "......", ".1....", "......" },
                new[] { "...", "..x", "..." },
            }},  // par 8
            new LevelDef { name = "Think Inside", rooms = new[]
            {
                new[] { "P.x...", ".1....", ".b...." },
                new[] { "...", ".p.", "..." },
            }},  // par 9
            new LevelDef { name = "Tuck It In", rooms = new[]
            {
                new[] { ".b....", "1.....", "P....." },
                new[] { "...", "..x", "..p" },
            }},  // par 9
            new LevelDef { name = "Special Delivery", rooms = new[]
            {
                new[] { "x.1..", ".bP..", "b....", ".x...", "....." },
                new[] { "...", ".p.", "..." },
            }},  // par 10
            new LevelDef { name = "Housewarming", rooms = new[]
            {
                new[] { "...1.", ".....", "...b.", ".....", ".P..." },
                new[] { "..x", "...", "..." },
            }},  // par 10
            new LevelDef { name = "Pocket", rooms = new[]
            {
                new[] { ".......", ".....1.", ".......", "b...P..", ".....x." },
                new[] { "...", "..p", "..." },
            }},  // par 10
            new LevelDef { name = "Nesting", rooms = new[]
            {
                new[] { ".......", "..b...P", ".1.x..." },
                new[] { "...", "...", "..." },
            }},  // par 10
            new LevelDef { name = "Roommates", rooms = new[]
            {
                new[] { ".1...", ".....", "...b.", ".....", "..P.." },
                new[] { "...", "x..", "..." },
            }},  // par 11
            new LevelDef { name = "Moving In", rooms = new[]
            {
                new[] { "....P", "...b.", "1...." },
                new[] { "...", "...", "x.." },
            }},  // par 12
            new LevelDef { name = "Homebound", rooms = new[]
            {
                new[] { ".b..P.", "......", "....1." },
                new[] { ".2.", "...", "..." },
                new[] { "...", ".x.", "..." },
            }},  // par 11
            new LevelDef { name = "Split Errand", rooms = new[]
            {
                new[] { ".....", "..b..", ".P.1." },
                new[] { "...", "..2", "..." },
                new[] { "...", ".x.", "..." },
            }},  // par 11
            new LevelDef { name = "Fetch", rooms = new[]
            {
                new[] { "..1b..", "......", "....P." },
                new[] { "...", "...", ".2." },
                new[] { "...", ".x.", "..." },
            }},  // par 12
            new LevelDef { name = "Settle Down", rooms = new[]
            {
                new[] { ".1...", "...P.", "b...." },
                new[] { "..2", "...", "..." },
                new[] { "...", ".x.", "..." },
            }},  // par 13
            new LevelDef { name = "Twin Rooms", rooms = new[]
            {
                new[] { "1.....", "...b..", ".....P" },
                new[] { "...", "...", "..2" },
                new[] { "...", ".x.", "..." },
            }},  // par 13
            new LevelDef { name = "Rabbit Hole", rooms = new[]
            {
                new[] { "....b..", "...2.b1", ".....P." },
                new[] { "...", ".x.", "..." },
                new[] { "...", ".x.", "..." },
            }},  // par 13
            new LevelDef { name = "Go Deeper", rooms = new[]
            {
                new[] { "......", "1...P.", "....b." },
                new[] { "...", "2..", "..." },
                new[] { "...", ".x.", "..." },
            }},  // par 14
            new LevelDef { name = "Two Deep", rooms = new[]
            {
                new[] { ".2.....", "....b..", "....1P.", "....b..", "......." },
                new[] { "...", ".x.", "..." },
                new[] { "...", ".x.", "..." },
            }},  // par 14
            new LevelDef { name = "Descent", rooms = new[]
            {
                new[] { ".b.....", "P...b..", "...2..1" },
                new[] { "...", ".x.", "..." },
                new[] { "...", ".x.", "..." },
            }},  // par 14
            new LevelDef { name = "Double Delivery", rooms = new[]
            {
                new[] { ".b.2...", "P..1b..", "......." },
                new[] { "...", ".x.", "..." },
                new[] { "...", ".x.", "..." },
            }},  // par 14
        };

        // ================================================= level prefabs
        static GameObject[] CreateLevelPrefabs(Tiles t)
        {
            var defs = Levels();
            var result = new GameObject[defs.Length];
            for (int i = 0; i < defs.Length; i++)
                result[i] = BuildLevelPrefab(i, defs[i], t);
            return result;
        }

        static GameObject BuildLevelPrefab(int index, LevelDef def, Tiles t)
        {
            var root = new GameObject("Level_" + (index + 1));
            root.AddComponent<ParaboxLevel>().levelName = def.name;

            float offsetX = 0f;
            for (int r = 0; r < def.rooms.Length; r++)
            {
                string[] rows = def.rooms[r];
                int h = rows.Length;
                int w = rows[0].Length;

                var roomGO = new GameObject("Room_" + r);
                roomGO.transform.SetParent(root.transform, false);
                roomGO.transform.localPosition = new Vector3(offsetX + w * 0.5f, 0f, 0f);
                offsetX += w + 2f;

                var rm = roomGO.AddComponent<RoomMarker>();
                rm.roomId = r; rm.width = w; rm.height = h;

                Color floorC = RoomColors[r % RoomColors.Length];

                var floor = (GameObject)PrefabUtility.InstantiatePrefab(t.floor, roomGO.transform);
                floor.transform.localPosition = Vector3.zero;
                var fsr = floor.GetComponent<SpriteRenderer>();
                fsr.size = new Vector2(w, h); fsr.color = floorC;

                var border = (GameObject)PrefabUtility.InstantiatePrefab(t.border, roomGO.transform);
                border.transform.localPosition = Vector3.zero;
                var bsr = border.GetComponent<SpriteRenderer>();
                bsr.size = new Vector2(w, h); bsr.color = Lighten(floorC, 0.32f);

                for (int row = 0; row < h; row++)
                {
                    for (int col = 0; col < rows[row].Length; col++)
                    {
                        char ch = rows[row][col];
                        if (ch == '.') continue;

                        int x = col;
                        int y = h - 1 - row; // top row of the string = highest y
                        var pos = new Vector3(x - (w - 1) * 0.5f, y - (h - 1) * 0.5f, 0f);

                        if (ch == '#')
                        {
                            var go = Place(t.wall, roomGO.transform, pos);
                            var mk = go.AddComponent<WallMarker>(); mk.x = x; mk.y = y;
                        }
                        else if (ch == 'b')
                        {
                            var go = Place(t.box, roomGO.transform, pos);
                            var mk = go.AddComponent<BoxMarker>(); mk.x = x; mk.y = y; mk.containsRoomId = -1;
                        }
                        else if (ch >= '1' && ch <= '9')
                        {
                            var go = Place(t.metaBox, roomGO.transform, pos);
                            var mk = go.AddComponent<BoxMarker>(); mk.x = x; mk.y = y; mk.containsRoomId = ch - '0';
                            Color inC = RoomColors[(ch - '0') % RoomColors.Length];
                            var back = go.transform.Find("Backing"); if (back) back.GetComponent<SpriteRenderer>().color = Darken(inC, 0.5f);
                            var frame = go.transform.Find("Frame"); if (frame) frame.GetComponent<SpriteRenderer>().color = Lighten(inC, 0.3f);
                        }
                        else if (ch == 'P')
                        {
                            var go = Place(t.player, roomGO.transform, pos);
                            var mk = go.AddComponent<PlayerMarker>(); mk.x = x; mk.y = y;
                        }
                        else if (ch == 'x')
                        {
                            var go = Place(t.boxGoal, roomGO.transform, pos);
                            var mk = go.AddComponent<GoalMarker>(); mk.x = x; mk.y = y; mk.forPlayer = false;
                        }
                        else if (ch == 'p')
                        {
                            var go = Place(t.playerGoal, roomGO.transform, pos);
                            var mk = go.AddComponent<GoalMarker>(); mk.x = x; mk.y = y; mk.forPlayer = true;
                        }
                    }
                }
            }

            return SavePrefab(root, LevelDir + "/Level_" + (index + 1) + ".prefab");
        }

        static GameObject Place(GameObject prefab, Transform parent, Vector3 pos)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.localPosition = pos;
            return go;
        }

        // ================================================= scenes
        static string CreateGameScene(Sprites spr, Tiles t, GameObject[] levels)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cam = CreateCamera(true);
            var follow = cam.gameObject.AddComponent<CameraFollow>();
            follow.padding = 1.05f;      // board fits the middle band, clear of the top title + bottom controls
            follow.topFrac = 0.16f;
            follow.bottomFrac = 0.20f;

            // Tier-tinted atmospheric backdrop behind the board (matches the level-select tiers).
            var backdrop = new GameObject("Backdrop");
            backdrop.transform.SetParent(cam.transform, false);
            backdrop.transform.localPosition = new Vector3(0f, 0f, 20f);
            var bd = backdrop.AddComponent<CameraBackdrop>();
            bd.cam = cam;
            bd.glow1 = MakeWorldSprite(backdrop.transform, "Glow1", spr.glow, -200);
            bd.glow2 = MakeWorldSprite(backdrop.transform, "Glow2", spr.glow, -199);
            bd.vignette = MakeWorldSprite(backdrop.transform, "Vignette", spr.vignette, -198);
            // underwater god-ray shafts slanting down from the surface (subtler as the water deepens)
            bd.rays = new[]
            {
                MakeWorldSprite(backdrop.transform, "Ray0", spr.godRay, -197),
                MakeWorldSprite(backdrop.transform, "Ray1", spr.godRay, -197),
                MakeWorldSprite(backdrop.transform, "Ray2", spr.godRay, -197),
            };
            var bdThemes = CategoryThemes();
            bd.baseColors  = new[] { bdThemes[0].bg, bdThemes[1].bg, bdThemes[2].bg };
            bd.glowAColors = new[] { WithAlpha(bdThemes[0].accent, 0.18f), WithAlpha(bdThemes[1].accent, 0.20f), WithAlpha(bdThemes[2].accent, 0.24f) };
            // secondary lower glow per depth — coastal turquoise, reef deep-blue, abyss faint teal.
            bd.glowBColors = new[] { WithAlpha(bdThemes[0].cardBase, 0.16f), WithAlpha(bdThemes[1].cardBase, 0.16f), WithAlpha(Hex("1E7AA8"), 0.16f) };
            bd.rayColors   = new[] { WithAlpha(Hex("BFF4FF"), 0.07f), WithAlpha(Hex("9FE0FF"), 0.05f), WithAlpha(Hex("6FD8E0"), 0.03f) };
            bd.vignetteAlpha = 0.45f;
            // editor-preview values (overwritten at runtime to the level's tier)
            cam.backgroundColor = bd.baseColors[0];
            bd.glow1.color = bd.glowAColors[0];
            bd.glow2.color = bd.glowBColors[0];
            bd.vignette.color = new Color(0f, 0f, 0f, bd.vignetteAlpha);
            bd.glow1.transform.localScale = Vector3.one * 20f;
            bd.glow2.transform.localScale = Vector3.one * 17f;
            bd.vignette.transform.localScale = new Vector3(26f, 15f, 1f);
            bd.glow1.transform.localPosition = new Vector3(-4f, 2.6f, 0f);
            bd.glow2.transform.localPosition = new Vector3(4.5f, -3f, 0f);
            for (int i = 0; i < bd.rays.Length; i++)
            {
                bd.rays[i].color = bd.rayColors[0];
                bd.rays[i].transform.localScale = new Vector3(9f, 34f, 1f);
                bd.rays[i].transform.localPosition = new Vector3((i - 1) * 7f, 4f, 0.05f);
                bd.rays[i].transform.localEulerAngles = new Vector3(0f, 0f, (i - 1) * 10f + 6f);
            }

            // Optional drop-in underwater background photo (Assets/Parabox/Art/GameBG.png) behind the board.
            var gamePhoto = LoadGameBgImage();
            if (gamePhoto != null)
            {
                bd.bgPhoto = MakeWorldSprite(backdrop.transform, "GamePhoto", gamePhoto, -206);
                bd.bgPhoto.transform.localScale = new Vector3(30f, 18f, 1f);   // editor preview; view-fit at runtime
                // subtle dark wash so the puzzle stays readable over the artwork
                var photoWash = MakeWorldSprite(backdrop.transform, "PhotoWash", spr.fill, -205);
                photoWash.color = new Color(0.02f, 0.05f, 0.09f, 0.30f);
                photoWash.transform.localScale = new Vector3(200f, 200f, 1f);
            }

            // Ambient particles per ocean region (calm coastal bubbles / reef bubble field / abyss bioluminescence).
            var ambientGO = new GameObject("Ambient");
            ambientGO.transform.SetParent(cam.transform, false);
            ambientGO.transform.localPosition = new Vector3(0f, 0f, 15f);
            var amb = ambientGO.AddComponent<AmbientParticles>();
            amb.cam = cam;
            amb.tiers = new[]
            {
                new AmbientParticles.Config {   // Coastal Waters — a few calm, bright bubbles rising
                    sprite = spr.ringCircle,
                    palette = new[] { Hex("CFFAFF"), Hex("A4EEF4"), Hex("7FE6DE") },
                    count = 18, sizeRange = new Vector2(0.10f, 0.32f),
                    driftY = 0.80f, swayAmp = 0.28f, swaySpeed = 1.2f, spin = 0f, baseAlpha = 0.32f,
                },
                new AmbientParticles.Config {   // Coral Reef & Deep Ocean — light bubble drift
                    sprite = spr.ringCircle,
                    palette = new[] { Hex("8FE0F0"), Hex("B4F0FF"), Hex("6FC8E0") },
                    count = 22, sizeRange = new Vector2(0.10f, 0.36f),
                    driftY = 0.90f, swayAmp = 0.34f, swaySpeed = 1.35f, spin = 0f, baseAlpha = 0.34f,
                },
                new AmbientParticles.Config {   // Ocean Abyss — sparse bioluminescent motes flickering in the dark
                    sprite = spr.disc,
                    palette = new[] { Hex("46E8D0"), Hex("7FD8FF"), Hex("B4F0FF") },
                    count = 20, sizeRange = new Vector2(0.05f, 0.20f),
                    driftY = 0.40f, swayAmp = 0.18f, swaySpeed = 0.9f, spin = 0f, baseAlpha = 0.60f, flicker = true,
                },
            };

            CreateEventSystem();

            var canvas = CreateCanvas("HUDCanvas");

            // clean centered header — title + moves/best, balanced with the board, off the edges.
            // both get a dark outline so they stay legible on any board colour or background photo.
            var levelLabel = MakeText(canvas, "LevelLabel", "Level 1", 34, Color.white,
                new Vector2(0, -56), new Vector2(1000, 46), FontStyle.Bold, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1));
            var levelOutline = levelLabel.gameObject.AddComponent<Outline>();
            levelOutline.effectColor = new Color(0f, 0.05f, 0.09f, 0.85f);
            levelOutline.effectDistance = new Vector2(2f, -2f);

            var movesLabel = MakeText(canvas, "MovesLabel", "Moves: 0", 24, Color.white,
                new Vector2(0, -100), new Vector2(700, 32), FontStyle.Bold, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1));
            var movesOutline = movesLabel.gameObject.AddComponent<Outline>();
            movesOutline.effectColor = new Color(0f, 0.05f, 0.09f, 0.8f);
            movesOutline.effectDistance = new Vector2(1.5f, -1.5f);

            // premium circular countdown timer — SAME design, moved to the top-RIGHT corner so it
            // no longer overlaps the centered game board. Colour follows the level's tier.
            var timerRoot = MakeRect(canvas, "Timer", new Vector2(-104, -104), new Vector2(120, 120),
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f));
            var timerCanvas = timerRoot.gameObject.AddComponent<CanvasGroup>();
            UIImage(timerRoot, "Shadow", spr.glow, new Color(0f, 0f, 0f, 0.40f), new Vector2(0f, -6f), new Vector2(150, 150));
            UIImage(timerRoot, "Backing", spr.disc, Hex("141A28"), Vector2.zero, new Vector2(112, 112));
            UIImage(timerRoot, "Track", spr.ringCircle, Hex("2A3350"), Vector2.zero, new Vector2(120, 120));
            var fillRT = MakeRect(timerRoot, "Fill", Vector2.zero, new Vector2(120, 120));
            var timerFill = fillRT.gameObject.AddComponent<Image>();
            timerFill.sprite = spr.ringCircle; timerFill.color = Accent; timerFill.raycastTarget = false;
            timerFill.type = Image.Type.Filled;
            timerFill.fillMethod = Image.FillMethod.Radial360;
            timerFill.fillOrigin = (int)Image.Origin360.Top;
            timerFill.fillClockwise = true;
            timerFill.fillAmount = 1f;
            var timerLabel = MakeText(timerRoot, "TimerText", "16", 42, Color.white,
                Vector2.zero, new Vector2(120, 120), FontStyle.Bold);

            // ---- clean control bar, grouped and centered below the board ----
            var barAccent = Hex("46CEE0");
            var bar = MakeRect(canvas, "ControlBar", new Vector2(0, 92), new Vector2(1400, 150),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f));

            // directional pad (left) — these MOVE the player
            var dsz = new Vector2(54, 54);
            var upBtn    = MakeFlatButton(bar, "BtnUp",    "▲", 24, new Vector2(-380, 34),  dsz, barAccent, spr);
            var downBtn  = MakeFlatButton(bar, "BtnDown",  "▼", 24, new Vector2(-380, -30), dsz, barAccent, spr);
            var leftBtn  = MakeFlatButton(bar, "BtnLeft",  "◀", 24, new Vector2(-436, 2),   dsz, barAccent, spr);
            var rightBtn = MakeFlatButton(bar, "BtnRight", "▶", 24, new Vector2(-324, 2),   dsz, barAccent, spr);
            MakeText(bar, "MoveLbl", "MOVE", 15, Hex("9FC0D2"), new Vector2(-380, -60), new Vector2(140, 22), FontStyle.Bold);

            // action buttons (right) — uniform labelled pills, wired to the real functions
            var asz = new Vector2(150, 56);
            var undoBtn    = MakeFlatButton(bar, "BtnUndo",    "UNDO",    22, new Vector2(-150, 2), asz, barAccent, spr);
            var restartBtn = MakeFlatButton(bar, "BtnRestart", "RESTART", 22, new Vector2(20, 2),   asz, barAccent, spr);
            var muteBtn    = MakeFlatButton(bar, "BtnMute",    null,      22, new Vector2(190, 2),  asz, barAccent, spr);
            var muteLbl    = MakeText(muteBtn.transform, "MuteLbl",  "MUTE",  22, Color.white,   Vector2.zero, asz, FontStyle.Bold);
            var mutedLbl   = MakeText(muteBtn.transform, "MutedLbl", "MUTED", 22, Hex("F2A6A6"), Vector2.zero, asz, FontStyle.Bold);
            mutedLbl.gameObject.SetActive(false);
            var menuHudBtn = MakeFlatButton(bar, "BtnMenu",    "MENU",    22, new Vector2(360, 2),  asz, barAccent, spr);

            // win panel
            var panelGO = new GameObject("WinPanel", typeof(RectTransform));
            panelGO.transform.SetParent(canvas, false);
            var panelRT = (RectTransform)panelGO.transform;
            panelRT.anchorMin = Vector2.zero; panelRT.anchorMax = Vector2.one; panelRT.sizeDelta = Vector2.zero;
            panelGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            var windowRT = MakeRect(panelGO.transform, "Window", Vector2.zero, new Vector2(560, 340));
            var windowImg = windowRT.gameObject.AddComponent<Image>();
            windowImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            windowImg.type = Image.Type.Sliced;
            windowImg.color = Hex("1C2130");
            windowRT.gameObject.AddComponent<UIPopIn>(); // pops in when the win panel shows

            var winTitle = MakeText(windowRT, "WinTitle", "Level Complete!", 42, Accent,
                new Vector2(0, 104), new Vector2(520, 56), FontStyle.Bold);
            var winStats = MakeText(windowRT, "WinStats", "Solved in 0 moves", 22, Color.white,
                new Vector2(0, 52), new Vector2(520, 56));
            MakeText(windowRT, "WinHint", "press Space to continue", 18, DimText,
                new Vector2(0, 14), new Vector2(520, 28));
            var nextBtn = MakeButton(windowRT, "NextButton", "Next Level", new Vector2(-115, -62), new Vector2(210, 64), 26, RoomColors[3]);
            var menuBtn = MakeButton(windowRT, "MenuButton", "Menu", new Vector2(115, -62), new Vector2(210, 64), 26, ButtonCol);

            panelGO.SetActive(false);

            // time-up panel (shown when the countdown hits zero)
            var tuGO = new GameObject("TimeUpPanel", typeof(RectTransform));
            tuGO.transform.SetParent(canvas, false);
            var tuRT = (RectTransform)tuGO.transform;
            tuRT.anchorMin = Vector2.zero; tuRT.anchorMax = Vector2.one; tuRT.sizeDelta = Vector2.zero;
            tuGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

            var tuWin = MakeRect(tuGO.transform, "Window", Vector2.zero, new Vector2(560, 340));
            var tuWinImg = tuWin.gameObject.AddComponent<Image>();
            tuWinImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            tuWinImg.type = Image.Type.Sliced; tuWinImg.color = Hex("1C2130");
            tuWin.gameObject.AddComponent<UIPopIn>();

            MakeText(tuWin, "TUTitle", "TIME'S UP", 46, Hex("E5484D"),
                new Vector2(0, 100), new Vector2(520, 60), FontStyle.Bold);
            MakeText(tuWin, "TUSub", "You ran out of time on this level.", 22, DimText,
                new Vector2(0, 44), new Vector2(520, 40));
            var retryBtn = MakeButton(tuWin, "RetryButton", "Retry Level", new Vector2(-115, -66), new Vector2(214, 66), 24, RoomColors[3]);
            var toLevelsBtn = MakeButton(tuWin, "ToLevelsButton", "Level Select", new Vector2(115, -66), new Vector2(214, 66), 24, ButtonCol);
            tuGO.SetActive(false);

            var gm = new GameObject("GameManager").AddComponent<GameManager>();
            gm.levelPrefabs = levels;
            gm.floorPrefab = t.floor;
            gm.borderPrefab = t.border;
            gm.wallPrefab = t.wall;
            gm.boxPrefab = t.box;
            gm.metaBoxPrefab = t.metaBox;
            gm.playerPrefab = t.player;
            gm.boxGoalPrefab = t.boxGoal;
            gm.playerGoalPrefab = t.playerGoal;
            gm.gridPrefab = t.grid;
            gm.roomColors = RoomColors;
            gm.boxColor = BoxColor;
            gm.playerColor = PlayerColor;
            gm.pieceSprite = spr.fill;
            gm.ringSprite = spr.ringThick;
            gm.glowSprite = spr.glow;
            gm.vignetteSprite = spr.vignette;
            gm.cameraFollow = follow;
            gm.levelLabel = levelLabel;
            gm.movesLabel = movesLabel;
            gm.winPanel = panelGO;
            gm.winTitle = winTitle;
            gm.winStats = winStats;
            gm.nextButton = nextBtn;
            gm.menuButton = menuBtn;
            gm.timerRoot = timerRoot;
            gm.timerCanvas = timerCanvas;
            gm.timerFill = timerFill;
            gm.timerLabel = timerLabel;
            gm.timerAccents = new[] { bdThemes[0].accent, bdThemes[1].accent, bdThemes[2].accent };
            // Per-tier level skins (Beginner cool-bright / Intermediate rich-violet / Advanced gold-neon).
            gm.levelThemes = new[]
            {
                new LevelTheme {   // COASTAL WATERS — clean solid board + subtle dark grid
                    roomColors = new[] { Hex("38C6CE"), Hex("50D6CE"), Hex("42CCD6"), Hex("62DCD2"), Hex("4CCEDC") },
                    wall = Hex("0F3844"), box = Hex("F2884E"), player = Hex("FFE066"),
                    grid = new Color(0f, 0f, 0f, 0.10f), floorVignette = 0.08f, pieceGlow = 0.12f,
                    floorTex = null, floorTexTint = new Color(0f, 0f, 0f, 0f),
                },
                new LevelTheme {   // CORAL REEF & DEEP OCEAN — clean solid board + subtle grid
                    roomColors = new[] { Hex("2E74BE"), Hex("3A86C8"), Hex("2E6CB0"), Hex("3E92C6"), Hex("347CBE") },
                    wall = Hex("0B1E34"), box = Hex("F2884E"), player = Hex("FFE066"),
                    grid = new Color(0f, 0f, 0f, 0.12f), floorVignette = 0.10f, pieceGlow = 0.14f,
                    floorTex = null, floorTexTint = new Color(0f, 0f, 0f, 0f),
                },
                new LevelTheme {   // OCEAN ABYSS — clean solid board + subtle light grid
                    roomColors = new[] { Hex("123452"), Hex("184060"), Hex("0E2C48"), Hex("1C4A6C"), Hex("143C5C") },
                    wall = Hex("050F1C"), box = Hex("F2A64E"), player = Hex("46E8D0"),
                    grid = new Color(1f, 1f, 1f, 0.10f), floorVignette = 0.14f, pieceGlow = 0.18f,
                    floorTex = null, floorTexTint = new Color(0f, 0f, 0f, 0f),
                },
            };
            gm.timeUpPanel = tuGO;
            gm.retryButton = retryBtn;
            gm.backToLevelsButton = toLevelsBtn;

            // on-screen control bar
            gm.upButton = upBtn; gm.downButton = downBtn; gm.leftButton = leftBtn; gm.rightButton = rightBtn;
            gm.undoButton = undoBtn; gm.restartButton = restartBtn; gm.muteButton = muteBtn; gm.hudMenuButton = menuHudBtn;
            gm.muteOnIcon = muteLbl.gameObject; gm.muteOffIcon = mutedLbl.gameObject;

            CreateFade(canvas);

            string path = SceneDir + "/Game.unity";
            EditorSceneManager.SaveScene(scene, path);
            return path;
        }

        static string CreateMainMenuScene(Sprites spr, int levelCount)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera(false);
            CreateEventSystem();

            var canvas = CreateCanvas("MenuCanvas");
            var ui = canvas.gameObject.AddComponent<MainMenuUI>();

            BuildMenuBackground(canvas, spr);

            // ============================================ HOME screen
            UIImage(canvas, "MascotGlow", spr.glow, new Color(1f, 1f, 1f, 0.14f), new Vector2(0, 360), new Vector2(380, 380));
            var mascot = MakeRect(canvas, "Mascot", new Vector2(0, 360), new Vector2(400, 120));
            mascot.gameObject.AddComponent<Bobber>();
            MascotPiece(mascot, "MascotBox", new Vector2(70, 0), 78f, BoxColor, spr);   // the box
            MascotPiece(mascot, "MascotBody", new Vector2(-40, 0), 90f, PlayerColor, spr); // the player
            MascotEye(mascot, "MascotEyeL", new Vector2(-53, 8), spr);
            MascotEye(mascot, "MascotEyeR", new Vector2(-27, 8), spr);

            // title logo — glowing, breathing, with a bright-aqua->ocean-blue gradient fill
            var titleGlow = UIImage(canvas, "TitleGlow", spr.glow, WithAlpha(Hex("38C0D8"), 0.42f), new Vector2(0, 240), new Vector2(1300, 560));
            var tgPulse = titleGlow.gameObject.AddComponent<UIPulse>();
            tgPulse.amplitude = 0.06f; tgPulse.speed = 1.4f;
            var title = MakeText(canvas, "Title", "PARABOX", 108, Color.white,
                new Vector2(0, 240), new Vector2(1300, 150), FontStyle.Bold);
            var titleGrad = title.gameObject.AddComponent<UIGradient>();   // added before Shadow so the fill is the gradient
            titleGrad.top = Hex("5FE6EA"); titleGrad.bottom = Hex("2E86C4");
            var titleShadow = title.gameObject.AddComponent<Shadow>();
            titleShadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            titleShadow.effectDistance = new Vector2(0f, -7f);

            var subtitle = MakeText(canvas, "Subtitle", "a recursive puzzle game — by Hayot", 26, Hex("AEB6D8"),
                new Vector2(0, 150), new Vector2(1200, 40), FontStyle.Italic);
            var subShadow = subtitle.gameObject.AddComponent<Shadow>();
            subShadow.effectColor = new Color(0f, 0f, 0f, 0.4f);
            subShadow.effectDistance = new Vector2(0f, -2f);

            MenuButtonGlow(canvas, new Vector2(0, 34), new Vector2(360, 84), Hex("46D8D0"), spr);
            ui.playButton   = MakeButton(canvas, "PlayButton",   "PLAY",   new Vector2(0, 34),   new Vector2(360, 84), 34, Hex("4CDAD0"), Hex("2E9EA0"));
            MenuButtonGlow(canvas, new Vector2(0, -70), new Vector2(360, 76), Hex("38B4E4"), spr);
            ui.levelsButton = MakeButton(canvas, "LevelsButton", "LEVELS", new Vector2(0, -70),  new Vector2(360, 76), 30, Hex("46B6E8"), Hex("2A80C0"));
            MenuButtonGlow(canvas, new Vector2(0, -172), new Vector2(240, 60), Hex("2E7088"), spr);
            ui.quitButton   = MakeButton(canvas, "QuitButton",   "QUIT",   new Vector2(0, -172), new Vector2(240, 60), 24, ButtonCol);

            // premium button icons
            AddPlayIcon(ui.playButton, spr);
            AddGridIcon(ui.levelsButton, spr);
            AddPowerIcon(ui.quitButton, spr);

            MakeText(canvas, "Hint", "WASD / Arrows — move    Z — undo    R — restart    M — mute    Esc — menu",
                20, DimText, new Vector2(0, 22), new Vector2(1400, 30), FontStyle.Normal, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0));

            var themes = CategoryThemes();
            int cats = themes.Length;

            ui.categoryButtons  = new Button[cats];
            ui.categoryProgress = new Text[cats];
            ui.categoryLocks    = new GameObject[cats];
            ui.gridScreens      = new UIScreen[cats];
            ui.gridBackButtons  = new Button[cats];
            ui.levelButtons     = new Button[levelCount];
            ui.levelChecks      = new GameObject[levelCount];
            ui.levelLocks       = new GameObject[levelCount];
            ui.levelHighlights  = new GameObject[levelCount];

            // ============================================ CATEGORY SELECT screen
            UIScreen catScreen;
            var catPanel = MakeOverlay(canvas, "CategorySelectPanel", Hex("0B0912"), out catScreen);
            ui.categorySelectScreen = catScreen;
            BuildAtmosphere(catPanel, spr, Accent, 0.16f, Hex("7E57D8"), 0.16f, 0.42f);

            MakeText(catPanel, "CSTitle", "SELECT  DIFFICULTY", 56, Accent,
                new Vector2(0, 428), new Vector2(1300, 74), FontStyle.Bold);
            MakeText(catPanel, "CSHint", "Three tiers — pick where you belong", 24, DimText,
                new Vector2(0, 366), new Vector2(1100, 32));

            float catGap = 500f;
            float catStartX = -(cats - 1) * catGap * 0.5f;
            for (int c = 0; c < cats; c++)
            {
                var gpos = new Vector2(catStartX + c * catGap, -18f);
                // soft glow halo behind the panel (a "glowing room" look; stronger per tier)
                UIImage(catPanel, "CatGlow" + c, spr.glow, WithAlpha(themes[c].accent, 0.28f + 0.07f * c),
                    gpos, new Vector2(680, 780));
                ui.categoryButtons[c] = MakeCategoryPanel(catPanel, c, themes[c], spr,
                    gpos, new Vector2(440, 540),
                    out ui.categoryProgress[c], out ui.categoryLocks[c]);
            }

            ui.categoryBackButton = MakeButton(catPanel, "CatBack", "Back", new Vector2(0, -452), new Vector2(240, 64), 26, ButtonCol);
            catPanel.gameObject.SetActive(false);

            // ============================================ LEVEL GRID screens (one per category)
            int per = MainMenuUI.PerCategory;
            float[] glowA = { 0.22f, 0.24f, 0.20f };
            float[] glowB = { 0.16f, 0.15f, 0.13f };
            float[] vign = { 0.34f, 0.44f, 0.58f };
            for (int c = 0; c < cats; c++)
            {
                var theme = themes[c];
                UIScreen gScreen;
                var gPanel = MakeOverlay(canvas, "GridPanel" + c, theme.bg, out gScreen);
                ui.gridScreens[c] = gScreen;

                // deep-space atmosphere: two nebula glows + a vignette, escalating per tier
                BuildAtmosphere(gPanel, spr, theme.accent, glowA[c], theme.cardBase, glowB[c], vign[c]);

                MakeText(gPanel, "GTitle", theme.name, 52, theme.accent,
                    new Vector2(0, 430), new Vector2(1100, 66), FontStyle.Bold);
                MakeText(gPanel, "GDesc", theme.desc, 24, Lighten(theme.accent, 0.25f),
                    new Vector2(0, 376), new Vector2(1200, 32));

                const int cols = 5;
                const float gapX = 232f, gapY = 210f, firstY = 150f;
                var cardSize = new Vector2(196, 176);
                for (int k = 0; k < per; k++)
                {
                    int i = c * per + k;
                    if (i >= levelCount) break;
                    int row = k / cols;
                    int col = k % cols;
                    float x = -(cols - 1) * gapX * 0.5f + col * gapX;
                    float y = firstY - row * gapY;
                    ui.levelButtons[i] = MakeLevelCard(gPanel, "Level" + (i + 1), i + 1, c, theme, spr,
                        new Vector2(x, y), cardSize,
                        out ui.levelLocks[i], out ui.levelChecks[i], out ui.levelHighlights[i]);
                }

                ui.gridBackButtons[c] = MakeButton(gPanel, "GBack" + c, "Back", new Vector2(0, -452), new Vector2(240, 64), 26, ButtonCol);
                gPanel.gameObject.SetActive(false);
            }

            CreateFade(canvas);

            string path = SceneDir + "/MainMenu.unity";
            EditorSceneManager.SaveScene(scene, path);
            return path;
        }

        // ---------- level-select theming ----------------------------------------
        struct CatTheme
        {
            public string name, desc;
            public Color bg, accent, cardBase;
        }

        static CatTheme[] CategoryThemes() => new[]
        {
            new CatTheme { name = "COASTAL WATERS", desc = "Levels 1–10   ·   Beginner",
                           bg = Hex("0F4658"), accent = Hex("58E4E0"), cardBase = Hex("35BEC8") },
            new CatTheme { name = "CORAL REEF",     desc = "Levels 11–20   ·   Intermediate",
                           bg = Hex("082A46"), accent = Hex("3CC8EA"), cardBase = Hex("2E7EC4") },
            new CatTheme { name = "OCEAN ABYSS",    desc = "Levels 21–30   ·   Advanced",
                           bg = Hex("030E1C"), accent = Hex("34ECC6"), cardBase = Hex("0E2A46") },
        };

        static string CategoryBlurb(int c)
        {
            if (c == 0) return "Crystal-clear shallows —\ncalm, sunlit, welcoming.";
            if (c == 1) return "Reefs & the deeper blue —\ncurrents grow complex.";
            return "The lightless abyss —\nbioluminescence & mastery.";
        }

        // A full-screen overlay panel with a CanvasGroup + UIScreen (fade/scale transition).
        static Transform MakeOverlay(Transform canvas, string name, Color bg, out UIScreen screen)
        {
            var rt = MakeRect(canvas, name, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            rt.gameObject.AddComponent<CanvasGroup>();
            screen = rt.gameObject.AddComponent<UIScreen>();
            var img = rt.gameObject.AddComponent<Image>();
            img.color = bg;
            return rt;
        }

        // Deep-space atmosphere on a panel: two soft nebula glows + a dark vignette for depth.
        static void BuildAtmosphere(Transform panel, Sprites spr, Color a, float aa, Color b, float ab, float vig)
        {
            UIImage(panel, "Glow1", spr.glow, WithAlpha(a, aa), new Vector2(-540, 250), new Vector2(1500, 1500));
            UIImage(panel, "Glow2", spr.glow, WithAlpha(b, ab), new Vector2(560, -270), new Vector2(1400, 1400));
            var v = MakeRect(panel, "Vignette", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var vi = v.gameObject.AddComponent<Image>();
            vi.sprite = spr.vignette; vi.type = Image.Type.Simple;
            vi.color = new Color(0f, 0f, 0f, vig); vi.raycastTarget = false;
        }

        // A big clickable difficulty panel: glowing outline, escalating frame, unique icon,
        // name, blurb and progress. Reads like a premium "room".
        static Button MakeCategoryPanel(Transform parent, int c, CatTheme t, Sprites spr,
                                        Vector2 pos, Vector2 size, out Text progress, out GameObject lockGO)
        {
            var rt = MakeRect(parent, "Cat" + c, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = spr.fill; img.type = Image.Type.Sliced; img.color = t.cardBase;

            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.93f, 0.93f, 0.93f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            rt.gameObject.AddComponent<UIHoverScale>();

            // bright glowing outline on every panel; escalating inner detail
            AddRing(rt, "Outline", size, spr.ringThin, WithAlpha(t.accent, c == 2 ? 1f : 0.65f));
            if (c >= 1) AddRing(rt, "Inner", size - new Vector2(20, 20), spr.ringThin, WithAlpha(t.accent, 0.4f));

            // no emblem — clean typographic cards. bright Coastal reads with dark ink; deeper tiers light.
            Color inkStrong = c == 0 ? Hex("0A3A46") : t.accent;
            Color inkSoft   = c == 0 ? Darken(t.cardBase, 0.50f) : Lighten(t.cardBase, 0.6f);
            MakeText(rt, "Name", t.name, 46, inkStrong,
                new Vector2(0, size.y * 0.5f - 172f), new Vector2(size.x - 24, 56), FontStyle.Bold);

            // difficulty badge — BEGINNER / INTERMEDIATE / ADVANCED
            string[] diffLabel = { "BEGINNER", "INTERMEDIATE", "ADVANCED" };
            var badge = MakeRect(rt, "DiffBadge", new Vector2(0, size.y * 0.5f - 230f), new Vector2(200, 34));
            var badgeImg = badge.gameObject.AddComponent<Image>();
            badgeImg.sprite = spr.fill; badgeImg.type = Image.Type.Sliced;
            badgeImg.color = c == 0 ? WithAlpha(Hex("0A3A46"), 0.18f) : WithAlpha(t.accent, 0.22f);
            badgeImg.raycastTarget = false;
            MakeText(badge, "DiffText", diffLabel[Mathf.Clamp(c, 0, 2)], 17, c == 0 ? Hex("0A3A46") : Color.white,
                Vector2.zero, new Vector2(200, 34), FontStyle.Bold);

            MakeText(rt, "Blurb", CategoryBlurb(c), 21, inkSoft,
                new Vector2(0, size.y * 0.5f - 300f), new Vector2(size.x - 40, 74));

            var pill = MakeRect(rt, "Pill", new Vector2(0, -size.y * 0.5f + 66f), new Vector2(210, 54));
            var pillImg = pill.gameObject.AddComponent<Image>();
            pillImg.sprite = spr.fill; pillImg.type = Image.Type.Sliced;
            pillImg.color = Darken(t.cardBase, 0.42f); pillImg.raycastTarget = false;
            progress = MakeText(pill, "Prog", "0 / 10", 26, Color.white, Vector2.zero, new Vector2(210, 54), FontStyle.Bold);

            var lk = MakeRect(rt, "CatLock", new Vector2(size.x * 0.5f - 34f, size.y * 0.5f - 34f), new Vector2(50, 50));
            UIImage(lk, "LockIcon", spr.lockIcon, Hex("E7B84B"), Vector2.zero, new Vector2(48, 48));
            lockGO = lk.gameObject;
            lockGO.SetActive(false);

            return btn;
        }

        // Unique icon per ocean region: wave (Coastal Waters), coral (Coral Reef), jellyfish (Ocean Abyss).
        static void BuildCategoryIcon(Transform parent, int c, CatTheme t, Sprites spr, Vector2 pos, Vector2 size, Color color)
        {
            // if a real world-icon image is present (Art/icon_coastal|reef|abyss.png), use the artwork
            var custom = LoadWorldIcon(c);
            if (custom != null)
            {
                var im = UIImage(parent, "Icon", custom, WithAlpha(Color.white, color.a), pos, size * 1.2f);
                im.preserveAspect = true;
                return;
            }

            if (c == 0)        // Coastal Waters: a gentle ocean wave
                UIImage(parent, "Icon", spr.iconWaveR, WithAlpha(Color.white, color.a), pos, size * 1.1f);
            else if (c == 1)   // Coral Reef & Deep Ocean: branching coral
                UIImage(parent, "Icon", spr.iconCoral, WithAlpha(Color.white, color.a), pos, size * 1.15f);
            else               // Ocean Abyss: a bioluminescent jellyfish (teal halo behind)
            {
                UIImage(parent, "JellyGlow", spr.glow, WithAlpha(t.accent, color.a * 0.55f), pos, size * 1.5f);
                UIImage(parent, "Icon", spr.iconJelly, WithAlpha(Color.white, color.a), pos, size * 1.2f);
            }
        }

        // A premium level card: glowing box outline (Parabox look) that grows richer with the tier.
        static Button MakeLevelCard(Transform parent, string name, int number, int tier, CatTheme theme, Sprites spr,
                                    Vector2 pos, Vector2 size,
                                    out GameObject lockGO, out GameObject checkGO, out GameObject highlightGO)
        {
            var rt = MakeRect(parent, name, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = spr.fill; img.type = Image.Type.Sliced; img.color = theme.cardBase;

            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
            colors.pressedColor = new Color(0.80f, 0.80f, 0.80f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.32f); // locked cards read as dimmed
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            rt.gameObject.AddComponent<UIHoverScale>();

            // bright box outline on every card; inner detail line from Intermediate up
            AddRing(rt, "Outline", size, spr.ringThin, WithAlpha(theme.accent, tier == 2 ? 1f : 0.7f));
            if (tier >= 1) AddRing(rt, "Inner", size - new Vector2(20, 20), spr.ringThin, WithAlpha(theme.accent, 0.4f));

            MakeText(rt, "Number", number.ToString(), 52,
                tier == 0 ? Hex("0A3A46") : (tier == 2 ? theme.accent : Color.white),
                Vector2.zero, size, FontStyle.Bold);

            var hi = AddRing(rt, "Highlight", size + new Vector2(24, 24), spr.ringThin, theme.accent);
            hi.gameObject.AddComponent<UIPulse>();
            highlightGO = hi.gameObject;
            highlightGO.SetActive(false);

            var chk = MakeRect(rt, "Check", new Vector2(size.x * 0.5f - 20f, size.y * 0.5f - 20f), new Vector2(52, 52));
            var chkImg = chk.gameObject.AddComponent<Image>();
            chkImg.sprite = spr.fill; chkImg.type = Image.Type.Sliced; chkImg.color = Hex("34C759");
            chkImg.raycastTarget = false;
            MakeText(chk, "Tick", "✓", 30, Color.white, Vector2.zero, new Vector2(52, 52), FontStyle.Bold);
            checkGO = chk.gameObject;
            checkGO.SetActive(false);

            var lk = MakeRect(rt, "Lock", Vector2.zero, size);
            var lkBg = lk.gameObject.AddComponent<Image>();
            lkBg.sprite = spr.fill; lkBg.type = Image.Type.Sliced;
            lkBg.color = new Color(0.04f, 0.05f, 0.09f, 0.80f); lkBg.raycastTarget = false;
            UIImage(lk, "LockIcon", spr.lockIcon, Hex("E7B84B"), Vector2.zero, new Vector2(74, 74));
            lockGO = lk.gameObject;
            lockGO.SetActive(false);

            return btn;
        }

        static RectTransform AddRing(Transform parent, string name, Vector2 size, Sprite sprite, Color color)
        {
            var rt = MakeRect(parent, name, Vector2.zero, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite; img.type = Image.Type.Sliced; img.color = color; img.raycastTarget = false;
            return rt;
        }

        static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        // ---------- premium animated main-menu background ----------
        static void BuildMenuBackground(Transform canvas, Sprites spr)
        {
            var baseRT = MakeRect(canvas, "MenuBG", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var baseImg = baseRT.gameObject.AddComponent<Image>();
            baseImg.color = Hex("05121F"); baseImg.raycastTarget = false;   // deep ocean navy

            // optional custom art: drop a dark PNG at Assets/Parabox/Art/MenuBG.png. If present it
            // becomes the base layer (with a dark wash for readability); the animated glow/motes sit on top.
            var photo = LoadMenuBgImage();
            if (photo != null)
            {
                var picRT = MakeRect(canvas, "MenuPhoto", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
                var pic = picRT.gameObject.AddComponent<Image>();
                pic.sprite = photo; pic.color = Color.white; pic.preserveAspect = false; pic.raycastTarget = false;

                var dimRT = MakeRect(canvas, "MenuPhotoDim", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
                var dim = dimRT.gameObject.AddComponent<Image>();
                dim.color = new Color(0.03f, 0.04f, 0.08f, 0.45f); dim.raycastTarget = false;
            }

            // three slowly drifting + pulsing nebula glows (subtler when a photo is present)
            float blobA = photo != null ? 0.30f : 0.6f;
            MenuBlob(canvas, spr, Hex("40D8D0"), new Vector2(-580, 280), 1180f, 0f, blobA);   // coastal turquoise
            MenuBlob(canvas, spr, Hex("2E92D8"), new Vector2(600, -40), 1080f, 2.1f, blobA);   // reef blue
            MenuBlob(canvas, spr, Hex("1E7AA8"), new Vector2(120, -380), 1000f, 4.2f, blobA);   // abyss deep-teal

            // underwater god-ray shafts slanting down from the surface
            {
                float rayA = photo != null ? 0.07f : 0.11f;
                float[] rxp = { -600f, 40f, 560f };
                float[] rzp = { 11f, -7f, 15f };
                for (int i = 0; i < 3; i++)
                {
                    var ray = UIImage(canvas, "MenuRay" + i, spr.godRay, WithAlpha(Hex("C4F4FF"), rayA),
                        new Vector2(rxp[i], 260f), new Vector2(360f, 1500f));
                    ray.rectTransform.localEulerAngles = new Vector3(0f, 0f, rzp[i]);
                    ray.raycastTarget = false;
                }
            }

            // floating light motes (like drifting plankton / bubbles)
            var ambRT = MakeRect(canvas, "MenuParticles", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var amb = ambRT.gameObject.AddComponent<UIAmbient>();
            amb.sprite = spr.glow;
            amb.palette = new[] { Hex("7FE6EA"), Hex("58C0E8"), Hex("A9F0F0"), Hex("FFFFFF") };
            amb.count = 44; amb.baseAlpha = 0.34f;

            // depth vignette on top of the nebula (content sits above this)
            var vigRT = MakeRect(canvas, "MenuVignette", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var vig = vigRT.gameObject.AddComponent<Image>();
            vig.sprite = spr.vignette; vig.color = new Color(0f, 0f, 0f, 0.55f); vig.raycastTarget = false;
        }

        static void MenuBlob(Transform canvas, Sprites spr, Color color, Vector2 pos, float size, float phase, float alpha)
        {
            var img = UIImage(canvas, "Blob", spr.glow, WithAlpha(color, alpha), pos, new Vector2(size, size));
            var bob = img.gameObject.AddComponent<Bobber>();
            bob.amplitude = 42f; bob.speed = 0.4f; bob.phase = phase;
            var pul = img.gameObject.AddComponent<UIPulse>();
            pul.amplitude = 0.07f; pul.speed = 0.45f;
        }

        // Loads a user-supplied menu background from Assets/Parabox/Art/MenuBG.(png|jpg),
        // forcing it to import as a Sprite. Returns null if none is present (procedural fallback).
        static Sprite LoadMenuBgImage()
        {
            string[] candidates = { Root + "/Art/MenuBG.png", Root + "/Art/MenuBG.jpg", Root + "/Art/MenuBG.jpeg" };
            foreach (var path in candidates)
            {
                string abs = Path.Combine(Application.dataPath, path.Substring("Assets/".Length));
                if (!File.Exists(abs)) continue;

                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp != null && (imp.textureType != TextureImporterType.Sprite ||
                                    imp.spriteImportMode != SpriteImportMode.Single))
                {
                    imp.textureType = TextureImporterType.Sprite;
                    imp.spriteImportMode = SpriteImportMode.Single;
                    imp.SaveAndReimport();
                }
                var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sp != null) return sp;
            }
            return null;
        }

        // Optional in-game underwater background: Assets/Parabox/Art/GameBG.(png|jpg). Null => procedural scene.
        static Sprite LoadGameBgImage()
        {
            string[] candidates = { Root + "/Art/GameBG.png", Root + "/Art/GameBG.jpg", Root + "/Art/GameBG.jpeg" };
            foreach (var path in candidates)
            {
                string abs = Path.Combine(Application.dataPath, path.Substring("Assets/".Length));
                if (!File.Exists(abs)) continue;

                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp != null && (imp.textureType != TextureImporterType.Sprite ||
                                    imp.spriteImportMode != SpriteImportMode.Single))
                {
                    imp.textureType = TextureImporterType.Sprite;
                    imp.spriteImportMode = SpriteImportMode.Single;
                    imp.SaveAndReimport();
                }
                var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sp != null) return sp;
            }
            return null;
        }

        // Optional real region icon: Assets/Parabox/Art/icon_{coastal|reef|abyss}.(png|jpg). Null => procedural.
        static Sprite LoadWorldIcon(int c)
        {
            string[] names = { "icon_coastal", "icon_reef", "icon_abyss" };
            if (c < 0 || c >= names.Length) return null;
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
            {
                string path = Root + "/Art/" + names[c] + ext;
                string abs = Path.Combine(Application.dataPath, path.Substring("Assets/".Length));
                if (!File.Exists(abs)) continue;
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp != null && (imp.textureType != TextureImporterType.Sprite ||
                                    imp.spriteImportMode != SpriteImportMode.Single))
                {
                    imp.textureType = TextureImporterType.Sprite;
                    imp.spriteImportMode = SpriteImportMode.Single;
                    imp.SaveAndReimport();
                }
                var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sp != null) return sp;
            }
            return null;
        }

        static void MenuButtonGlow(Transform parent, Vector2 pos, Vector2 size, Color color, Sprites spr)
        {
            UIImage(parent, "BtnGlow", spr.glow, WithAlpha(color, 0.42f), pos, size + new Vector2(150f, 150f));
        }

        // The lit top face of a MakeButton (icons live here, above the gradient).
        static Transform BtnFace(Button b)
        {
            var f = b.transform.Find("Face");
            return f != null ? f : b.transform;
        }

        static void AddPlayIcon(Button b, Sprites spr)
        {
            UIImage(BtnFace(b), "Icon", spr.iconPlay, Color.white, new Vector2(-128f, 0f), new Vector2(38, 38));
        }

        static void AddGridIcon(Button b, Sprites spr)
        {
            var g = MakeRect(BtnFace(b), "Icon", new Vector2(-128f, 0f), new Vector2(42, 42));
            const float c = 15f, o = 9.5f;
            UIImage(g, "g0", spr.fill, Color.white, new Vector2(-o,  o), new Vector2(c, c));
            UIImage(g, "g1", spr.fill, Color.white, new Vector2( o,  o), new Vector2(c, c));
            UIImage(g, "g2", spr.fill, Color.white, new Vector2(-o, -o), new Vector2(c, c));
            UIImage(g, "g3", spr.fill, Color.white, new Vector2( o, -o), new Vector2(c, c));
        }

        static void AddPowerIcon(Button b, Sprites spr)
        {
            var g = MakeRect(BtnFace(b), "Icon", new Vector2(-78f, 0f), new Vector2(34, 34));
            UIImage(g, "ring", spr.ringCircle, Color.white, new Vector2(0f, -3f), new Vector2(30, 30));
            UIImage(g, "bar", spr.fill, Color.white, new Vector2(0f, 7f), new Vector2(5, 15));
        }

        // A premium mascot piece: soft drop shadow + top-lit 3D body + lighter rim outline.
        static void MascotPiece(Transform parent, string name, Vector2 pos, float size, Color color, Sprites spr)
        {
            var s = new Vector2(size, size);
            UIImage(parent, name + "Shadow", spr.glow, new Color(0f, 0f, 0f, 0.35f), pos + new Vector2(0f, -11f), s * 1.25f);
            UIImage(parent, name, spr.shaded, color, pos, s);
            UIImage(parent, name + "Rim", spr.ringThick, Lighten(color, 0.42f), pos, s);
        }

        // A mascot eye with a small catchlight highlight for a lively, premium look.
        static void MascotEye(Transform parent, string name, Vector2 pos, Sprites spr)
        {
            UIImage(parent, name, spr.fill, EyeColor, pos, new Vector2(13, 20));
            UIImage(parent, name + "Hi", spr.fill, new Color(1f, 1f, 1f, 0.85f), pos + new Vector2(3f, 5f), new Vector2(4.5f, 5.5f));
        }

        // Soft radial glow (white center -> transparent) — nebula backdrops and panel halos.
        static Sprite MakeGlowSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float c = s * 0.5f, maxr = s * 0.5f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c)) / maxr;
                    float a = Mathf.Clamp01(1f - d); a *= a;
                    px[y * s + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "Glow", false, 0, FilterMode.Bilinear);
        }

        // Radial vignette (transparent center -> black edges) for premium depth.
        static Sprite MakeVignetteSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float c = s * 0.5f, maxr = s * 0.5f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c)) / maxr;
                    float a = Mathf.Clamp01((d - 0.55f) / 0.45f); a *= a;
                    px[y * s + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "Vignette", false, 0, FilterMode.Bilinear);
        }

        // Right-pointing play triangle — the PLAY button icon.
        static Sprite MakePlaySprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float m = 0.22f * s;
            var vx = new float[] { m, m, s - m };
            var vy = new float[] { s - m, m, s * 0.5f };
            const int SS = 2;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                            if (PointInPoly(x + (sx + 0.5f) / SS, y + (sy + 0.5f) / SS, vx, vy)) hits++;
                    px[y * s + x] = new Color32(255, 255, 255, (byte)(255 * hits / (SS * SS)));
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "PlayIcon", false, 0, FilterMode.Bilinear);
        }

        // Tileable per-tier floor surface. mode: 0 grass mottle, 1 water ripples, 2 cracked rock.
        static Sprite MakeFloorTexSprite(string name, int mode)
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[s * s];
            const float TAU = 6.2831853f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = x / (float)s, v = y / (float)s;
                    float a;
                    if (mode == 0)        // grass: soft organic blotches
                    {
                        float n = Noise2(u, v) * 0.5f + 0.5f;
                        a = Mathf.Clamp01((n - 0.42f) * 2.4f);
                    }
                    else if (mode == 1)   // water: thin bright ripple crests
                    {
                        float w = Mathf.Sin(v * TAU * 7f + 0.7f * Mathf.Sin(u * TAU * 3f));
                        a = Mathf.Clamp01((w - 0.55f) * 2.6f);
                    }
                    else if (mode == 2)   // rock: thin dark cracks where the field crosses zero
                    {
                        float f = Noise2(u, v);
                        a = Mathf.Clamp01(1f - Mathf.Abs(f) * 7f);
                    }
                    else                  // stars: sparse bright points (cosmos)
                    {
                        float st = Mathf.Sin(u * TAU * 11f) * Mathf.Sin(v * TAU * 13f)
                                 + Mathf.Sin(u * TAU * 17f + 2f) * Mathf.Sin(v * TAU * 19f + 1f);
                        a = Mathf.Clamp01((st - 1.35f) * 6f);
                    }
                    px[y * s + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            tex.SetPixels32(px); tex.Apply();

            string assetPath = SpriteDir + "/" + name + ".png";
            string absPath = Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
            File.WriteAllBytes(absPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            var imp = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = Res * 0.5f;   // one tile ~ 2 world cells (less repetitive)
            imp.mipmapEnabled = false;
            imp.filterMode = FilterMode.Bilinear;
            imp.alphaIsTransparency = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.wrapMode = TextureWrapMode.Repeat;
            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteBorder = Vector4.zero;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            imp.SetTextureSettings(settings);
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        // Smooth tileable value field ~[-1,1] (integer frequencies => wraps seamlessly).
        static float Noise2(float u, float v)
        {
            const float TAU = 6.2831853f;
            float n = Mathf.Sin(u * TAU * 3f) * Mathf.Cos(v * TAU * 2f)
                    + 0.6f * Mathf.Sin(u * TAU * 5f + 1.3f) * Mathf.Cos(v * TAU * 4f + 0.7f)
                    + 0.4f * Mathf.Sin(u * TAU * 7f + 2.1f) * Mathf.Cos(v * TAU * 6f + 1.9f);
            return n * 0.5f;
        }

        // Up-pointing triangle — the Forest tree canopy icon.
        static Sprite MakeTriSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float m = 0.12f * s;
            var vx = new float[] { m, s - m, s * 0.5f };
            var vy = new float[] { m, m, s - m };
            const int SS = 2;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                            if (PointInPoly(x + (sx + 0.5f) / SS, y + (sy + 0.5f) / SS, vx, vy)) hits++;
                    px[y * s + x] = new Color32(255, 255, 255, (byte)(255 * hits / (SS * SS)));
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "TriIcon", false, 0, FilterMode.Bilinear);
        }

        // Teardrop (round bottom, pointed top) — Ocean droplet (flipped) & Volcano flame.
        static Sprite MakeDropSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float cx = s * 0.5f, cy = 0.36f * s, r = 0.30f * s, topY = 0.94f * s;
            var vx = new float[] { cx - r, cx + r, cx };
            var vy = new float[] { cy, cy, topY };
            const int SS = 2;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                        {
                            float fx = x + (sx + 0.5f) / SS, fy = y + (sy + 0.5f) / SS;
                            bool inC = (fx - cx) * (fx - cx) + (fy - cy) * (fy - cy) <= r * r;
                            if (inC || PointInPoly(fx, fy, vx, vy)) hits++;
                        }
                    px[y * s + x] = new Color32(255, 255, 255, (byte)(255 * hits / (SS * SS)));
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "DropIcon", false, 0, FilterMode.Bilinear);
        }

        static Color32 Col(Vector3 c, float a) =>
            new Color32((byte)(Mathf.Clamp01(c.x) * 255f), (byte)(Mathf.Clamp01(c.y) * 255f),
                        (byte)(Mathf.Clamp01(c.z) * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));

        // Realistic shaded tree: brown trunk + a bushy green canopy lit from the top-left.
        static Sprite MakeTreeIconSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            var darkG = new Vector3(0.13f, 0.38f, 0.19f); var liteG = new Vector3(0.47f, 0.82f, 0.37f);
            var barkD = new Vector3(0.27f, 0.16f, 0.08f); var barkL = new Vector3(0.46f, 0.29f, 0.15f);
            float[] ccx = { 0.50f * s, 0.34f * s, 0.66f * s, 0.50f * s };
            float[] ccy = { 0.58f * s, 0.50f * s, 0.50f * s, 0.42f * s };
            float[] ccr = { 0.26f * s, 0.22f * s, 0.22f * s, 0.21f * s };
            float lx = 0.36f * s, ly = 0.70f * s;
            const int SS = 2;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    int chits = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                        {
                            float ux = x + (sx + 0.5f) / SS, uy = y + (sy + 0.5f) / SS;
                            for (int k = 0; k < 4; k++)
                            { float d0 = (ux - ccx[k]) * (ux - ccx[k]) + (uy - ccy[k]) * (uy - ccy[k]); if (d0 <= ccr[k] * ccr[k]) { chits++; break; } }
                        }
                    float canA = chits / (float)(SS * SS);
                    bool trunk = fx > 0.44f * s && fx < 0.56f * s && fy > 0.04f * s && fy < 0.34f * s;
                    if (canA > 0f)
                    {
                        float d = Mathf.Sqrt((fx - lx) * (fx - lx) + (fy - ly) * (fy - ly)) / (0.6f * s);
                        float t = Mathf.Clamp01(1f - d);
                        px[y * s + x] = Col(Vector3.Lerp(darkG, liteG, t * t), canA);
                    }
                    else if (trunk)
                    {
                        float t = Mathf.Clamp01((fx - 0.44f * s) / (0.12f * s));
                        px[y * s + x] = Col(Vector3.Lerp(barkD, barkL, t), 1f);
                    }
                    else px[y * s + x] = new Color32(0, 0, 0, 0);
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "IconTreeR", false, 0, FilterMode.Bilinear);
        }

        // Realistic glossy water droplet: blue gradient + a bright specular highlight.
        static Sprite MakeDropIconSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float cx = s * 0.5f, cy = 0.36f * s, r = 0.30f * s, topY = 0.94f * s;
            var vx = new float[] { cx - r, cx + r, cx };
            var vy = new float[] { cy, cy, topY };
            var deep = new Vector3(0.10f, 0.34f, 0.72f); var lite = new Vector3(0.52f, 0.84f, 0.98f);
            float lx = 0.40f * s, ly = 0.30f * s;
            const int SS = 2;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                        {
                            float ux = x + (sx + 0.5f) / SS, uy = y + (sy + 0.5f) / SS;
                            bool inC = (ux - cx) * (ux - cx) + (uy - cy) * (uy - cy) <= r * r;
                            if (inC || PointInPoly(ux, uy, vx, vy)) hits++;
                        }
                    float a = hits / (float)(SS * SS);
                    if (a <= 0f) { px[y * s + x] = new Color32(0, 0, 0, 0); continue; }
                    float fx = x + 0.5f, fy = y + 0.5f;
                    float d = Mathf.Sqrt((fx - lx) * (fx - lx) + (fy - ly) * (fy - ly)) / (0.62f * s);
                    float t = Mathf.Clamp01(1f - d);
                    var col = Vector3.Lerp(deep, lite, t * t);
                    float ds = Mathf.Sqrt((fx - 0.42f * s) * (fx - 0.42f * s) + (fy - 0.25f * s) * (fy - 0.25f * s));
                    col = Vector3.Lerp(col, Vector3.one, Mathf.Clamp01(1f - ds / (0.10f * s)) * 0.9f);
                    px[y * s + x] = Col(col, a);
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "IconDropR", false, 0, FilterMode.Bilinear);
        }

        // Realistic glowing star: gold rays + a white-hot core.
        static Sprite MakeStarIconSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float c = s * 0.5f, rayLen = 0.46f * s, core = 0.13f * s;
            var gold = new Vector3(1f, 0.84f, 0.46f);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - c), dy = Mathf.Abs(y + 0.5f - c);
                    float ax = Mathf.Clamp01(1f - dx / rayLen) * Mathf.Clamp01(1f - dy / (0.03f * s + 0.10f * dx));
                    float ay = Mathf.Clamp01(1f - dy / rayLen) * Mathf.Clamp01(1f - dx / (0.03f * s + 0.10f * dy));
                    float ray = Mathf.Max(ax, ay);
                    float cr = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / core); cr *= cr;
                    float a = Mathf.Clamp01(Mathf.Max(ray, cr));
                    px[y * s + x] = Col(Vector3.Lerp(gold, Vector3.one, cr), a);
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "IconStarR", false, 0, FilterMode.Bilinear);
        }

        // Ocean wave: three stacked blue wave bands with white foam crests.
        static Sprite MakeWaveIconSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            const float TAU = 6.2831853f;
            var cols = new[] { new Vector3(0.56f, 0.86f, 0.98f), new Vector3(0.30f, 0.64f, 0.92f), new Vector3(0.13f, 0.43f, 0.80f) };
            float[] baseY = { 0.64f, 0.47f, 0.30f };
            float[] amp = { 0.05f, 0.06f, 0.05f };
            float[] th = { 0.11f, 0.11f, 0.11f };
            float[] ph = { 0.0f, 1.3f, 2.6f };
            float[] freq = { 1.6f, 1.9f, 1.5f };
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = (x + 0.5f) / s, vv = (y + 0.5f) / s;
                    Color32 outp = new Color32(0, 0, 0, 0);
                    for (int i = 2; i >= 0; i--)
                    {
                        float surf = baseY[i] + amp[i] * Mathf.Sin(u * TAU * freq[i] + ph[i]);
                        float dist = vv - surf;                       // >0 above the wave surface
                        if (dist <= 0f && dist > -th[i])
                        {
                            float aa = Mathf.Min(Mathf.Clamp01(-dist / (2f / s)), Mathf.Clamp01((dist + th[i]) / (2f / s)));
                            float foam = Mathf.Clamp01((0.03f - Mathf.Abs(dist)) / 0.03f);
                            outp = Col(Vector3.Lerp(cols[i], Vector3.one, foam * 0.75f), aa);
                            break;
                        }
                    }
                    px[y * s + x] = outp;
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "IconWaveR", false, 0, FilterMode.Bilinear);
        }

        // distance from point (px,py) to segment a->b; out t = closest param along the segment.
        static float SegDist(float px, float py, float ax, float ay, float bx, float by, out float t)
        {
            float vx = bx - ax, vy = by - ay, wx = px - ax, wy = py - ay;
            float L2 = vx * vx + vy * vy;
            t = L2 <= 0f ? 0f : Mathf.Clamp01((wx * vx + wy * vy) / L2);
            float cx = ax + vx * t, cy = ay + vy * t;
            return Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
        }

        // Branching coral: thick tapered tubes on a shared trunk, deep-pink base → light-coral tips.
        static Sprite MakeCoralIconSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            // ax, ay, bx, by, thickBase, thickTip  (normalized, y up)
            float[,] br = {
                { 0.50f, 0.04f, 0.50f, 0.42f, 0.075f, 0.055f },   // trunk
                { 0.50f, 0.30f, 0.30f, 0.60f, 0.055f, 0.032f },   // left lower
                { 0.30f, 0.60f, 0.25f, 0.82f, 0.032f, 0.020f },   // left tip
                { 0.50f, 0.36f, 0.70f, 0.62f, 0.055f, 0.032f },   // right lower
                { 0.70f, 0.62f, 0.76f, 0.84f, 0.032f, 0.020f },   // right tip
                { 0.50f, 0.42f, 0.50f, 0.86f, 0.045f, 0.022f },   // center
                { 0.50f, 0.24f, 0.38f, 0.44f, 0.035f, 0.020f },   // small left
                { 0.50f, 0.40f, 0.62f, 0.56f, 0.035f, 0.020f },   // small right
            };
            float[,] tips = { { 0.25f, 0.82f, 0.048f }, { 0.76f, 0.84f, 0.048f }, { 0.50f, 0.86f, 0.052f }, { 0.38f, 0.44f, 0.032f }, { 0.62f, 0.56f, 0.032f } };
            var baseC = new Vector3(0.80f, 0.24f, 0.44f);   // deep coral-pink
            var tipC  = new Vector3(1.00f, 0.60f, 0.50f);   // light coral
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = (x + 0.5f) / s, v = (y + 0.5f) / s;
                    float bestA = 0f, hgt = 0f;
                    for (int i = 0; i < br.GetLength(0); i++)
                    {
                        float t; float d = SegDist(u, v, br[i, 0], br[i, 1], br[i, 2], br[i, 3], out t);
                        float th = br[i, 4] + (br[i, 5] - br[i, 4]) * t;
                        float a = Mathf.Clamp01(0.5f + (th - d) * s / 1.5f);
                        if (a > bestA) { bestA = a; hgt = br[i, 1] + (br[i, 3] - br[i, 1]) * t; }
                    }
                    for (int i = 0; i < tips.GetLength(0); i++)
                    {
                        float d = Mathf.Sqrt((u - tips[i, 0]) * (u - tips[i, 0]) + (v - tips[i, 1]) * (v - tips[i, 1]));
                        float a = Mathf.Clamp01(0.5f + (tips[i, 2] - d) * s / 1.5f);
                        if (a > bestA) { bestA = a; hgt = tips[i, 1]; }
                    }
                    var col = Vector3.Lerp(baseC, tipC, Mathf.Clamp01((hgt - 0.10f) / 0.75f));
                    col = Vector3.Lerp(col, Vector3.one, Mathf.Clamp01((0.5f - u) * 0.28f));   // soft left highlight
                    px[y * s + x] = Col(col, bestA);
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "IconCoral", false, 0, FilterMode.Bilinear);
        }

        // Bioluminescent jellyfish: translucent teal dome (bright at the crown) + wavy tentacles.
        static Sprite MakeJellyIconSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float cx = 0.5f, cy = 0.56f, rx = 0.30f, ry = 0.30f;
            float[,] tent = { { 0.34f, 0.0f }, { 0.43f, 1.1f }, { 0.50f, 2.0f }, { 0.57f, 0.6f }, { 0.66f, 1.7f } };
            var domeTop = new Vector3(0.74f, 1.00f, 0.95f);
            var domeBot = new Vector3(0.24f, 0.80f, 0.80f);
            var tentC   = new Vector3(0.48f, 0.92f, 0.84f);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = (x + 0.5f) / s, v = (y + 0.5f) / s;
                    float a = 0f; var col = domeBot;
                    if (v >= cy - 0.02f)
                    {
                        float ex = (u - cx) / rx, ey = (v - cy) / ry;
                        float r = Mathf.Sqrt(ex * ex + ey * ey);
                        float dome = Mathf.Clamp01(0.5f + (1f - r) * s * rx / 1.5f);
                        if (dome > a) { a = dome; col = Vector3.Lerp(domeBot, domeTop, Mathf.Clamp01((v - cy) / ry)); }
                    }
                    for (int i = 0; i < tent.GetLength(0); i++)
                    {
                        if (v >= 0.58f) continue;
                        float fall = Mathf.Clamp01((0.58f - v) / 0.46f);
                        float xoff = 0.035f * Mathf.Sin(v * 22f + tent[i, 1]) * fall;
                        float d = Mathf.Abs(u - (tent[i, 0] + xoff));
                        float th = 0.014f * (1f - 0.5f * fall);
                        float ta = Mathf.Clamp01(0.5f + (th - d) * s / 1.5f) * Mathf.Clamp01((v - 0.10f) / 0.10f) * (0.9f - 0.5f * fall);
                        if (ta > a) { a = ta; col = tentC; }
                    }
                    px[y * s + x] = Col(col, a);
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "IconJelly", false, 0, FilterMode.Bilinear);
        }

        // Underwater god-ray: a soft light shaft, bright at the top (surface) fanning + fading downward.
        static Sprite MakeGodRaySprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = (x + 0.5f) / s, v = (y + 0.5f) / s;
                    float hw = 0.06f + 0.28f * (1f - v);           // narrow at top, widening downward
                    float dx = Mathf.Abs(u - 0.5f);
                    float horiz = Mathf.Clamp01(1f - dx / hw); horiz *= horiz;
                    float vert = Mathf.SmoothStep(0f, 1f, v);      // bright at top, fades to nothing at bottom
                    float a = horiz * vert * 0.9f;
                    px[y * s + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "GodRay", false, 0, FilterMode.Bilinear);
        }

        // Solid filled circle (white, AA edge) — timer coin backing / soft shadow.
        static Sprite MakeDiscSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float c = s * 0.5f, R = 0.47f * s;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                    float a = Mathf.Clamp01(0.5f + (R - d));
                    px[y * s + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "Disc", false, 0, FilterMode.Bilinear);
        }

        // Circular ring / annulus (white, AA) — the timer track and its radial progress arc.
        static Sprite MakeRingCircleSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float c = s * 0.5f, ro = 0.47f * s, ri = 0.355f * s;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                    float a = Mathf.Clamp01(0.5f + (ro - d)) * Mathf.Clamp01(0.5f + (d - ri));
                    px[y * s + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "RingCircle", false, 0, FilterMode.Bilinear);
        }

        // Filled 5-point star (white, tinted at runtime) — the Advanced category's icon.
        static Sprite MakeStarSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float cx = s * 0.5f, cy = s * 0.5f, R = 0.46f * s, r = 0.19f * s;

            var vx = new float[10];
            var vy = new float[10];
            for (int k = 0; k < 10; k++)
            {
                float ang = -Mathf.PI / 2f + k * Mathf.PI / 5f;
                float rad = (k % 2 == 0) ? R : r;
                vx[k] = cx + Mathf.Cos(ang) * rad;
                vy[k] = cy + Mathf.Sin(ang) * rad;
            }

            const int SS = 2;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                            if (PointInPoly(x + (sx + 0.5f) / SS, y + (sy + 0.5f) / SS, vx, vy)) hits++;
                    byte a = (byte)(255 * hits / (SS * SS));
                    px[y * s + x] = new Color32(255, 255, 255, a);
                }

            tex.SetPixels32(px);
            tex.Apply();
            return SaveTexAsSprite(tex, "Star", false, 0, FilterMode.Bilinear);
        }

        static bool PointInPoly(float x, float y, float[] vx, float[] vy)
        {
            bool inside = false;
            int n = vx.Length, j = n - 1;
            for (int i = 0; i < n; i++)
            {
                if (((vy[i] > y) != (vy[j] > y)) &&
                    (x < (vx[j] - vx[i]) * (y - vy[i]) / (vy[j] - vy[i]) + vx[i]))
                    inside = !inside;
                j = i;
            }
            return inside;
        }

        // ================================================= scene building blocks
        static SpriteRenderer MakeWorldSprite(Transform parent, string name, Sprite sprite, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        static Camera CreateCamera(bool ortho)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            go.transform.position = new Vector3(0f, 0f, -10f);
            cam.orthographic = ortho;
            cam.orthographicSize = 6f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BG;

            var urpCam = go.AddComponent<UniversalAdditionalCameraData>();
            urpCam.renderPostProcessing = false;

            go.AddComponent<AudioListener>();
            return cam;
        }

        static void CreateEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        static Transform CreateCanvas(string name)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return go.transform;
        }

        // Full-screen black overlay that fades out on scene load (level transitions).
        // Added last so it draws on top of everything else on the canvas.
        static void CreateFade(Transform canvas)
        {
            var rt = MakeRect(canvas, "ScreenFade", Vector2.zero, Vector2.zero,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f); // transparent in the editor;
            img.raycastTarget = false;             // ScreenFade turns it black at runtime, then fades out
            rt.gameObject.AddComponent<ScreenFade>().image = img;
        }

        static RectTransform MakeRect(Transform parent, string name, Vector2 pos, Vector2 size,
            Vector2? anchorMin = null, Vector2? anchorMax = null, Vector2? pivot = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin ?? new Vector2(0.5f, 0.5f);
            rt.anchorMax = anchorMax ?? new Vector2(0.5f, 0.5f);
            rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static Text MakeText(Transform parent, string name, string content, int size, Color color,
            Vector2 pos, Vector2 dim, FontStyle style = FontStyle.Normal,
            TextAnchor align = TextAnchor.MiddleCenter,
            Vector2? anchorMin = null, Vector2? anchorMax = null, Vector2? pivot = null)
        {
            var rt = MakeRect(parent, name, pos, dim, anchorMin, anchorMax, pivot);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = align;
            text.raycastTarget = false;
            return text;
        }

        static Button MakeButton(Transform parent, string name, string label,
            Vector2 pos, Vector2 size, int fontSize)
            => MakeButton(parent, name, label, pos, size, fontSize, ButtonCol);

        static Button MakeButton(Transform parent, string name, string label,
            Vector2 pos, Vector2 size, int fontSize, Color color)
            => MakeButton(parent, name, label, pos, size, fontSize, Lighten(color, 0.16f), Darken(color, 0.20f));

        // Premium 3D gradient button: a lit top->bottom gradient face on a darker base lip.
        static Button MakeButton(Transform parent, string name, string label,
            Vector2 pos, Vector2 size, int fontSize, Color top, Color bottom)
        {
            var rt = MakeRect(parent, name, pos, size);
            var btn = rt.gameObject.AddComponent<Button>();
            rt.gameObject.AddComponent<UIHoverScale>();

            var round = s_roundBtn != null ? s_roundBtn
                : AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            // 3D base "lip" (darker, offset down) + a soft floating shadow — chunky & tactile
            var lipRT = MakeRect(rt, "Lip", new Vector2(0f, -7f), size);
            var lipImg = lipRT.gameObject.AddComponent<Image>();
            lipImg.sprite = round; lipImg.type = Image.Type.Sliced;
            lipImg.color = Darken(bottom, 0.32f); lipImg.raycastTarget = false;
            var sh = lipRT.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.42f);
            sh.effectDistance = new Vector2(0f, -7f);

            // lit top face with a vibrant top->bottom gradient — Button tints this
            var faceRT = MakeRect(rt, "Face", Vector2.zero, size);
            var face = faceRT.gameObject.AddComponent<Image>();
            face.sprite = round; face.type = Image.Type.Sliced; face.color = Color.white;
            var grad = faceRT.gameObject.AddComponent<UIGradient>();
            grad.top = top; grad.bottom = bottom;
            btn.targetGraphic = face;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.94f, 0.94f, 0.94f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            // glossy top-light so the face reads as a rounded, lit surface
            if (s_gloss != null)
                UIImage(faceRT, "Gloss", s_gloss, new Color(1f, 1f, 1f, 0.10f), Vector2.zero, size);

            var lbl = MakeText(faceRT, "Label", label, fontSize, Color.white, Vector2.zero, size, FontStyle.Bold);
            var lsh = lbl.gameObject.AddComponent<Shadow>();
            lsh.effectColor = new Color(0f, 0f, 0f, 0.5f);
            lsh.effectDistance = new Vector2(0f, -2f);
            return btn;
        }

        // A non-interactive rounded UI image (menu decoration / mascot).
        // A clean flat control button — rounded slate fill + thin accent outline + centered label. Clickable.
        static Button MakeFlatButton(Transform parent, string name, string label, int fontSize,
                                     Vector2 pos, Vector2 size, Color accent, Sprites spr)
        {
            var rt = MakeRect(parent, name, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = spr.fill; img.type = Image.Type.Sliced; img.color = Hex("14304A");
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.82f, 0.90f, 0.96f, 1f);
            colors.pressedColor = new Color(0.68f, 0.78f, 0.86f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            rt.gameObject.AddComponent<UIHoverScale>();
            AddRing(rt, "Outline", size, spr.ringThin, WithAlpha(accent, 0.45f));
            if (!string.IsNullOrEmpty(label))
                MakeText(rt, "Label", label, fontSize, Color.white, Vector2.zero, size, FontStyle.Bold);
            return btn;
        }

        static Image UIImage(Transform parent, string name, Sprite sprite, Color color, Vector2 pos, Vector2 size)
        {
            var rt = MakeRect(parent, name, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        // "Beaten" checkmark badge for a level button; hidden until MainMenuUI shows it.
        static GameObject MakeCheck(Transform button, Sprites spr)
        {
            var rt = MakeRect(button, "Check", new Vector2(32, 20), new Vector2(30, 30));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = spr.fill;
            img.color = Hex("34C759");
            img.raycastTarget = false;
            MakeText(rt, "Tick", "✓", 22, Color.white, Vector2.zero, new Vector2(30, 30), FontStyle.Bold);
            rt.gameObject.SetActive(false);
            return rt.gameObject;
        }

        // ================================================= color helpers
        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }

        static Color Lighten(Color c, float t) => Color.Lerp(c, Color.white, t);
        static Color Darken(Color c, float t) => Color.Lerp(c, Color.black, t);
    }
}
