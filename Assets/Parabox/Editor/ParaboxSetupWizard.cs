using System.Collections.Generic;
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
    // sprite tile prefabs, 50 level prefabs, the MainMenu and Game scenes and adds
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
        static readonly Color ShadowCol = new Color(0f, 0f, 0f, 0.46f);

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

        class Sprites { public Sprite fill, floorFill, tile, cell, cellRing, grid, ringThin, ringThick, ringBox, trophy, shaded, shadow, lockIcon, star, glow, vignette, disc, ringCircle, iconPlay, texGrass, texWater, texRock, texStars, iconTri, iconDrop, iconTreeR, iconDropR, iconStarR, iconWaveR, iconCoral, iconJelly, godRay; }

        // Rounded sprite + top-light gloss used to make every button look premium (set once sprites exist).
        static Sprite s_roundBtn;
        static Sprite s_gloss;
        static Sprite s_glow;   // soft glow used for the hover/selected button highlight
        static Font s_font;     // optional drop-in font from Assets/Parabox/Fonts (else the default UI font)
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
            s_glow = spr.glow;
            s_font = LoadGameFont();
            var tiles = CreateTilePrefabs(spr);
            var levels = CreateLevelPrefabs(tiles);
            string gamePath = CreateGameScene(spr, tiles, levels);
            string menuPath = CreateMainMenuScene(spr, levels, tiles);

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
                "URP assigned, rounded sprites generated, tile prefabs, 50 level\n" +
                "prefabs, MainMenu + Game scenes created and added to Build Settings.\n\n" +
                "You can now DELETE the folder Assets/Parabox/Editor.\n" +
                "Keep Assets/Parabox/Scripts — the game needs it!",
                "OK");
        }

        [MenuItem("Tools/Parabox/Regenerate Master Levels (41-50)")]
        public static void RegenerateMasterLevels()
        {
            var tiles = new Tiles
            {
                floor = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Floor.prefab"),
                grid = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Grid.prefab"),
                border = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Border.prefab"),
                wall = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Wall.prefab"),
                box = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Box.prefab"),
                metaBox = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/MetaBox.prefab"),
                player = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Player.prefab"),
                boxGoal = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/BoxGoal.prefab"),
                playerGoal = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/PlayerGoal.prefab"),
            };

            if (tiles.floor == null || tiles.grid == null || tiles.border == null ||
                tiles.wall == null || tiles.box == null || tiles.metaBox == null ||
                tiles.player == null || tiles.boxGoal == null || tiles.playerGoal == null)
            {
                Debug.LogError("Parabox: one or more base tile prefabs are missing. Run Create Everything first.");
                return;
            }

            var defs = Levels();
            for (int i = 40; i < Mathf.Min(50, defs.Length); i++)
                BuildLevelPrefab(i, defs[i], tiles);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Parabox: regenerated Master levels 41-50.");
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
            EnsureFolder(Root + "/Art");     // drop a custom MenuBG.png / GameBG.png here (kept across re-runs)
            EnsureFolder(Root + "/Audio");   // bundled CC0 music lives here; Music.* can replace it on future re-runs
            EnsureFolder(Root + "/Fonts");   // drop a .ttf/.otf here to restyle all text, kept across re-runs
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
                // filled rounded square — boxes, player, UI cards (nicely rounded)
                fill = MakeSprite("Fill", 0.22f, 0f, Mathf.RoundToInt(0.22f * Res) + 10, true),
                // board floor / frame — barely rounded corners so the full-size grid doesn't visibly overshoot
                floorFill = MakeSprite("FloorFill", 0.03f, 0f, Mathf.RoundToInt(0.03f * Res) + 8, true),
                // barely-rounded square — walls (solid blocks that fill their cell)
                tile = MakeSprite("Tile", 0.11f, 0f, 0, false),
                // crisp near-square board cell — flat surface with only a whisper of top light
                cell = MakeShadedSprite("Cell", 0.09f, 1.20f, 0.80f),
                // Ring that traces the CELL exactly — same 0.05 corner radius. ringThin is 0.18,
                // so using it to outline a cell drew a round border around a square face and the
                // two corners fought each other. A border must follow the shape it borders.
                cellRing = MakeSprite("CellRing", 0.05f, 0.055f, Mathf.RoundToInt(0.10f * Res) + 6, true),
                // hairline grid, tiled over the flat floor (thin lines on cell edges)
                grid = MakeGridSprite(),
                // thin rounded ring — UI panel/button outlines (sliced)
                ringThin = MakeSprite("RingThin", 0.18f, 0.05f, Mathf.RoundToInt(0.23f * Res) + 8, true),
                // thick rounded ring — goals and meta-box frame (uniform scale)
                ringThick = MakeSprite("RingThick", 0.24f, 0.13f, 0, false),
                // The meta-box's own frame. ringThick's stroke is so fat that its inner opening is
                // only 0.605 units — narrower than the 0.72 interior it surrounds — so it drew OVER
                // the room's edge cells (goals came out as clipped "U"/"C" shapes) AND, once you
                // stepped inside the box, that band filled the view. 0.05 opens it to 0.740.
                ringBox = MakeSprite("RingBox", 0.22f, 0.05f, 0, false),
                // top-lit gradient rounded square — boxes & player look rounded / 3D
                shaded = MakeShadedSprite("Shaded", 0.22f, 1.12f, 0.62f),
                // soft dark blob — drop shadow under boxes & player
                shadow = MakeShadowSprite(),
                // premium padlock icon — locked levels on the level-select screen
                lockIcon = MakeLockSprite(),
                // 5-point star — the Advanced difficulty icon
                star = MakeStarSprite(),
                // The completion trophy. Four endings were built before anyone noticed the game
                // had no artifact for finishing it — only effects, which evaporate. This is the
                // thing you can point at.
                trophy = MakeTrophySprite(),
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
        // ---- strict object design system (all values in CELL units; one scale for the whole game) ----
        const float ObjSize    = 0.84f;   // EVERY movable object + goal shares this exact footprint
        const float TileSize   = 0.90f;   // board tile + wall — fills its grid cell, leaving the gutter
        const float ObjShadow  = 0.92f;   // one uniform drop-shadow size under every object
        const float ObjShadowY = -0.085f; // one uniform shadow offset

        static Tiles CreateTilePrefabs(Sprites s)
        {
            var t = new Tiles();

            // Floor — sliced sprite sized to the room at runtime (barely-rounded so the grid aligns cleanly).
            var floor = new GameObject("Floor");
            var fsr = floor.AddComponent<SpriteRenderer>();
            fsr.sprite = s.floorFill; fsr.color = RoomColors[0];
            fsr.drawMode = SpriteDrawMode.Sliced; fsr.size = Vector2.one; fsr.sortingOrder = OrderFloor;
            t.floor = SavePrefab(floor, PrefabDir + "/Floor.prefab");

            // Grid — a hairline overlay tiled over the flat floor at runtime. Gives the
            // room a subtle grid WITHOUT a rounded plate under every object.
            var grid = new GameObject("Grid");
            var gsr = grid.AddComponent<SpriteRenderer>();
            gsr.sprite = s.grid; gsr.color = new Color(0f, 0f, 0f, 0.12f);
            gsr.drawMode = SpriteDrawMode.Tiled; gsr.size = Vector2.one; gsr.sortingOrder = OrderFloor + 1;
            t.grid = SavePrefab(grid, PrefabDir + "/Grid.prefab");

            // Frame base — solid rounded fill (same sprite as the floor), drawn behind an inset floor
            // at runtime so the border is a clean rounded frame (no hollow ring = no edge artifacts).
            var border = new GameObject("Border");
            var bsr = border.AddComponent<SpriteRenderer>();
            bsr.sprite = s.fill; bsr.color = Color.white;
            bsr.drawMode = SpriteDrawMode.Sliced; bsr.size = Vector2.one; bsr.sortingOrder = OrderBorder;
            t.border = SavePrefab(border, PrefabDir + "/Border.prefab");

            // Wall — dark block that fills its cell (reads as a solid barrier).
            var wall = new GameObject("Wall");
            SpriteChild("Sprite", wall.transform, s.tile, WallColor, OrderWall, Vector3.zero, V(TileSize));
            t.wall = SavePrefab(wall, PrefabDir + "/Wall.prefab");

            // Box — a soft drop shadow behind a top-lit rounded square (reads as 3D).
            var box = new GameObject("Box");
            box.AddComponent<EntityView>();
            SpriteChild("Shadow", box.transform, s.shadow, ShadowCol, OrderBox - 1, new Vector3(0f, ObjShadowY, 0f), V(ObjShadow));
            SpriteChild("Sprite", box.transform, s.shaded, BoxColor, OrderBox, Vector3.zero, V(ObjSize));
            // inset panel + top highlight — gives the crate a readable face instead of a flat square
            SpriteChild("Panel", box.transform, s.shaded, Lighten(BoxColor, 0.22f), OrderBox + 1,
                        Vector3.zero, V(ObjSize * 0.58f));
            SpriteChild("Gloss", box.transform, s.fill, new Color(1f, 1f, 1f, 0.20f), OrderBox + 2,
                        new Vector3(0f, ObjSize * 0.26f, 0f), new Vector3(ObjSize * 0.66f, ObjSize * 0.13f, 1f));
            t.box = SavePrefab(box, PrefabDir + "/Box.prefab");

            // MetaBox — dark backing + colored frame. Interior room is nested at runtime;
            // a SortingGroup isolates the contents so nesting sorts correctly.
            var meta = new GameObject("MetaBox");
            meta.AddComponent<EntityView>();
            meta.AddComponent<SortingGroup>().sortingOrder = OrderBox;
            SpriteChild("Shadow", meta.transform, s.shadow, ShadowCol, OrderBackingInBox - 10, new Vector3(0f, ObjShadowY, 0f), V(ObjShadow));
            SpriteChild("Backing", meta.transform, s.shaded, Darken(RoomColors[1], 0.5f), OrderBackingInBox, Vector3.zero, V(ObjSize));
            SpriteChild("Frame", meta.transform, s.ringBox, Lighten(RoomColors[1], 0.3f), OrderFrameInBox, Vector3.zero, V(ObjSize));
            t.metaBox = SavePrefab(meta, PrefabDir + "/MetaBox.prefab");

            // Player — a shadow behind a top-lit rounded box with two eyes; eyes blink.
            var player = new GameObject("Player");
            player.AddComponent<EntityView>();
            SpriteChild("Shadow", player.transform, s.shadow, ShadowCol, OrderPlayer - 1, new Vector3(0f, ObjShadowY, 0f), V(ObjShadow));
            SpriteChild("Body", player.transform, s.shaded, PlayerColor, OrderPlayer, Vector3.zero, V(ObjSize));
            var eyeL = SpriteChild("EyeL", player.transform, s.fill, EyeColor, OrderPlayer + 1, new Vector3(-0.162f, 0.067f, 0f), new Vector3(0.134f, 0.20f, 1f));
            var eyeR = SpriteChild("EyeR", player.transform, s.fill, EyeColor, OrderPlayer + 1, new Vector3(0.162f, 0.067f, 0f), new Vector3(0.134f, 0.20f, 1f));
            var blink = player.AddComponent<Blinker>();
            blink.eyeL = eyeL.transform;
            blink.eyeR = eyeR.transform;
            t.player = SavePrefab(player, PrefabDir + "/Player.prefab");

            // Goals — thick rounded outline in the target's color.
            var boxGoal = new GameObject("BoxGoal");
            SpriteChild("Glow", boxGoal.transform, s.glow, WithAlpha(BoxColor, 0.42f), OrderGoal - 1, Vector3.zero, V(ObjSize * 1.7f));
            SpriteChild("Socket", boxGoal.transform, s.shaded, new Color(0f, 0f, 0f, 0.34f), OrderGoal, Vector3.zero, V(ObjSize * 0.86f));
            SpriteChild("Ring", boxGoal.transform, s.ringThick, BoxColor, OrderGoal + 1, Vector3.zero, V(ObjSize));
            t.boxGoal = SavePrefab(boxGoal, PrefabDir + "/BoxGoal.prefab");

            var playerGoal = new GameObject("PlayerGoal");
            SpriteChild("Glow", playerGoal.transform, s.glow, WithAlpha(PlayerColor, 0.42f), OrderGoal - 1, Vector3.zero, V(ObjSize * 1.7f));
            SpriteChild("Socket", playerGoal.transform, s.shaded, new Color(0f, 0f, 0f, 0.34f), OrderGoal, Vector3.zero, V(ObjSize * 0.86f));
            SpriteChild("Ring", playerGoal.transform, s.ringThick, PlayerColor, OrderGoal + 1, Vector3.zero, V(ObjSize));
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
        class LevelDef { public string name; public string[][] rooms; public int par; public string solution; }

        static LevelDef[] Levels() => new[]
        {
            new LevelDef { name = "Down the Chute", rooms = new[] { new[] { "#######", "#..P..#", "#..b..#", "#.._..#", "#.._..#", "#..x..#", "#.....#", "#######" } }, par = 1, solution = "D" },
            new LevelDef { name = "Long Slide", rooms = new[] { new[] { "########", "#P.b__x#", "#......#", "########" } }, par = 2, solution = "RR" },
            new LevelDef { name = "Take Aim", rooms = new[] { new[] { "#######", "#P....#", "#..b..#", "#.._..#", "#.._..#", "#..x..#", "#######" } }, par = 3, solution = "RRD" },
            new LevelDef { name = "The Straight", rooms = new[] { new[] { "##########", "#P.b__..x#", "#........#", "##########" } }, par = 5, solution = "RRRRR" },
            new LevelDef { name = "Ice Block", rooms = new[] { new[] { "######", "#P...#", "#.b__#", "#..__#", "#...x#", "######" } }, par = 6, solution = "DRRURD" },
            new LevelDef { name = "Two Ways", rooms = new[] { new[] { "########", "#P.b__x#", "#..b...#", "#.._...#", "#.._...#", "#...x..#", "########" } }, par = 7, solution = "RRDDLDR" },
            new LevelDef { name = "The Bend", rooms = new[] { new[] { "#######", "#..P..#", "#..b..#", "#.._..#", "#..__x#", "#.....#", "#######" } }, par = 8, solution = "LDRRURDD" },
            new LevelDef { name = "Long Bend", rooms = new[] { new[] { "#######", "#.P...#", "#.b...#", "#._...#", "#.___x#", "#.....#", "#######" } }, par = 9, solution = "LDRRRURDD" },
            new LevelDef { name = "Aim It", rooms = new[] { new[] { "########", "#Pb__.x#", "#..__..#", "#..b..x#", "#......#", "########" } }, par = 9, solution = "RRRLDDRRR" },
            new LevelDef { name = "Cold Finish", rooms = new[] { new[] { "########", "#P.b_.x#", "#......#", "#..b_.x#", "#......#", "########" } }, par = 11, solution = "RRRRLLDDRRR" },
            new LevelDef { name = "The Drift", rooms = new[] { new[] { "##########", "#P.ddddd.#", "#.######.#", "#.######.#", "#.......p#", "##########" } }, par = 5, solution = "RRDDD" },
            new LevelDef { name = "The Geyser", rooms = new[] { new[] { "########", "#P*~...#", "#####.##", "#......#", "#.....p#", "########" } }, par = 6, solution = "RRDDRD" },
            new LevelDef { name = "Against the Flow", rooms = new[] { new[] { "#######", "#P.<.p#", "#.###.#", "#.....#", "#######" } }, par = 8, solution = "DDRRRRUU" },
            new LevelDef { name = "The Shell Switch", rooms = new[] { new[] { "#########", "#P.b...B#", "#####G###", "#...p...#", "#########" } }, par = 9, solution = "RRRRRLDDL" },
            new LevelDef { name = "Dead Weight", rooms = new[] { new[] { "##########", "#P.b....W#", "#####H####", "#....p...#", "##########" } }, par = 10, solution = "RRRRRRLLDD" },
            new LevelDef { name = "Deep Water", rooms = new[] { new[] { "#######", "#..P..#", "#..b..#", "#,.,,,#", "#...x.#", "#######" } }, par = 11, solution = "RDLULDDLDRR" },
            new LevelDef { name = "The Pearl", rooms = new[] { new[] { "#########", "#P....k.#", "#.#####.#", "#.......#", "####K####", "###.p.###", "#########" } }, par = 13, solution = "RRRRRRDDLLLDD" },
            new LevelDef { name = "Through the Kelp", rooms = new[] { new[] { "#########", "#P......#", "#..b....#", "#.%%%%..#", "#..xp...#", "#########" } }, par = 14, solution = "RDRRRURDDRDLLL" },
            new LevelDef { name = "No Way Back", rooms = new[] { new[] { "#######", "#Pp...#", "###c#.#", "#.....#", "#.b.x.#", "#.....#", "#######" } }, par = 17, solution = "RRRRDDLLLLDRRUUUL" },
            new LevelDef { name = "Undertow", rooms = new[] { new[] { "###########", "#.........#", "#P.b......#", "#%%%%%%%..#", "#B........#", "#.dddddd..#", "########G##", "#.......p.#", "###########" } }, par = 22, solution = "RRRRRRURDDRDLLLLLLLDDD" },
            new LevelDef { name = "Too Narrow", rooms = new[] { new[] { "###########", "#P.b=x....#", "#.#######.#", "#.........#", "#.#######.#", "#p........#", "###########" } }, par = 8, solution = "RRLLDDDD" },
            new LevelDef { name = "Slippery Cargo", rooms = new[] { new[] { "##########", "#........#", "#P.i....x#", "#.######.#", "#........#", "#..p.....#", "##########" } }, par = 9, solution = "RRLLDDRRD" },
            new LevelDef { name = "Break Through", rooms = new[] { new[] { "#############", "#P...b...R.p#", "#############" } }, par = 10, solution = "RRRRRRRRRR" },
            new LevelDef { name = "Throw the Switch", rooms = new[] { new[] { "############", "#P.........#", "#.########.#", "#.....T....#", "#####L######", "#....p.....#", "############" } }, par = 10, solution = "DDRRRRRLDD" },
            new LevelDef { name = "Off Beat", rooms = new[] { new[] { "#############", "#P...:.....p#", "#.###########", "#.###########", "#############" } }, par = 12, solution = "RRRLRRRRRRRR" },
            new LevelDef { name = "Get a Run-Up", rooms = new[] { new[] { "###########", "#.........#", "#..P......#", "#..O......#", "#..x......#", "#.........#", "#........p#", "###########" } }, par = 12, solution = "UDDRRRRRRDDD" },
            new LevelDef { name = "Down the Well", rooms = new[] { new[] { "###########", "#P........#", "#.#######.#", "#..b.....g#", "#########g#", "#########g#", "#.#######.#", "#........x#", "###########" } }, par = 13, solution = "DDRRRRRRRRDDD" },
            new LevelDef { name = "Your Shadow", rooms = new[] { new[] { "##########", "#P......p#", "#..####..#", "#E.#....e#", "#...##...#", "#........#", "#........#", "##########" } }, par = 17, solution = "RDDDRDRRRRRUUUUDU" },
            new LevelDef { name = "Colour Coded", rooms = new[] { new[] { "##########", "#........#", "#P.J.N...#", "#........#", "#..n....j#", "##########" } }, par = 21, solution = "RRRURDDLDRRRUULLLULDD" },
            new LevelDef { name = "The Machine", rooms = new[] { new[] { "############", "#P..b..R...#", "##########.#", "#....T.....#", "######L#####", "#.....O....#", "#.....x....#", "#.##########", "#p.........#", "############" } }, par = 27, solution = "RRRRRRRRRDDLLLLLRDDLLLLLDDD" },
            new LevelDef { name = "Wrong Chimney", rooms = new[] { new[] { "#############", "##.ux#...u..#", "##.u#####u###", "##.u#####u###", "##.uPb...u..#", "####........#", "#############" } }, par = 12, solution = "DRRULLLLUUUR" },
            new LevelDef { name = "Facing Away", rooms = new[] { new[] { "##########", "#p......P#", "#........#", "#M...#..m#", "##########" } }, par = 13, solution = "LLLLLDLLURRLL" },
            new LevelDef { name = "One Way In", rooms = new[] { new[] { "##########", "#........#", "#P.b[..x.#", "#........#", "#........#", "##########" } }, par = 13, solution = "RURDLDRRRRDRU" },
            new LevelDef { name = "Carried Past", rooms = new[] { new[] { "########", "###x####", "###b####", "#P;p;..#", "###.##.#", "###....#", "########" } }, par = 14, solution = "RRRRRDDLLLUUUD" },
            new LevelDef { name = "Drawn In", rooms = new[] { new[] { "#############", "#...........#", "#P...b......#", "#.#########.#", "#.#########.#", "#...........#", "#......x#Y..#", "#############" } }, par = 17, solution = "RRRURRDLLLLULDDDD" },
            new LevelDef { name = "Set in Stone", rooms = new[] { new[] { "#########", "####...P#", "####..q.#", "##.bx#..#", "##......#", "####x####", "#########" } }, par = 18, solution = "LDDRDLLRUULLDDLLUR" },
            new LevelDef { name = "Nothing to Brace", rooms = new[] { new[] { "###############", "#.............#", "#.............#", "#P-b...-.....x#", "#.............#", "#.............#", "###############" } }, par = 19, solution = "RURDLDRRRRRRRRRRDRU" },
            new LevelDef { name = "Opposite Numbers", rooms = new[] { new[] { "##################", "#p..............P#", "#.......[........#", "#M.......#......m#", "#................#", "##################" } }, par = 21, solution = "LLLLLLLLLDLLURRLLLLLL" },
            new LevelDef { name = "Two of Us", rooms = new[] { new[] { "############", "#P........p#", "#..####....#", "#E.#..[...e#", "#...##.....#", "#..........#", "############" } }, par = 23, solution = "RRLDDDRDRRRRUUURULRRRDU" },
            new LevelDef { name = "The Undertow", rooms = new[] { new[] { "##############", "#P..........p#", "#..####......#", "#E.#..[.....e#", "#...##.......#", "#....##......#", "#-...........#", "##############" } }, par = 25, solution = "RRLDDDRDRDRRRUURULRRRRRUU" },
            new LevelDef { name = "Undertow Chamber", rooms = new[] { new[] { "###########", "#.........#", "#P.b......#", "#%%%%%%%..#", "#B........#", "#.dddddd..#", "########G##", "#.......Q.#", "###########" }, new[] { "####.####", "#.......#", "#..b....#", "...###..#", "#...x...#", "#......p#", "#########" } }, par = 38, solution = "RRRRRRURDDRDLLLLLLLDDDDDLULDDLDRRDRRRR" },
            new LevelDef { name = "Pulse Chamber", rooms = new[] { new[] { "#############", "#P...:.....Q#", "#.###########", "#.###########", "#############" }, new[] { "#########", "#...x...#", "#..###..#", "..b.....#", "#.......#", "#......p#", "#########" } }, par = 31, solution = "DURRRRRRRRRRRDRUULURRLDDDDRRRRR" },
            new LevelDef { name = "Boulder Chamber", rooms = new[] { new[] { "###########", "#.........#", "#..P......#", "#..O......#", "#..x......#", "#.........#", "#........Q#", "###########" }, new[] { "####.####", "#.......#", "#..b....#", "...###..#", "#...x...#", "#......p#", "#########" } }, par = 28, solution = "UDDRDDRRRRRDDDLULDDLDRRDRRRR" },
            new LevelDef { name = "Latch Chamber", rooms = new[] { new[] { "############", "#P.........#", "#.########.#", "#.....T....#", "#####L######", "#....Q.....#", "############" }, new[] { "####.####", "#p......#", "#.#####.#", "#.......#", "#..b....#", "#..###..#", "#...x...#", "#.......#", "#########" } }, par = 36, solution = "DDRRRRRLDDDLLLDDRRRDLULDDLDRRLUUULUU" },
            new LevelDef { name = "Pearl Chamber", rooms = new[] { new[] { "#########", "#P....k.#", "#.#####.#", "#.......#", "####K####", "###.Q.###", "#########" }, new[] { "####.####", "#.......#", "#..b....#", "#..###..#", "#...x...#", "#......p#", "#########" } }, par = 29, solution = "RRRRRRDDLLLDDDDLULDDLDRRDRRRR" },
            new LevelDef { name = "Coral Chamber", rooms = new[] { new[] { "#######", "#PQ...#", "###c#.#", "#.....#", "#.b.x.#", "#.....#", "#######" }, new[] { "#########", "#...x...#", "#..###..#", "..b......", "#.......#", "#......p#", "#########" } }, par = 38, solution = "RRDRUULURRLDDRRRRRRRDDLLDRRURRUULLLLDD" },
            new LevelDef { name = "Weight Chamber", rooms = new[] { new[] { "##########", "#P.b....W#", "#####H####", "#....Q...#", "##########" }, new[] { "####.####", "#p......#", "#.#####.#", "#.......#", "#..b....#", "#..###..#", "#...x...#", "#.......#", "#########" } }, par = 36, solution = "RRRRRRLLDDDLLLDDRRRDLULDDLDRRLUUULUU" },
            new LevelDef { name = "Switch Chamber", rooms = new[] { new[] { "#########", "#P.b...B#", "#####G###", "#...Q...#", "#########" }, new[] { "#########", "#...x...#", "#..###..#", "#.....b..", "#.......#", "#p......#", "#########" } }, par = 28, solution = "RRRRRLDDLLDLUURULLRDDDDLLLLL" },
            new LevelDef { name = "One-Way Chamber", rooms = new[] { new[] { "#######", "#Q.<.P#", "#.###.#", "#.....#", "#######" }, new[] { "#########", "#.......#", "#...x...#", "#..###..#", "#....b..#", "#.......#", "#.#####.#", "#......p#", "####.####" } }, par = 34, solution = "DDLLLLUUULLLUUURRRRDRUURULLRDDDRDD" },
            new LevelDef { name = "The Machine Within", rooms = new[] { new[] { "############", "#P..b..R...#", "##########.#", "#....T.....#", "######L#####", "#.....O....#", "#.....x....#", "#.##########", "#Q.........#", "############" }, new[] { "####.####", "#.......#", "#..b....#", "#..###..#", "#...x...#", "#......p#", "#########" } }, par = 43, solution = "RRRRRRRRRDDLLLLLRDDLDLLLLDDDDLULDDLDRRDRRRR" },
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
            var levelInfo = root.AddComponent<ParaboxLevel>();
            levelInfo.levelName = def.name;
            levelInfo.par = def.par;   // the move limit is derived from this at runtime
            levelInfo.solution = def.solution;   // the tutorial replays this on the real board

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
                        else if (ch == '~')
                        {
                            // trench: a marker-only object (the board is rendered from the model, so
                            // no tile is needed in the prefab — only the marker LevelParser reads)
                            var go = new GameObject("Trench");
                            go.transform.SetParent(roomGO.transform, false);
                            go.transform.localPosition = pos;
                            var mk = go.AddComponent<TrenchMarker>(); mk.x = x; mk.y = y;
                        }
                        else if (ch == '_')
                        {
                            // ice: marker-only, rendered from the model like the trench/portal
                            var go = new GameObject("Ice");
                            go.transform.SetParent(roomGO.transform, false);
                            go.transform.localPosition = pos;
                            var mk = go.AddComponent<IceMarker>(); mk.x = x; mk.y = y;
                        }
                        else if (ch == 'o')
                        {
                            // whirlpool portal: marker-only, rendered from the model like the trench
                            var go = new GameObject("Portal");
                            go.transform.SetParent(roomGO.transform, false);
                            go.transform.localPosition = pos;
                            var mk = go.AddComponent<PortalMarker>(); mk.x = x; mk.y = y;
                        }
                        else if (TerrainKinds.ContainsKey(ch))
                        {
                            var go = new GameObject(TerrainKinds[ch].ToString());
                            go.transform.SetParent(roomGO.transform, false);
                            go.transform.localPosition = pos;
                            var mk = go.AddComponent<TerrainMarker>();
                            mk.x = x; mk.y = y; mk.kind = TerrainKinds[ch];
                        }
                        else if (CurrentDirs.ContainsKey(ch) || OneWayDirs.ContainsKey(ch)
                                 || ch == 'c' || ch == 'B' || ch == 'G' || ch == 'W' || ch == 'H')
                        {
                            // Every batch-A mechanic is marker-only: the board is drawn from the
                            // model, so the prefab carries just the fact, never a tile.
                            var go = new GameObject(MechName(ch));
                            go.transform.SetParent(roomGO.transform, false);
                            go.transform.localPosition = pos;

                            if (CurrentDirs.TryGetValue(ch, out var cd))
                            {
                                var mk = go.AddComponent<CurrentMarker>();
                                mk.x = x; mk.y = y; mk.dx = cd.x; mk.dy = cd.y;
                            }
                            else if (OneWayDirs.TryGetValue(ch, out var od))
                            {
                                var mk = go.AddComponent<OneWayMarker>();
                                mk.x = x; mk.y = y; mk.dx = od.x; mk.dy = od.y;
                            }
                            else if (ch == 'c')
                            {
                                var mk = go.AddComponent<CrackMarker>(); mk.x = x; mk.y = y;
                            }
                            else if (ch == 'B' || ch == 'W')
                            {
                                var mk = go.AddComponent<SwitchMarker>();
                                mk.x = x; mk.y = y; mk.heavy = ch == 'W';
                            }
                            else
                            {
                                var mk = go.AddComponent<GateMarker>();
                                mk.x = x; mk.y = y; mk.heavy = ch == 'H';
                            }
                        }
                        else if (CrateKinds.TryGetValue(ch, out var crate))
                        {
                            var go = Place(t.box, roomGO.transform, pos);
                            var mk = go.AddComponent<BoxMarker>();
                            mk.x = x; mk.y = y; mk.containsRoomId = -1;
                            mk.colour = crate.colour; mk.slick = crate.slick;
                            mk.boulder = crate.boulder; mk.locking = crate.locking;
                            mk.fragile = crate.fragile;
                        }
                        else if ((ch >= '1' && ch <= '9') || AnchoredBoxes.ContainsKey(ch))
                        {
                            int inner = AnchoredBoxes.TryGetValue(ch, out var aid) ? aid : ch - '0';
                            var go = Place(t.metaBox, roomGO.transform, pos);
                            var mk = go.AddComponent<BoxMarker>();
                            mk.x = x; mk.y = y; mk.containsRoomId = inner;
                            mk.anchored = AnchoredBoxes.ContainsKey(ch);
                            Color inC = RoomColors[inner % RoomColors.Length];
                            var back = go.transform.Find("Backing"); if (back) back.GetComponent<SpriteRenderer>().color = Darken(inC, 0.5f);
                            var frame = go.transform.Find("Frame"); if (frame) frame.GetComponent<SpriteRenderer>().color = Lighten(inC, 0.3f);
                        }
                        else if (ch == 'P' || ch == 'E' || ch == 'M')
                        {
                            var go = Place(t.player, roomGO.transform, pos);
                            var mk = go.AddComponent<PlayerMarker>();
                            mk.x = x; mk.y = y; mk.isEcho = ch == 'E'; mk.isMirror = ch == 'M';
                        }
                        else if (ch == 'x')
                        {
                            var go = Place(t.boxGoal, roomGO.transform, pos);
                            var mk = go.AddComponent<GoalMarker>(); mk.x = x; mk.y = y; mk.forPlayer = false;
                        }
                        else if (ch == 'p' || ch == 'e' || ch == 'm')
                        {
                            var go = Place(t.playerGoal, roomGO.transform, pos);
                            var mk = go.AddComponent<GoalMarker>();
                            mk.x = x; mk.y = y;
                            mk.forPlayer = ch == 'p'; mk.forEcho = ch == 'e'; mk.forMirror = ch == 'm';
                        }
                        else if (ColourGoals.TryGetValue(ch, out var goalColour))
                        {
                            var go = Place(t.boxGoal, roomGO.transform, pos);
                            var mk = go.AddComponent<GoalMarker>();
                            mk.x = x; mk.y = y; mk.colour = goalColour;
                        }
                    }
                }
            }

            return SavePrefab(root, LevelDir + "/Level_" + (index + 1) + ".prefab");
        }

        // Level-string chars for the directional mechanics. WASD reads as "carried this way";
        // the arrows read as "only passable this way". Kept here so the wizard and the Python
        // solver's CURRENT_CHARS / ONEWAY_CHARS stay side by side and obviously identical.
        static readonly Dictionary<char, Vector2Int> CurrentDirs = new Dictionary<char, Vector2Int>
        {
            { 'w', Vector2Int.up }, { 's', Vector2Int.down },
            { 'a', Vector2Int.left }, { 'd', Vector2Int.right },
        };
        static readonly Dictionary<char, Vector2Int> OneWayDirs = new Dictionary<char, Vector2Int>
        {
            { '^', Vector2Int.up }, { 'v', Vector2Int.down },
            { '<', Vector2Int.left }, { '>', Vector2Int.right },
        };

        static readonly Dictionary<char, TerrainKind> TerrainKinds = new Dictionary<char, TerrainKind>
        {
            { '%', TerrainKind.Kelp }, { ',', TerrainKind.Deep },
            { 'k', TerrainKind.Key }, { 'K', TerrainKind.Lock }, { '*', TerrainKind.Geyser },
            { '=', TerrainKind.Gap }, { 'g', TerrainKind.Gravity },
            { 'R', TerrainKind.Rock }, { 'T', TerrainKind.Toggle },
            { 'L', TerrainKind.Latch }, { ':', TerrainKind.Pulse },
            { 'u', TerrainKind.Updraft }, { '[', TerrainKind.Cage },
            { '@', TerrainKind.Deflector }, { '-', TerrainKind.Sand },
            { 'Y', TerrainKind.Magnet }, { ';', TerrainKind.Sticky },
            { '!', TerrainKind.Swap },
        };

        // Meta-boxes that are bolted to the floor: enterable, never pushable. The char stands in
        // for the interior room id, exactly as '1'..'9' do. Must match ANCHORED_BOXES in the
        // Python solver or a level it proved solvable will behave differently in game.
        static readonly Dictionary<char, int> AnchoredBoxes = new Dictionary<char, int>
        {
            { 'Q', 1 }, { 'U', 2 }, { 'V', 3 },
        };

        // Crates that are more than a crate. Colour > 0 means it has its own matching goal;
        // slick means it keeps sliding once shoved.
        static readonly Dictionary<char, CrateKind> CrateKinds = new Dictionary<char, CrateKind>
        {
            { 'b', new CrateKind() },
            { 'i', new CrateKind { slick = true } },
            { 'J', new CrateKind { colour = 1 } },
            { 'N', new CrateKind { colour = 2 } },
            { 'O', new CrateKind { boulder = true } },
            { 'q', new CrateKind { locking = true } },
            { 'f', new CrateKind { fragile = true } },
        };
        class CrateKind { public int colour; public bool slick, boulder, locking, fragile; }
        static readonly Dictionary<char, int> ColourGoals = new Dictionary<char, int>
        {
            { 'j', 1 }, { 'n', 2 },
        };

        static string MechName(char ch)
        {
            if (CurrentDirs.ContainsKey(ch)) return "Current";
            if (OneWayDirs.ContainsKey(ch)) return "OneWay";
            if (ch == 'c') return "Crack";
            if (ch == '=') return "Gap";
            if (ch == 'g') return "Gravity";
            if (ch == 'R') return "Rock";
            if (ch == 'T') return "Toggle";
            if (ch == 'L') return "Latch";
            if (ch == ':') return "Pulse";
            if (ch == 'u') return "Updraft";
            if (ch == '[') return "Cage";
            if (ch == '@') return "Deflector";
            if (ch == '-') return "Sand";
            if (ch == 'Y') return "Magnet";
            if (ch == ';') return "Sticky";
            if (ch == '!') return "Swap";
            if (ch == 'B') return "Button";
            if (ch == 'W') return "Plate";
            if (ch == 'H') return "HeavyGate";
            return "Gate";
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

            // A background camera used ONLY during the level-1 cinematic. The tutorial redirects the
            // main camera into a RenderTexture (the panel's video); while that is happening no camera
            // would be left drawing to the display ("No cameras rendering"), so this one clears the
            // screen to dark behind the panel. It renders nothing but its clear (cullingMask 0) and is
            // disabled outside the cinematic, so normal play stays single-camera and unchanged.
            var bgCamGO = new GameObject("TutorialBgCamera");
            var bgCam = bgCamGO.AddComponent<Camera>();
            bgCam.orthographic = true;
            bgCam.clearFlags = CameraClearFlags.SolidColor;
            bgCam.backgroundColor = Hex("03060C");
            bgCam.cullingMask = 0;
            bgCam.depth = -10;           // behind the main camera on the rare frame both are on
            bgCam.nearClipPlane = 0.1f;
            bgCam.farClipPlane = 10f;
            var bgUrp = bgCamGO.AddComponent<UniversalAdditionalCameraData>();
            bgUrp.renderPostProcessing = false;
            bgCam.enabled = false;       // GameManager switches it on for the cinematic only

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
            bd.glowAColors = new[] { WithAlpha(bdThemes[0].accent, 0f), WithAlpha(bdThemes[1].accent, 0f), WithAlpha(bdThemes[2].accent, 0f) };   // flat background — no nebula wash
            // secondary lower glow per depth — coastal turquoise, reef deep-blue, abyss faint teal.
            bd.glowBColors = new[] { WithAlpha(bdThemes[0].cardBase, 0f), WithAlpha(bdThemes[1].cardBase, 0f), WithAlpha(Hex("1E7AA8"), 0f) };
            bd.rayColors   = new[] { WithAlpha(Hex("BFF4FF"), 0f), WithAlpha(Hex("9FE0FF"), 0f), WithAlpha(Hex("6FD8E0"), 0f) };   // no wave shafts anywhere — clean, non-decorative background
            bd.vignetteAlpha = 0f;
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
            CreateMusicPlayer();

            var canvas = CreateCanvas("HUDCanvas");

            // clean centered header — title + moves/best, balanced with the board, off the edges.
            // both get a dark outline so they stay legible on any board colour or background photo.
            var levelLabel = MakeText(canvas, "LevelLabel", "Level 1", 34, Color.white,
                new Vector2(0, -56), new Vector2(1000, 46), FontStyle.Bold, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1));
            var levelOutline = levelLabel.gameObject.AddComponent<Outline>();
            levelOutline.effectColor = new Color(0f, 0.05f, 0.09f, 0.85f);
            levelOutline.effectDistance = new Vector2(2f, -2f);

            var movesLabel = MakeText(canvas, "MovesLabel", "MOVES  0", 24, Color.white,
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
            var bar = MakeRect(canvas, "ControlBar", new Vector2(0, 100), new Vector2(1400, 150),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f));

            // directional pad (left) — these MOVE the player
            var dsz = new Vector2(48, 48);
            var upBtn    = MakeFlatButton(bar, "BtnUp",    "▲", 24, new Vector2(-380, 34),  dsz, barAccent, spr);
            var downBtn  = MakeFlatButton(bar, "BtnDown",  "▼", 24, new Vector2(-380, -22), dsz, barAccent, spr);
            var leftBtn  = MakeFlatButton(bar, "BtnLeft",  "◀", 24, new Vector2(-436, 4),   dsz, barAccent, spr);
            var rightBtn = MakeFlatButton(bar, "BtnRight", "▶", 24, new Vector2(-324, 4),   dsz, barAccent, spr);
            MakeKeyHint(bar, "MOVE", new Vector2(-380, -64));

            // action buttons (right) — uniform labelled pills, wired to the real functions
            var asz = new Vector2(150, 56);
            var undoBtn    = MakeFlatButton(bar, "BtnUndo",    "UNDO",    22, new Vector2(-150, 2), asz, barAccent, spr);
            var restartBtn = MakeFlatButton(bar, "BtnRestart", "RESTART", 22, new Vector2(20, 2),   asz, barAccent, spr);
            var muteBtn    = MakeFlatButton(bar, "BtnMute",    null,      22, new Vector2(190, 2),  asz, barAccent, spr);
            var muteLbl    = MakeText(muteBtn.transform, "MuteLbl",  "MUTE",  22, Color.white,   Vector2.zero, asz, FontStyle.Bold);
            var mutedLbl   = MakeText(muteBtn.transform, "MutedLbl", "MUTED", 22, Hex("F2A6A6"), Vector2.zero, asz, FontStyle.Bold);
            mutedLbl.gameObject.SetActive(false);
            var menuHudBtn = MakeFlatButton(bar, "BtnMenu",    "LEVELS",  22, new Vector2(360, 2),  asz, barAccent, spr);

            // keycap hints under each action button — the keyboard key that triggers it
            MakeKeyHint(bar, "Z",   new Vector2(-150, -42));
            MakeKeyHint(bar, "R",   new Vector2(20, -42));
            MakeKeyHint(bar, "M",   new Vector2(190, -42));
            MakeKeyHint(bar, "ESC", new Vector2(360, -42));

            // on a seamless dive-in, reveal the HUD a beat after the board (fade + the control bar slides up)
            var hudGroup = canvas.gameObject.AddComponent<CanvasGroup>();
            var hudReveal = canvas.gameObject.AddComponent<HudReveal>();
            hudReveal.group = hudGroup;
            hudReveal.slide = bar;

            // ---- level-1 cinematic onboarding (raised video panel) -----------------------
            // The tutorial "video" is the real board rendered into a RenderTexture and shown inside a
            // premium raised card: a soft drop shadow + a polished frame, floating over a dimmed
            // background so it reads as elevated, with the video as the clear focus. Its OWN canvas
            // (above the HUD, own raycaster) so hiding the gameplay HUD never touches it. Built inert
            // (scrim + panel invisible, choice non-interactive) so levels 2-50 show nothing.
            var cineTf = CreateCanvas("CinematicCanvas");
            cineTf.GetComponent<Canvas>().sortingOrder = 100;
            var cine = cineTf;

            // dimmed background — its own CanvasGroup so only the scrim fades, not the panel on top
            var scrimRT = MakeRect(cine, "Scrim", Vector2.zero, Vector2.zero,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var scrimImg = scrimRT.gameObject.AddComponent<Image>();
            scrimImg.color = Hex("03060C");
            scrimImg.raycastTarget = false;   // the dim background must never absorb a click
            // a soft lit centre behind the card so the dark background reads as depth, not flat black
            UIImage(scrimRT, "BgGlow", spr.glow, WithAlpha(Hex("123648"), 0.55f), new Vector2(0, 60), new Vector2(1500, 1000));
            var scrimGroup = scrimRT.gameObject.AddComponent<CanvasGroup>();
            scrimGroup.alpha = 0f;

            // the raised card
            const float cardW = 1100f, cardH = 620f, vidW = 1040f, vidH = 585f;   // video is 16:9
            var videoPanelRT = MakeRect(cine, "VideoPanel", new Vector2(0, 50), new Vector2(cardW, cardH));
            var panelGroup = videoPanelRT.gameObject.AddComponent<CanvasGroup>();
            panelGroup.alpha = 0f;
            // soft drop shadow (elevation)
            UIImage(videoPanelRT, "Shadow", spr.glow, WithAlpha(Hex("00030A"), 0.55f), new Vector2(0, -22), new Vector2(cardW + 220f, cardH + 220f));
            // card body
            var cardImg = UIImage(videoPanelRT, "Card", spr.fill, Hex("0C2036"), Vector2.zero, new Vector2(cardW, cardH));
            cardImg.type = Image.Type.Sliced;
            // the video (RenderTexture assigned at runtime; white so the texture shows untinted)
            var vidRT = MakeRect(videoPanelRT, "Video", Vector2.zero, new Vector2(vidW, vidH));
            var videoImg = vidRT.gameObject.AddComponent<RawImage>();
            videoImg.color = Color.white;
            // polished frame: a bright inner edge + a soft outer ring for depth
            AddRing(videoPanelRT, "Frame", new Vector2(vidW + 10f, vidH + 10f), spr.ringThin, WithAlpha(Hex("6FE4F2"), 0.28f));

            // one short line, seated in the card's lower strip
            var capRT = MakeRect(videoPanelRT, "CineCaption", new Vector2(0, -cardH * 0.5f + 52f), new Vector2(vidW - 40f, 44),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            var capGroup = capRT.gameObject.AddComponent<CanvasGroup>();
            capGroup.alpha = 0f; capGroup.blocksRaycasts = false;
            var capText = AddTextShadow(MakeText(capRT, "CaptionText", "", 28, Hex("EAFBFF"),
                Vector2.zero, new Vector2(vidW - 40f, 44), FontStyle.Bold));
            var capOutline = capText.gameObject.AddComponent<Outline>();
            capOutline.effectColor = new Color(0f, 0.05f, 0.09f, 0.9f);
            capOutline.effectDistance = new Vector2(2f, -2f);

            // the three options, below the card
            var choiceRT = MakeRect(cine, "CineChoice", new Vector2(0, -410), new Vector2(900, 120),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            var choiceGroup = choiceRT.gameObject.AddComponent<CanvasGroup>();
            choiceGroup.alpha = 0f; choiceGroup.interactable = false; choiceGroup.blocksRaycasts = false;
            var againBtn = MakeButton(choiceRT, "TutAgain", "Watch Again", new Vector2(-290, 0), new Vector2(232, 64), 21, Hex("2A5570"));
            MenuButtonGlow(choiceRT, new Vector2(0, 0), new Vector2(272, 80), Hex("46D8C0"), spr);
            var tryBtn = MakeButton(choiceRT, "TutTry", "Try It Yourself", new Vector2(0, 0), new Vector2(272, 80), 25, Hex("4CE2C8"), Hex("2AA890"));
            var skipBtn = MakeButton(choiceRT, "TutSkip", "Skip Tutorial", new Vector2(290, 0), new Vector2(232, 64), 21, Hex("24405C"));

            var tutFx = cine.gameObject.AddComponent<TutorialFx>();
            tutFx.scrimGroup = scrimGroup;
            tutFx.panelGroup = panelGroup;
            tutFx.panelRT = videoPanelRT;
            tutFx.videoImage = videoImg;
            tutFx.captionGroup = capGroup;
            tutFx.captionText = capText;
            tutFx.choiceGroup = choiceGroup;
            tutFx.choiceRT = choiceRT;
            tutFx.againButton = againBtn;
            tutFx.tryButton = tryBtn;
            tutFx.skipButton = skipBtn;

            // win panel — premium "Level Complete" celebration
            var panelGO = new GameObject("WinPanel", typeof(RectTransform));
            panelGO.transform.SetParent(canvas, false);
            var panelRT = (RectTransform)panelGO.transform;
            panelRT.anchorMin = Vector2.zero; panelRT.anchorMax = Vector2.one; panelRT.sizeDelta = Vector2.zero;
            panelGO.AddComponent<Image>().color = new Color(0.01f, 0.03f, 0.06f, 0.88f);   // dims the board so the panel pops

            // celebration glow: a big soft burst + a tighter bright halo, both gently pulsing
            var winBurst = UIImage(panelGO.transform, "WinBurst", spr.glow, WithAlpha(Hex("46E0D8"), 0.5f), Vector2.zero, new Vector2(1180, 860));
            winBurst.gameObject.AddComponent<UIPulse>();
            var winHalo = UIImage(panelGO.transform, "WinHalo", spr.glow, WithAlpha(Hex("7FF2E6"), 0.4f), Vector2.zero, new Vector2(760, 560));
            winHalo.gameObject.AddComponent<UIPulse>();

            // subtle ambient light motes drifting behind the panel — low density, low opacity, fade in/out
            var winAmbRT = MakeRect(panelGO.transform, "WinParticles", Vector2.zero, Vector2.zero,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var winAmb = winAmbRT.gameObject.AddComponent<UIAmbient>();
            winAmb.sprite = spr.glow;
            winAmb.palette = new[] { Hex("7FF2E6"), Hex("A9F0F0"), Hex("FFFFFF"), Hex("62ECE0") };
            winAmb.count = 20;
            winAmb.sizeRange = new Vector2(5f, 16f);
            winAmb.riseSpeed = 11f;   // slow drift
            winAmb.sway = 22f;
            winAmb.baseAlpha = 0.22f; // very low opacity

            // one-shot celebration burst — glowing dots, star sparkles & light streaks fly outward on win
            var winFxRT = MakeRect(panelGO.transform, "WinBurstFx", Vector2.zero, Vector2.zero,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var winFx = winFxRT.gameObject.AddComponent<UIBurst>();
            winFx.glowSprite = spr.glow;
            winFx.sparkSprite = spr.star;
            winFx.palette = new[] { Hex("62ECE0"), Hex("46E0D8"), Hex("58B8F0"), Hex("BFF4FF"), Hex("FFFFFF") };
            winFx.count = 28;

            var windowRT = MakeRect(panelGO.transform, "Window", Vector2.zero, new Vector2(600, 384));
            var windowImg = windowRT.gameObject.AddComponent<Image>();
            windowImg.sprite = spr.fill; windowImg.type = Image.Type.Sliced; windowImg.color = Hex("0C2740");
            windowRT.gameObject.AddComponent<UIPopIn>(); // pops in when the win panel shows
            AddRing(windowRT, "WinOutline", new Vector2(600, 384), spr.ringThin, WithAlpha(Hex("62ECE0"), 1f));
            AddRing(windowRT, "WinOutline2", new Vector2(624, 408), spr.ringThin, WithAlpha(Hex("62ECE0"), 0.4f));

            // three premium layered stars (warm glow halo + gold star + white shine) that cascade in on win
            for (int i = 0; i < 3; i++)
            {
                var starRT = MakeRect(windowRT, "WinStar" + i, new Vector2((i - 1) * 92f, 122f), new Vector2(92, 92));
                UIImage(starRT, "Glow", spr.glow, WithAlpha(Hex("FFE7A6"), 0.6f), Vector2.zero, new Vector2(126, 126));
                UIImage(starRT, "Star", spr.star, Hex("FFC838"), Vector2.zero, new Vector2(88, 88));
                UIImage(starRT, "Shine", spr.star, WithAlpha(Hex("FFF7DC"), 0.92f), new Vector2(0, 5), new Vector2(46, 46));
                starRT.gameObject.AddComponent<StarPop>().delay = 0.16f + i * 0.14f;
            }

            var winTitle = MakeText(windowRT, "WinTitle", "LEVEL COMPLETE", 48, Hex("EAFBFF"),
                new Vector2(0, 52f), new Vector2(560, 60), FontStyle.Bold);
            var winTitleOutline = winTitle.gameObject.AddComponent<Outline>();
            winTitleOutline.effectColor = new Color(0f, 0.14f, 0.20f, 0.9f); winTitleOutline.effectDistance = new Vector2(2f, -2f);

            var winStats = MakeText(windowRT, "WinStats", "Solved in 0 moves", 24, Hex("C4ECF4"),
                new Vector2(0, 2f), new Vector2(560, 40));

            // NEXT LEVEL — bright primary button (glowing); MENU — secondary
            MenuButtonGlow(windowRT, new Vector2(-134, -108), new Vector2(252, 76), Hex("46D8C0"), spr);
            var nextBtn = MakeButton(windowRT, "NextButton", "NEXT LEVEL", new Vector2(-134, -108), new Vector2(252, 76), 26, Hex("4CE2C8"), Hex("2AA890"));
            var menuBtn = MakeButton(windowRT, "MenuButton", "LEVELS", new Vector2(140, -108), new Vector2(206, 68), 24, ButtonCol);

            panelGO.SetActive(false);

            // time-up panel (shown when the countdown hits zero)
            // ---- lose sequence -------------------------------------------------------------
            // Deliberately NOT a window on a scrim. The old panel was a 560x340 grey card that
            // popped in instantly, which reads as a dialog thrown over the game. Here the verdict
            // and the options sit directly on the dimmed board, and LoseFx times them in.
            var tuGO = new GameObject("LosePanel", typeof(RectTransform));
            tuGO.transform.SetParent(canvas, false);
            var tuRT = (RectTransform)tuGO.transform;
            tuRT.anchorMin = Vector2.zero; tuRT.anchorMax = Vector2.one; tuRT.sizeDelta = Vector2.zero;

            // full-screen dim on its own group, so the board can drain independently of the text
            var dimRT = MakeRect(tuGO.transform, "Dim", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var dimImg = dimRT.gameObject.AddComponent<Image>();
            dimImg.color = Hex("03080F");
            var dimCG = dimRT.gameObject.AddComponent<CanvasGroup>();

            // the verdict — text set per failure kind at runtime ("OUT OF MOVES" / "TIME'S UP")
            var titleHolder = MakeRect(tuGO.transform, "TitleGroup", new Vector2(0f, 40f), new Vector2(900f, 150f));
            var titleCG = titleHolder.gameObject.AddComponent<CanvasGroup>();
            var loseTitle = AddTextShadow(MakeText(titleHolder, "LoseTitle", "OUT OF MOVES", 62, Hex("FF6B6B"),
                new Vector2(0f, 26f), new Vector2(880f, 78f), FontStyle.Bold));
            // sits clear of BOTH the title above and the buttons below — 4px of air under a 62pt
            // title is not air, it's a collision waiting to be reported
            var loseSub = AddTextShadow(MakeText(titleHolder, "LoseSub", "", 24, Hex("A8C2D4"),
                new Vector2(0f, -46f), new Vector2(880f, 36f)));

            // particle burst behind the verdict — UIBurst fires the moment it's enabled
            var burstRT = MakeRect(tuGO.transform, "LoseBurst", new Vector2(0f, 40f), new Vector2(10f, 10f));
            var loseBurst = burstRT.gameObject.AddComponent<UIBurst>();
            loseBurst.glowSprite = spr.glow;
            loseBurst.sparkSprite = spr.star;
            loseBurst.palette = new[] { Hex("FF6B6B"), Hex("FF9E5E"), Hex("6E7A8A") };
            loseBurst.count = 22;
            loseBurst.speed = 520f;
            loseBurst.life = 1.0f;
            loseBurst.baseAlpha = 0.7f;
            burstRT.gameObject.SetActive(false);   // LoseFx enables it on the beat

            // Each option animates on its own timing, so they arrive one after the other.
            // Colours come from the game's OWN ocean palette rather than the generic room colours:
            // Restart was RoomColors[3] — a grass green — sitting on a dark ocean-navy scrim, which
            // belonged to no part of this game. Teal reads as the primary action against the navy;
            // the slate recedes without disappearing.
            var retryHolder = MakeRect(tuGO.transform, "RestartHolder", new Vector2(-124f, -132f), new Vector2(232f, 72f));
            var retryCG = retryHolder.gameObject.AddComponent<CanvasGroup>();
            var retryBtn = MakeButton(retryHolder, "RetryButton", "Restart", Vector2.zero, new Vector2(232f, 72f), 26, Hex("2F93AD"));

            var levelsHolder = MakeRect(tuGO.transform, "LevelsHolder", new Vector2(124f, -132f), new Vector2(232f, 72f));
            var levelsCG = levelsHolder.gameObject.AddComponent<CanvasGroup>();
            var toLevelsBtn = MakeButton(levelsHolder, "ToLevelsButton", "Level Select", Vector2.zero, new Vector2(232f, 72f), 26, Hex("2A5570"));

            // full-screen impact flash, ABOVE the dim but below the text
            var pulseRT = MakeRect(tuGO.transform, "Pulse", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var pulseImg = pulseRT.gameObject.AddComponent<Image>();
            pulseImg.color = new Color(1f, 0.30f, 0.28f, 0f);
            pulseImg.raycastTarget = false;
            pulseRT.SetSiblingIndex(1);   // straight after Dim

            var loseFx = tuGO.AddComponent<LoseFx>();
            loseFx.dim = dimCG;
            loseFx.pulse = pulseImg;
            loseFx.titleGroup = titleCG;
            loseFx.titleRT = titleHolder;
            loseFx.titleText = loseTitle;
            loseFx.subText = loseSub;
            loseFx.burst = burstRT.gameObject;
            loseFx.restartGroup = retryCG;
            loseFx.restartRT = retryHolder;
            loseFx.levelsGroup = levelsCG;
            loseFx.levelsRT = levelsHolder;
            loseFx.shakeTarget = follow;   // the board jolts via the gameplay camera
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
            gm.cellSprite = spr.cell;
            gm.cameraFollow = follow;
            gm.tutorialBgCamera = bgCam;
            gm.levelLabel = levelLabel;
            gm.movesLabel = movesLabel;
            gm.tutorialFx = tutFx;
            gm.hudGroup = hudGroup;
            gm.hudReveal = hudReveal;
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
            // All tiers use ONE board skin that matches the title screen's O board:
            // blue gradient-ish floor, bright cyan neon frame, orange box / yellow player.
            gm.levelThemes = ChapterThemes();   // 3 evolving chapters (Levels 1-10 / 11-20 / 21-30)
            gm.timeUpPanel = tuGO;
            gm.retryButton = retryBtn;
            gm.backToLevelsButton = toLevelsBtn;
            gm.loseFx = loseFx;

            // on-screen control bar
            gm.upButton = upBtn; gm.downButton = downBtn; gm.leftButton = leftBtn; gm.rightButton = rightBtn;
            gm.undoButton = undoBtn; gm.restartButton = restartBtn; gm.muteButton = muteBtn; gm.hudMenuButton = menuHudBtn;
            gm.muteOnIcon = muteLbl.gameObject; gm.muteOffIcon = mutedLbl.gameObject;

            var gameFade = CreateFade(canvas, Hex("2A5E92"));   // game emerges from the board-blue the O zoomed into
            gm.screenFade = gameFade;   // ...and fades back OUT to it when the level is done

            string path = SceneDir + "/Game.unity";
            EditorSceneManager.SaveScene(scene, path);
            return path;
        }

        // Three evolving board chapters (Levels 1-10 / 11-20 / 21-30): bright clean intro → darker richer
        // journey → deepest premium master challenge. Same ocean art direction (cyan frame, warm pieces,
        // square-cell grid) with monotonically rising floor-depth, cell contrast, lighting, and border
        // identity. Palettes were contrast-verified (piece/cell/frame/wall readability + progression).
        // Five regions, one continuous descent.
        //
        // The FRAME is each chapter's identity: it paints the board's rim in-game and, on the map,
        // every node border, the current-level ring, the path dots, the zone wash, the chapter
        // label and the gate. Get it wrong and a whole chapter has no colour of its own.
        //
        // It was wrong. Chapters 1-3 sat at 181-188 degrees of hue — three shades of the same cyan —
        // so with every node beaten (orange, spread over a mere 12 degrees) the first three chapters
        // were literally indistinguishable. The hues now step 168 -> 196 -> 224 -> 262 -> 302:
        // 28 degrees minimum between any two chapters, monotonic, so the descent reads as a journey
        // from green-cyan shallows to magenta endgame. Every frame is >= 3:1 against its own tile
        // (bought with brightness, not by desaturating into pastel).
        static LevelTheme[] ChapterThemes() => new[]
        {
            // 1 — Easy. Sunlit shallows: simple and welcoming.
            new LevelTheme {
                roomColors = new[] { Hex("2B79B4"), Hex("3488C4"), Hex("24689C"), Hex("3E96D2"), Hex("2F7FBA") },
                gutter = Hex("11304A"),
                box = Hex("F5A24E"), player = Hex("F6C453"), wall = Hex("0A1E30"), frame = Hex("0CF0C2"),
                grid = new Color(0f, 0f, 0f, 0.09f), floorVignette = 0.05f, pieceGlow = 0f, cellLift = 0.12f,
                floorTexTint = Color.clear,
            },
            // 2 — Medium. Deeper water: more adventurous.
            new LevelTheme {
                roomColors = new[] { Hex("226B9E"), Hex("2A7AB0"), Hex("1C5C88"), Hex("3389C0"), Hex("2470A4") },
                gutter = Hex("0C2438"),
                box = Hex("F0812E"), player = Hex("F8C13C"), wall = Hex("07182A"), frame = Hex("40CCFF"),
                grid = new Color(0f, 0f, 0f, 0.10f), floorVignette = 0.08f, pieceGlow = 0f, cellLift = 0.14f,
                floorTexTint = Color.clear,
            },
            // 3 — Hard. The abyss: challenging and important.
            new LevelTheme {
                roomColors = new[] { Hex("1A5578"), Hex("216488"), Hex("154868"), Hex("2A7398"), Hex("1D5C80") },
                gutter = Hex("071A28"),
                box = Hex("EF7A22"), player = Hex("FBC532"), wall = Hex("04101C"), frame = Hex("7D9EFA"),
                grid = new Color(0f, 0f, 0f, 0.11f), floorVignette = 0.11f, pieceGlow = 0f, cellLift = 0.16f,
                floorTexTint = Color.clear,
            },
            // 4 — Expert. Trench: colder, more prestigious. The violet is the first hue shift in
            // the whole game, so arriving here reads as leaving the ocean you knew.
            new LevelTheme {
                roomColors = new[] { Hex("263C7A"), Hex("2E4790"), Hex("1E3164"), Hex("38539E"), Hex("2A4386") },
                gutter = Hex("0A1230"),
                box = Hex("F2681E"), player = Hex("FFD34A"), wall = Hex("050818"), frame = Hex("A370FA"),
                grid = new Color(0f, 0f, 0f, 0.12f), floorVignette = 0.14f, pieceGlow = 0f, cellLift = 0.17f,
                floorTexTint = Color.clear,
            },
            // 5 — Master. The endgame: near-black, and the only fully saturated frame in the game.
            new LevelTheme {
                roomColors = new[] { Hex("2A2454"), Hex("332B66"), Hex("221D46"), Hex("3D3378"), Hex("2E2860") },
                gutter = Hex("06040F"),
                box = Hex("FF5E1A"), player = Hex("FFE066"), wall = Hex("030208"), frame = Hex("D10ACA"),
                grid = new Color(0f, 0f, 0f, 0.14f), floorVignette = 0.18f, pieceGlow = 0f, cellLift = 0.19f,
                floorTexTint = Color.clear,
            },
        };

        static string CreateMainMenuScene(Sprites spr, GameObject[] levels, Tiles t)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            int levelCount = levels.Length;

            var menuCam = CreateCamera(true);   // orthographic — it physically flies into the real board on Start
            CreateEventSystem();
            CreateMusicPlayer();

            // world-space backdrop behind the board (same look as the game); a menu-only copy so the game scene is untouched
            var backdrop = BuildMenuBackdrop(menuCam, spr);

            var canvas = CreateCanvas("MenuCanvas");
            var ui = canvas.gameObject.AddComponent<MainMenuUI>();

            // the drifting squares sit in their OWN group, behind everything: the home text has to hide
            // when the level board opens, but this ambience should survive it.
            var bgRoot = MakeRect(canvas, "BackgroundRoot", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var bgGroup = bgRoot.gameObject.AddComponent<CanvasGroup>();
            bgGroup.blocksRaycasts = false;

            // all home UI lives under a fade-able group so it dissolves as the camera dives into the board
            var homeRoot = MakeRect(canvas, "HomeRoot", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var homeGroup = homeRoot.gameObject.AddComponent<CanvasGroup>();
            ui.menuCam = menuCam;
            ui.backdrop = backdrop;
            ui.homeGroup = homeGroup;
            ui.backgroundGroup = bgGroup;

            // the real board's inputs — same prefabs / sprites / theme the game uses → identical render
            ui.levelPrefabs = levels;
            ui.floorPrefab = t.floor; ui.gridPrefab = t.grid; ui.wallPrefab = t.wall;
            ui.boxPrefab = t.box; ui.metaBoxPrefab = t.metaBox; ui.playerPrefab = t.player;
            ui.boxGoalPrefab = t.boxGoal; ui.playerGoalPrefab = t.playerGoal;
            ui.fxRing = spr.ringThick; ui.fxGlow = spr.glow; ui.fxVignette = spr.vignette; ui.fxCell = spr.cell;
            ui.boardThemes = ChapterThemes();

            BuildMenuBackground(bgRoot, spr);

            // ============================================ HOME screen (under homeRoot → fades during the dive)
            // "HAYOT'S" kicker above the logo
            AddTextShadow(MakeText(homeRoot, "Kicker", "H A Y O T ' S", 42, Hex("EAFBFF"),
                new Vector2(0, 332), new Vector2(1200, 60), FontStyle.Bold));

            // title logo — "PARAB[board]X": the O is a TRANSPARENT slot the real world board is framed into
            var titleGlow = UIImage(homeRoot, "TitleGlow", spr.glow, WithAlpha(Hex("38C0D8"), 0.42f), new Vector2(0, 205), new Vector2(1300, 560));
            var tgPulse = titleGlow.gameObject.AddComponent<UIPulse>();
            tgPulse.amplitude = 0.06f; tgPulse.speed = 1.4f;

            // auto-centered horizontal row so we don't have to guess the font's letter widths
            var titleRow = MakeRect(homeRoot, "TitleRow", new Vector2(0, 205), new Vector2(1200, 170));
            var hlg = titleRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.spacing = 4f;
            hlg.childControlWidth = true; hlg.childControlHeight = false;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
            var fitter = titleRow.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            TitleWord(titleRow, "TitlePARAB", "PARAB");
            var oSlotRT = MakeTitleBoardO(titleRow, spr, out var boardInner, out var oRingRT);   // transparent O slot; the world board is framed into it
            ui.boardInner = boardInner;
            ui.oSlot = oSlotRT;
            ui.oRing = oRingRT;
            TitleWord(titleRow, "TitleX", "X");

            // credits, like the reference
            AddTextShadow(MakeText(homeRoot, "Credit1", "a game by Nurhayot", 28, Hex("D6E8F4"),
                new Vector2(0, 74), new Vector2(1100, 38), FontStyle.Bold));
            // big keycap-style buttons — Start game / Menu (clickable + keyboard)
            ui.playButton   = MakePrompt(homeRoot, "PlayPrompt", "Start game", new Vector2(-135, -150), new Vector2(300, 92), spr);
            // "Levels", not "Menu": it opens the progression map. The field has always been called
            // levelsButton and has always called OpenLevelBoard — only the label was lying.
            ui.levelsButton = MakePrompt(homeRoot, "LevelsPrompt", "Levels",   new Vector2(185, -150),  new Vector2(200, 92), spr);
            // no Quit on the title screen (matches the reference); MainMenuUI guards the null.

            BuildProgressionMap(canvas, spr, ui, levelCount);

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

        // ================================================= the progression map
        // Not a grid, and not a chapter list: ONE continuous route that snakes from level 1 to
        // level 30. Chapter 1 runs left→right, chapter 2 turns and runs right→left, chapter 3
        // turns again and runs to the final challenge — so the eye actually travels, and the
        // chapters read as legs of a journey rather than three separate shelves.
        //
        // The route itself is the progress bar: it's drawn as a trail of dots sampled along each
        // chapter's curve, and a leg only lights up once the level behind it is cleared. Your
        // progress IS the lit path.
        //
        // Each chapter's curve has its own identity — the wave grows in amplitude and frequency
        // as you descend (calm arc → 1.5 waves → a wilder double wave).
        static void BuildProgressionMap(Transform canvas, Sprites spr, MainMenuUI ui, int levelCount)
        {
            // 5 rows have to share the same canvas 3 used to. Measured: node 84 needs 172px of
            // amplitude clearance and overruns the Back button; node 72 over an 860px span leaves
            // 28px between rows, 16px above Back, and 103px between the closest two nodes anywhere.
            const float nodeSize = 72f, halfSpan = 720f, dotSize = 8f;
            const float rowSpan = 860f;
            const int dotsPerLink = 7;

            var themes = ChapterThemes();
            int per = MainMenuUI.PerCategory;
            int chapters = Mathf.Max(1, Mathf.CeilToInt(levelCount / (float)per));
            float rowGap = rowSpan / Mathf.Max(1, chapters);
            var baseY = new float[chapters];
            for (int c = 0; c < chapters; c++) baseY[c] = rowSpan * 0.5f - rowGap * 0.5f - c * rowGap;
            string[] chapterName = { "CHAPTER I", "CHAPTER II", "CHAPTER III", "CHAPTER IV", "CHAPTER V" };

            ui.levelButtons    = new Button[levelCount];
            ui.levelFills      = new Image[levelCount];
            ui.levelBorders    = new Image[levelCount];
            ui.levelNumbers    = new Text[levelCount];
            ui.levelChecks     = new GameObject[levelCount];
            ui.levelLocks      = new GameObject[levelCount];
            ui.levelHighlights = new GameObject[levelCount];
            ui.levelStars      = new GameObject[levelCount];
            ui.pathDots        = new Image[Mathf.Max(0, (levelCount - 1) * dotsPerLink)];
            ui.pathDotsPerLink = dotsPerLink;

            // a quiet scrim: the map floats in the world, it isn't a panel pasted over it
            var panel = MakeOverlay(canvas, "LevelBoardPanel", WithAlpha(Hex("03080F"), 0.90f), out var screen);
            ui.levelBoardScreen = screen;

            // Each chapter is a REGION: its own zone of colour, its own group, its own arrival.
            // A region owns its stretch of route, its nodes and its label, so the three chapters
            // read as places you travel between rather than three settings of one graph.
            // The map camera. Every region hangs off this one rect, so scaling and offsetting it
            // IS a camera push/pan — no second Unity camera, no render texture. The Back button
            // stays outside it so the UI never travels with the world.
            var mapCam = MakeRect(panel, "MapCam", Vector2.zero, new Vector2(1920f, 1080f));
            ui.mapCam = mapCam;

            var reveal = panel.gameObject.AddComponent<MapReveal>();
            reveal.regions = new CanvasGroup[chapters];
            reveal.regionRTs = new RectTransform[chapters];

            var regionRoot = new RectTransform[chapters];
            var pathLayers = new Transform[chapters];
            var glowLayers = new Transform[chapters];
            var nodeLayers = new Transform[chapters];
            for (int c = 0; c < chapters; c++)
            {
                var th = themes[Mathf.Clamp(c, 0, themes.Length - 1)];
                var rr = MakeRect(mapCam, "Region" + c, Vector2.zero, new Vector2(1920f, 1080f));
                regionRoot[c] = rr;
                reveal.regions[c] = rr.gameObject.AddComponent<CanvasGroup>();
                reveal.regionRTs[c] = rr;

                // the zone wash — a soft field of this chapter's colour behind its leg. It grows
                // stronger as you descend, so the final region is the most present on screen.
                UIImage(rr, "Zone", spr.glow, WithAlpha(th.frame, 0.05f + c * 0.025f),
                    new Vector2(0f, baseY[Mathf.Clamp(c, 0, baseY.Length - 1)]), new Vector2(2000f, 460f));

                // draw order within a region: trail underneath, then hover glows, then nodes
                pathLayers[c] = MakeRect(rr, "Route", Vector2.zero, new Vector2(1920f, 1080f));
                glowLayers[c] = MakeRect(rr, "Glows", Vector2.zero, new Vector2(1920f, 1080f));
                nodeLayers[c] = MakeRect(rr, "Nodes", Vector2.zero, new Vector2(1920f, 1080f));
            }

            // ---- the route -------------------------------------------------------------
            for (int i = 0; i + 1 < levelCount; i++)
            {
                int ca = i / per, cb = (i + 1) / per;
                var th = themes[Mathf.Clamp(cb, 0, themes.Length - 1)];
                for (int d = 0; d < dotsPerLink; d++)
                {
                    float f = (d + 1f) / (dotsPerLink + 1f);
                    Vector2 p;
                    if (ca == cb)
                    {
                        // walk along this chapter's own curve, so the trail hugs the route
                        p = MapPos(ca, (i % per) + f, per, baseY[Mathf.Clamp(ca, 0, baseY.Length - 1)], halfSpan);
                    }
                    else
                    {
                        // the turn between chapters: bow it outward so the route visibly bends
                        // around, instead of dropping in a dead straight line
                        Vector2 a = MapPos(ca, per - 1, per, baseY[Mathf.Clamp(ca, 0, baseY.Length - 1)], halfSpan);
                        Vector2 b = MapPos(cb, 0, per, baseY[Mathf.Clamp(cb, 0, baseY.Length - 1)], halfSpan);
                        Vector2 ctrl = (a + b) * 0.5f + new Vector2(Mathf.Sign(a.x) * 90f, 0f);
                        p = Bezier(a, ctrl, b, f);
                    }
                    var dot = UIImage(pathLayers[Mathf.Clamp(cb, 0, chapters - 1)], "Dot" + i + "_" + d,
                        spr.disc, WithAlpha(th.frame, 0.12f), p, new Vector2(dotSize, dotSize));
                    ui.pathDots[i * dotsPerLink + d] = dot;
                }
            }

            // ---- chapter labels, sitting at each leg's starting node --------------------
            for (int c = 0; c < chapters; c++)
            {
                var th = themes[Mathf.Clamp(c, 0, themes.Length - 1)];
                Vector2 start = MapPos(c, 0, per, baseY[Mathf.Clamp(c, 0, baseY.Length - 1)], halfSpan);
                bool rev = (c % 2) == 1;
                AddTextShadow(MakeText(regionRoot[c], "ChName" + c, chapterName[Mathf.Clamp(c, 0, chapterName.Length - 1)],
                    20, WithAlpha(Lighten(th.frame, 0.25f), 0.9f),
                    start + new Vector2(rev ? -110f : 110f, 76f), new Vector2(220f, 30f),
                    FontStyle.Bold, rev ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft));
            }

            // ---- the nodes -------------------------------------------------------------
            for (int i = 0; i < levelCount; i++)
            {
                int c = i / per;
                var th = themes[Mathf.Clamp(c, 0, themes.Length - 1)];
                Vector2 p = MapPos(c, i % per, per, baseY[Mathf.Clamp(c, 0, baseY.Length - 1)], halfSpan);
                // the last level is the destination — it earns extra presence
                float size = (i == levelCount - 1) ? nodeSize * 1.22f : nodeSize;
                bool isFinal = i == levelCount - 1;

                // The destination is marked BEFORE you can reach it: a standing gold aura burning
                // at the end of the route from the first time you open the map. Every other node
                // is a stop; this one is the thing you are walking toward.
                if (isFinal)
                    UIImage(glowLayers[Mathf.Clamp(c, 0, chapters - 1)], "FinalAura", spr.glow,
                        WithAlpha(th.player, 0.42f), p, new Vector2(size * 2.4f, size * 2.4f))
                        .gameObject.AddComponent<UIPulse>();

                ui.levelButtons[i] = MakeMapNode(nodeLayers[Mathf.Clamp(c, 0, chapters - 1)],
                    glowLayers[Mathf.Clamp(c, 0, chapters - 1)], "Level" + (i + 1), i + 1, th, spr, p, size,
                    out ui.levelFills[i], out ui.levelBorders[i], out ui.levelNumbers[i],
                    out ui.levelLocks[i], out ui.levelChecks[i], out ui.levelHighlights[i], out ui.levelStars[i]);

                if (isFinal)
                {
                    // gold outer ring + a label, so it reads as the final challenge even at a glance
                    AddRing(ui.levelButtons[i].transform, "FinalRing",
                        new Vector2(size + 18f, size + 18f), spr.cellRing, th.player);
                    AddTextShadow(MakeText(ui.levelButtons[i].transform, "FinalLabel", "FINAL", 15,
                        WithAlpha(Lighten(th.player, 0.25f), 0.95f),
                        new Vector2(0f, -size * 0.5f - 18f), new Vector2(160f, 22f), FontStyle.Bold));
                }
            }

            // ---- the gates -------------------------------------------------------------
            // A shut barrier straddling the route at each chapter boundary. It is visible from the
            // first time you open the map, so by the time it breaks it has been in your way for
            // ten levels — you cannot celebrate removing an obstacle nobody knew existed.
            ui.chapterGates = new GameObject[chapters];
            ui.gateLeft = new RectTransform[chapters];
            ui.gateRight = new RectTransform[chapters];
            ui.gateLocks = new RectTransform[chapters];
            for (int c = 1; c < chapters; c++)
            {
                var th = themes[Mathf.Clamp(c, 0, themes.Length - 1)];
                Vector2 a = MapPos(c - 1, per - 1, per, baseY[Mathf.Clamp(c - 1, 0, baseY.Length - 1)], halfSpan);
                Vector2 b = MapPos(c, 0, per, baseY[Mathf.Clamp(c, 0, baseY.Length - 1)], halfSpan);
                // the route bows outward on the hand-off, so meet it where it actually is
                Vector2 mid = (a + b) * 0.5f + new Vector2(Mathf.Sign(a.x) * 45f, 0f);

                var gate = MakeRect(mapCam, "Gate" + c, mid, new Vector2(240f, 60f));
                ui.chapterGates[c] = gate.gameObject;

                // the route here runs vertically, so the barrier lies across it
                var gl = MakeRect(gate, "GateL", new Vector2(-58f, 0f), new Vector2(112f, 26f));
                var glImg = gl.gameObject.AddComponent<Image>();
                glImg.sprite = spr.fill; glImg.type = Image.Type.Sliced;
                glImg.color = Darken(th.frame, 0.55f); glImg.raycastTarget = false;
                AddRing(gl, "GLEdge", new Vector2(112f, 26f), spr.ringThin, WithAlpha(th.frame, 0.7f));
                ui.gateLeft[c] = gl;

                var gr = MakeRect(gate, "GateR", new Vector2(58f, 0f), new Vector2(112f, 26f));
                var grImg = gr.gameObject.AddComponent<Image>();
                grImg.sprite = spr.fill; grImg.type = Image.Type.Sliced;
                grImg.color = Darken(th.frame, 0.55f); grImg.raycastTarget = false;
                AddRing(gr, "GREdge", new Vector2(112f, 26f), spr.ringThin, WithAlpha(th.frame, 0.7f));
                ui.gateRight[c] = gr;

                var lk = MakeRect(gate, "GateLock", Vector2.zero, new Vector2(44f, 44f));
                UIImage(lk, "GateLockGlow", spr.glow, WithAlpha(th.frame, 0.45f), Vector2.zero, new Vector2(76f, 76f));
                UIImage(lk, "GateLockIcon", spr.lockIcon, Lighten(th.frame, 0.35f), Vector2.zero, new Vector2(38f, 38f));
                ui.gateLocks[c] = lk;
            }

            ui.progressFx = panel.gameObject.AddComponent<MapProgressFx>();
            ui.progressFx.burstGlow = spr.glow;
            ui.progressFx.burstSpark = spr.star;
            ui.progressFx.ringSprite = spr.cellRing;   // same ring the nodes wear, so it reads as the node itself leaving
            ui.unlockFx = panel.gameObject.AddComponent<ChapterUnlockFx>();
            ui.unlockFx.burstGlow = spr.glow;
            ui.unlockFx.burstSpark = spr.star;
            ui.unlockFx.ringSprite = spr.cellRing;
            ui.regionBaseY = baseY;

            // ================================================= the victory screen
            // The artifact. Four endings were built before it existed — all of them board effects,
            // which vanish the moment they finish. This is the thing that is still there when the
            // motion stops, and it sits over your completed route rather than replacing it.
            var gold = themes[chapters - 1].player;
            var fin = MakeRect(panel, "FinaleBanner", Vector2.zero, new Vector2(1920f, 1080f));
            var finBG = fin.gameObject.AddComponent<Image>();
            finBG.color = WithAlpha(Hex("03080F"), 0.72f);
            ui.finaleBanner = fin.gameObject.AddComponent<CanvasGroup>();

            // the trophy: a box, inside a box, inside a box — this game's shape, not a stock cup
            var badge = MakeRect(fin, "Badge", new Vector2(0f, 132f), new Vector2(190f, 190f));
            UIImage(badge, "BadgeGlow", spr.glow, WithAlpha(gold, 0.42f), Vector2.zero, new Vector2(400f, 400f))
                .gameObject.AddComponent<UIPulse>();
            // a ROUNDED-SQUARE frame, not a circle: the glyph inside is a square, and a square in a
            // circle puts its corners in a fight with the ring around it
            UIImage(badge, "BadgeRing", spr.cellRing, WithAlpha(gold, 0.38f), Vector2.zero, new Vector2(268f, 268f));
            UIImage(badge, "BadgeIcon", spr.trophy, gold, Vector2.zero, new Vector2(168f, 168f));
            ui.finaleBadge = badge;

            ui.finaleTitle = AddTextShadow(MakeText(fin, "FinaleTitle", "GAME COMPLETE", 64,
                Lighten(gold, 0.25f), new Vector2(0f, -14f), new Vector2(1200f, 84f), FontStyle.Bold));

            ui.finaleCount = AddTextShadow(MakeText(fin, "FinaleCount", "30 / 30 LEVELS COMPLETE", 30,
                Hex("EAF6FF"), new Vector2(0f, -76f), new Vector2(1200f, 40f), FontStyle.Bold));

            ui.finaleSub = AddTextShadow(MakeText(fin, "FinaleSub", "", 22, Hex("A8C2D4"),
                new Vector2(0f, -126f), new Vector2(1200f, 32f)));

            ui.finaleBanner.alpha = 0f;
            ui.finaleBanner.interactable = false;
            ui.finaleBanner.blocksRaycasts = false;

            ui.levelBoardBackButton = MakeButton(panel, "LBBack", "Back",
                new Vector2(0f, -466f), new Vector2(232f, 64f), 24, Hex("2A5570"));
            panel.gameObject.SetActive(false);
        }

        // A point on chapter c's route. k is the node index within the chapter and may be
        // fractional, which is how the trail samples the curve between two nodes.
        static Vector2 MapPos(int c, float k, int per, float baseY, float half)
        {
            float t = Mathf.Clamp01(k / (per - 1f));
            bool rev = (c % 2) == 1;                        // serpentine: every other leg reverses
            float x = -half + 2f * half * (rev ? 1f - t : t);
            // Tuned for 5 rows: at the old 30+14c a chapter-5 row swung 86px and collided with its
            // neighbours. 20+6c keeps the wander visible while every row stays in its lane.
            float amp = 20f + c * 6f;                       // later chapters wander further
            float freq = 1f + c * 0.4f;                     // ...and oscillate more
            float y = baseY + Mathf.Sin(t * freq * Mathf.PI * 2f) * amp;
            return new Vector2(x, y);
        }

        static Vector2 Bezier(Vector2 a, Vector2 ctrl, Vector2 b, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * ctrl + t * t * b;
        }

        // One stop on the route. Same construction for all thirty, so size, stroke weight and
        // type scale can't drift; only the final node is scaled up, as the destination.
        static Button MakeMapNode(Transform parent, Transform glowLayer, string name, int number,
                                  LevelTheme th, Sprites spr, Vector2 pos, float size,
                                  out Image fill, out Image border, out Text numberText,
                                  out GameObject lockGO, out GameObject checkGO, out GameObject highlightGO,
                                  out GameObject starGO)
        {
            var s = new Vector2(size, size);

            var hl = UIImage(glowLayer, name + "HL", spr.glow, WithAlpha(th.frame, 0.5f), pos, s + new Vector2(64f, 64f));
            hl.gameObject.SetActive(false);

            var rt = MakeRect(parent, name, pos, s);
            fill = rt.gameObject.AddComponent<Image>();
            fill.sprite = spr.cell;
            fill.type = Image.Type.Simple;
            fill.color = (th.roomColors != null && th.roomColors.Length > 0) ? th.roomColors[0] : th.frame;

            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = fill;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = Color.white;   // locked is painted explicitly, not tinted
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            var hover = rt.gameObject.AddComponent<UIHoverScale>();
            hover.hover = 1.12f;
            hover.highlight = hl.gameObject;

            border = AddRing(rt, "Border", s, spr.cellRing, WithAlpha(th.frame, 0.55f)).GetComponent<Image>();
            numberText = MakeText(rt, "Number", number.ToString(), Mathf.RoundToInt(size * 0.36f), th.wall,
                Vector2.zero, s, FontStyle.Bold);

            var hi = AddRing(rt, "Current", s + new Vector2(20f, 20f), spr.cellRing, th.frame);
            hi.gameObject.AddComponent<UIPulse>();
            highlightGO = hi.gameObject;
            highlightGO.SetActive(false);

            var chk = MakeRect(rt, "Check", new Vector2(size * 0.5f - 13f, size * 0.5f - 13f), new Vector2(26f, 26f));
            var chkImg = chk.gameObject.AddComponent<Image>();
            chkImg.sprite = spr.disc; chkImg.type = Image.Type.Simple;
            chkImg.color = th.wall; chkImg.raycastTarget = false;
            MakeText(chk, "Tick", "✓", 16, th.box, Vector2.zero, new Vector2(26f, 26f), FontStyle.Bold);
            checkGO = chk.gameObject;
            checkGO.SetActive(false);

            // perfect = solved at par. Opposite corner from the tick so both can show at once.
            var st = MakeRect(rt, "Perfect", new Vector2(-size * 0.5f + 13f, size * 0.5f - 13f), new Vector2(30f, 30f));
            UIImage(st, "PerfectGlow", spr.glow, WithAlpha(th.player, 0.75f), Vector2.zero, new Vector2(46f, 46f));
            UIImage(st, "PerfectStar", spr.star, th.player, Vector2.zero, new Vector2(26f, 26f));
            starGO = st.gameObject;
            starGO.SetActive(false);

            var lk = MakeRect(rt, "Lock", Vector2.zero, new Vector2(28f, 28f));
            UIImage(lk, "LockIcon", spr.lockIcon, Lighten(th.gutter, 0.45f), Vector2.zero, new Vector2(28f, 28f));
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
        // Menu-only copy of the game's world backdrop (a child of the menu camera), so the game scene stays untouched.
        static CameraBackdrop BuildMenuBackdrop(Camera cam, Sprites spr)
        {
            var backdrop = new GameObject("Backdrop");
            backdrop.transform.SetParent(cam.transform, false);
            backdrop.transform.localPosition = new Vector3(0f, 0f, 20f);
            var bd = backdrop.AddComponent<CameraBackdrop>();
            bd.cam = cam;
            bd.glow1 = MakeWorldSprite(backdrop.transform, "Glow1", spr.glow, -200);
            bd.glow2 = MakeWorldSprite(backdrop.transform, "Glow2", spr.glow, -199);
            bd.vignette = MakeWorldSprite(backdrop.transform, "Vignette", spr.vignette, -198);
            bd.rays = new[]
            {
                MakeWorldSprite(backdrop.transform, "Ray0", spr.godRay, -197),
                MakeWorldSprite(backdrop.transform, "Ray1", spr.godRay, -197),
                MakeWorldSprite(backdrop.transform, "Ray2", spr.godRay, -197),
            };
            var bdThemes = CategoryThemes();
            bd.baseColors  = new[] { bdThemes[0].bg, bdThemes[1].bg, bdThemes[2].bg };
            bd.glowAColors = new[] { WithAlpha(bdThemes[0].accent, 0f), WithAlpha(bdThemes[1].accent, 0f), WithAlpha(bdThemes[2].accent, 0f) };   // flat background — no nebula wash
            bd.glowBColors = new[] { WithAlpha(bdThemes[0].cardBase, 0f), WithAlpha(bdThemes[1].cardBase, 0f), WithAlpha(Hex("1E7AA8"), 0f) };
            bd.rayColors   = new[] { WithAlpha(Hex("BFF4FF"), 0f), WithAlpha(Hex("9FE0FF"), 0f), WithAlpha(Hex("6FD8E0"), 0f) };   // no wave shafts anywhere — clean, non-decorative background
            bd.vignetteAlpha = 0f;
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
            var gamePhoto = LoadGameBgImage();
            if (gamePhoto != null)
            {
                bd.bgPhoto = MakeWorldSprite(backdrop.transform, "GamePhoto", gamePhoto, -206);
                bd.bgPhoto.transform.localScale = new Vector3(30f, 18f, 1f);
                var photoWash = MakeWorldSprite(backdrop.transform, "PhotoWash", spr.fill, -205);
                photoWash.color = new Color(0.02f, 0.05f, 0.09f, 0.30f);
                photoWash.transform.localScale = new Vector3(200f, 200f, 1f);
            }
            return bd;
        }

        static void BuildMenuBackground(Transform canvas, Sprites spr)
        {
            // NOTE: NO opaque background image — the world-space board renders behind this canvas and must
            // show through the "O". The ocean colour + nebula atmosphere come from the world CameraBackdrop.

            // soft top-centre glow → a gentle radial lightening (semi-transparent, the board shows through)
            var topGlow = UIImage(canvas, "MenuTopGlow", spr.glow, WithAlpha(Hex("2E86B0"), 0.4f), new Vector2(0, 340), new Vector2(1800, 1200));
            topGlow.raycastTarget = false;

            // clearly-visible drifting SQUARES (like the reference's floating squares), in ocean tones
            var ambRT = MakeRect(canvas, "MenuParticles", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var amb = ambRT.gameObject.AddComponent<UIAmbient>();
            amb.sprite = spr.floorFill;
            amb.palette = new[] { Hex("3E6E90"), Hex("5A9CC0"), Hex("86D2E4"), Hex("B4E8F0") };
            amb.count = 32; amb.baseAlpha = 0.4f;
            amb.sizeRange = new Vector2(18f, 56f); amb.riseSpeed = 40f; amb.sway = 10f;
            amb.drift = true;   // float around within the screen, not stream up off the top

            // gentle depth vignette (content sits above this)
            var vigRT = MakeRect(canvas, "MenuVignette", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var vig = vigRT.gameObject.AddComponent<Image>();
            vig.sprite = spr.vignette; vig.color = new Color(0f, 0f, 0f, 0.24f); vig.raycastTarget = false;
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

        // Optional custom font: the first .ttf/.otf in Assets/Parabox/Fonts restyles ALL text. Null => default UI font.
        static Font LoadGameFont()
        {
            string dir = Root + "/Fonts";
            if (!AssetDatabase.IsValidFolder(dir)) return null;
            foreach (var guid in AssetDatabase.FindAssets("t:Font", new[] { dir }))
            {
                var f = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(guid));
                if (f != null) return f;
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

        // Optional background music. The bundled CC0 puzzle track is preferred, while Music.*
        // remains available as a simple replacement name for future customization.
        static AudioClip LoadMusicClip()
        {
            // preferred names first
            string[] names = { "Sci-Fi Puzzle In-Game 1 - MintoDog", "Music", "music" };
            foreach (var n in names)
                foreach (var ext in new[] { ".ogg", ".mp3", ".wav" })
                {
                    string path = Root + "/Audio/" + n + ext;
                    string abs = Path.Combine(Application.dataPath, path.Substring("Assets/".Length));
                    if (File.Exists(abs))
                    {
                        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                        if (clip != null) return clip;
                    }
                }
            // fallback: use the first audio file of any name dropped in Assets/Parabox/Audio/
            string audioDir = Path.Combine(Application.dataPath, "Parabox/Audio");
            if (Directory.Exists(audioDir))
                foreach (var file in Directory.GetFiles(audioDir))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".ogg" || ext == ".mp3" || ext == ".wav" || ext == ".aiff" || ext == ".aif")
                    {
                        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Parabox/Audio/" + Path.GetFileName(file));
                        if (clip != null) return clip;
                    }
                }
            return null;
        }

        // Persistent looping music player (singleton, survives scene loads, follows the mute button).
        static void CreateMusicPlayer()
        {
            var go = new GameObject("MusicPlayer");
            go.AddComponent<AudioSource>();
            var mp = go.AddComponent<MusicPlayer>();
            mp.clip = LoadMusicClip();
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
        // The completion trophy: three of THIS GAME'S boxes, nested.
        //
        // The first version was hard-cornered squares sitting inside a circular ring, and it read
        // as wrong for two reasons that are the same reason: shape disagreement. The game's boxes
        // are ROUNDED squares (radiusFrac 0.22 — see `fill` and `shaded`), so a 90-degree glyph is
        // not this game's box; and a square glyph inside a CIRCLE has its corners fighting the ring
        // that frames it.
        //
        // So: the same rounded-square SDF the boxes themselves use, three rings deep, framed later
        // by a rounded-square ring rather than a circle. Every corner in the badge now agrees.
        static Sprite MakeTrophySprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];

            // half-extent of each ring, and its stroke — even steps, so it reads as deliberate
            float[] half  = { 0.440f, 0.290f, 0.140f };
            float[] thick = { 0.052f, 0.046f, 0.040f };
            const float BoxRadius = 0.22f;   // the boxes' own corner radius, as a fraction of the box
            const int SS = 4;                // the strokes are thin; 4x4 supersampling keeps them clean

            // signed distance to a rounded square of half-extent e (negative inside)
            float Sd(float x, float y, float e)
            {
                float r = BoxRadius * e * 2f;                  // radius scales with the ring, so all three match
                float dx = Mathf.Abs(x) - (e - r);
                float dy = Mathf.Abs(y) - (e - r);
                float outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) +
                                           Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
                return outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - r;
            }

            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                        {
                            float fx = (x + (sx + 0.5f) / SS) / s - 0.5f;
                            float fy = (y + (sy + 0.5f) / SS) / s - 0.5f;

                            bool ink = Sd(fx, fy, 0.055f) < 0f;          // the innermost box: solid
                            for (int r = 0; r < half.Length && !ink; r++)
                            {
                                float d = Sd(fx, fy, half[r]);
                                if (d < 0f && d > -thick[r]) ink = true;  // a ring of that stroke
                            }
                            if (ink) hits++;
                        }
                    px[y * s + x] = new Color32(255, 255, 255, (byte)(255 * hits / (SS * SS)));
                }

            tex.SetPixels32(px);
            tex.Apply();
            return SaveTexAsSprite(tex, "Trophy", false, 0, FilterMode.Bilinear);
        }

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
            // The module ships with the default UI actions (Point/Click/Move/Submit) already wired,
            // so arrow-key navigation works once a button is focused (handled by MainMenuUI).
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
        static ScreenFade CreateFade(Transform canvas) => CreateFade(canvas, Color.black);
        static ScreenFade CreateFade(Transform canvas, Color color)
        {
            var rt = MakeRect(canvas, "ScreenFade", Vector2.zero, Vector2.zero,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(color.r, color.g, color.b, 0f); // transparent in the editor;
            img.raycastTarget = false;             // ScreenFade sets alpha=1 at runtime, then fades out
            var fade = rt.gameObject.AddComponent<ScreenFade>();
            fade.image = img;
            return fade;
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

        // Soft drop shadow so text stays legible over the busy world board / backdrop.
        static Text AddTextShadow(Text t)
        {
            var sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0.02f, 0.05f, 0.8f);
            sh.effectDistance = new Vector2(0f, -3f);
            return t;
        }

        static Text MakeText(Transform parent, string name, string content, int size, Color color,
            Vector2 pos, Vector2 dim, FontStyle style = FontStyle.Normal,
            TextAnchor align = TextAnchor.MiddleCenter,
            Vector2? anchorMin = null, Vector2? anchorMax = null, Vector2? pivot = null)
        {
            var rt = MakeRect(parent, name, pos, dim, anchorMin, anchorMax, pivot);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = s_font != null ? s_font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
            var hover = rt.gameObject.AddComponent<UIHoverScale>();

            // bright glow behind the button, shown only while hovered or selected (obvious focus)
            var hl = UIImage(rt, "Highlight", s_glow != null ? s_glow : s_roundBtn,
                WithAlpha(Lighten(top, 0.45f), 0.9f), Vector2.zero, new Vector2(size.x + 140f, size.y + 140f));
            hl.gameObject.SetActive(false);
            hover.highlight = hl.gameObject;

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

            // Label weight: the typeface is fixed (one font ships in Assets/Parabox/Fonts), so the
            // presence has to come from treatment — a dark outline to bite against the lit face,
            // plus a drop shadow underneath it. Outline first so the shadow reads below both.
            var lbl = MakeText(faceRT, "Label", label, fontSize, Color.white, Vector2.zero, size, FontStyle.Bold);
            var lout = lbl.gameObject.AddComponent<Outline>();
            lout.effectColor = new Color(0f, 0.06f, 0.10f, 0.85f);
            lout.effectDistance = new Vector2(1.6f, -1.6f);
            var lsh = lbl.gameObject.AddComponent<Shadow>();
            lsh.effectColor = new Color(0f, 0f, 0f, 0.55f);
            lsh.effectDistance = new Vector2(0f, -3f);
            return btn;
        }

        // A non-interactive rounded UI image (menu decoration / mascot).
        // A clean flat control button — rounded slate fill + thin accent outline + centered label. Clickable.
        // One half of the split "PARAB[board]X" title, styled like the original PARABOX logo.
        static void TitleWord(Transform parent, string name, string text)
        {
            var t = MakeText(parent, name, text, 108, Color.white, Vector2.zero, new Vector2(260, 150), FontStyle.Bold);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var g = t.gameObject.AddComponent<UIGradient>();   // gradient fill (added before Shadow)
            g.top = Hex("5FE6EA"); g.bottom = Hex("2E86C4");
            var sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.6f);
            sh.effectDistance = new Vector2(0f, -7f);
        }

        // The "O" in PARABOX is a TRANSPARENT slot in the logo. The real world-space board renders behind
        // the canvas and is framed by the menu camera to sit exactly in `inner` (its on-screen anchor).
        static RectTransform MakeTitleBoardO(Transform parent, Sprites spr, out RectTransform inner, out RectTransform ringRT)
        {
            var board = MakeRect(parent, "TitleBoardO", Vector2.zero, new Vector2(118, 118));
            var le = board.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 118f; le.preferredHeight = 118f;

            // The letter O itself. Without this the only outline was the world board's own frame,
            // which is sized to the LEVEL — so a wide level made the "O" read as a dash. This ring is
            // a fixed square, styled like the other letters (same gradient + drop shadow), so PARABOX
            // always reads correctly no matter what shape the level behind it is.
            var ring = UIImage(board, "ORing", spr.ringThick, Color.white, Vector2.zero, new Vector2(118, 118));
            var rg = ring.gameObject.AddComponent<UIGradient>();
            rg.top = Hex("5FE6EA"); rg.bottom = Hex("2E86C4");
            var rsh = ring.gameObject.AddComponent<Shadow>();
            rsh.effectColor = new Color(0f, 0f, 0f, 0.6f);
            rsh.effectDistance = new Vector2(0f, -7f);

            // invisible spacer: reserves the O gap in the word and anchors the world board (via GetWorldCorners)
            var innerImg = UIImage(board, "OInner", null, new Color(0f, 0f, 0f, 0f), Vector2.zero, new Vector2(100, 100));
            inner = innerImg.rectTransform;
            ringRT = ring.rectTransform;
            return board;
        }

        // A big keycap-style title-screen button (label inside), clickable AND keyboard-navigable.
        static Button MakePrompt(Transform parent, string name, string label, Vector2 pos, Vector2 size, Sprites spr)
        {
            var hl = UIImage(parent, name + "Glow", s_glow, WithAlpha(Hex("59E1F0"), 0.85f), pos, size + new Vector2(80f, 80f));
            hl.gameObject.SetActive(false);

            var rt = MakeRect(parent, name, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = spr.fill; img.type = Image.Type.Sliced; img.color = WithAlpha(Hex("0B2A44"), 0.62f);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = Color.white; colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.82f, 0.88f, 0.94f, 1f); colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f; btn.colors = colors;
            var hover = rt.gameObject.AddComponent<UIHoverScale>();
            hover.highlight = hl.gameObject;

            // glossy top sheen
            var gloss = UIImage(rt, "Gloss", spr.fill, Color.white, new Vector2(0f, size.y * 0.02f), size - new Vector2(8f, 8f));
            gloss.type = Image.Type.Sliced;
            var gg = gloss.gameObject.AddComponent<UIGradient>();
            gg.top = WithAlpha(Color.white, 0.12f); gg.bottom = WithAlpha(Color.white, 0f);

            // crisp cyan neon border — the keycap look
            AddRing(rt, "Outline", size, spr.ringThin, WithAlpha(Hex("6FEAF2"), 0.95f));

            MakeText(rt, "Lbl", label, 34, Hex("EAFBFF"), Vector2.zero, size, FontStyle.Bold);
            return btn;
        }

        // A small keycap chip showing which keyboard key triggers a control button.
        static void MakeKeyHint(Transform parent, string key, Vector2 pos)
        {
            float w = 30f + Mathf.Max(0, key.Length - 1) * 13f;
            var size = new Vector2(w, 28f);
            // soft shadow beneath for a raised keycap
            UIImage(parent, "KeyShadow_" + key, s_glow, WithAlpha(Hex("020912"), 0.5f), pos + new Vector2(0f, -3f), size + new Vector2(6f, 8f));
            // keycap body with a two-tone gradient + neon edge
            var chip = UIImage(parent, "Key_" + key, s_roundBtn, Color.white, pos, size);
            chip.type = Image.Type.Sliced;
            var g = chip.gameObject.AddComponent<UIGradient>();
            g.top = Hex("17394F"); g.bottom = Hex("0A2236");
            var ol = chip.gameObject.AddComponent<Outline>();
            ol.effectColor = WithAlpha(Hex("46CEE0"), 0.7f);
            ol.effectDistance = new Vector2(1.2f, -1.2f);
            // glossy top sheen
            var gloss = UIImage(chip.transform, "KGloss", s_roundBtn, Color.white, Vector2.zero, size - new Vector2(4f, 4f));
            gloss.type = Image.Type.Sliced;
            var gg = gloss.gameObject.AddComponent<UIGradient>();
            gg.top = WithAlpha(Color.white, 0.18f); gg.bottom = WithAlpha(Color.white, 0f);
            MakeText(chip.transform, "K", key, 13, Hex("DFF4FA"), Vector2.zero, size, FontStyle.Bold);
        }

        static Button MakeFlatButton(Transform parent, string name, string label, int fontSize,
                                     Vector2 pos, Vector2 size, Color accent, Sprites spr)
        {
            // soft drop shadow for a raised, tactile feel (furthest back)
            UIImage(parent, name + "Shadow", spr.glow, WithAlpha(Hex("020912"), 0.5f),
                pos + new Vector2(0f, -9f), new Vector2(size.x + 4f, size.y + 14f));

            // bright glow behind the button, shown only while hovered or selected
            var hl = UIImage(parent, name + "HL", spr.glow, WithAlpha(Lighten(accent, 0.5f), 0.9f),
                pos, new Vector2(size.x + 62f, size.y + 62f));
            hl.gameObject.SetActive(false);

            var rt = MakeRect(parent, name, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = spr.fill; img.type = Image.Type.Sliced; img.color = Color.white;
            var grad = rt.gameObject.AddComponent<UIGradient>();   // premium two-tone fill
            grad.top = Hex("204E6A"); grad.bottom = Hex("0A2034");
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.80f, 0.86f, 0.92f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            var hover = rt.gameObject.AddComponent<UIHoverScale>();
            hover.highlight = hl.gameObject;

            // glossy sheen fading down from the top edge
            var gloss = UIImage(rt, "Gloss", spr.fill, Color.white, new Vector2(0f, size.y * 0.02f), size - new Vector2(6f, 6f));
            gloss.type = Image.Type.Sliced;
            var gg = gloss.gameObject.AddComponent<UIGradient>();
            gg.top = WithAlpha(Color.white, 0.16f); gg.bottom = WithAlpha(Color.white, 0f);

            // neon outline: a faint halo ring plus a crisp bright edge
            AddRing(rt, "OutlineGlow", size + Vector2.one * 7f, spr.ringThin, WithAlpha(accent, 0.3f));
            AddRing(rt, "Outline", size, spr.ringThin, WithAlpha(accent, 0.95f));

            if (!string.IsNullOrEmpty(label))
            {
                var lbl = MakeText(rt, "Label", label, fontSize, Color.white, Vector2.zero, size, FontStyle.Bold);
                var sh = lbl.gameObject.AddComponent<Shadow>();
                sh.effectColor = WithAlpha(Hex("02121C"), 0.6f);
                sh.effectDistance = new Vector2(0f, -2f);
            }
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
