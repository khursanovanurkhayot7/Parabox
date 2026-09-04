using UnityEngine;

namespace Parabox
{
    // The approved artwork is shared by cargo sockets and pressure buttons. When both occupy
    // one cell, draw ONE socket: its cargo emblem and live gate light retain both meanings.
    public sealed class PremiumCargoSocket : MonoBehaviour
    {
        static Sprite coralArt, emptyArt;
        static Material socketMaterial;
        static readonly int LampId = Shader.PropertyToID("_Lamp");
        static readonly int HueId = Shader.PropertyToID("_FrameHueShift");
        static readonly int FillId = Shader.PropertyToID("_Filled");
        static readonly int BoundsId = Shader.PropertyToID("_ArtBounds");
        static readonly int LampOnlyId = Shader.PropertyToID("_LampOnly");
        static readonly int RecessId = Shader.PropertyToID("_RecessRect");
        // Centre and half-size in normalized opaque-tile coordinates. The light shader uses this
        // to keep its status detail aligned with the artwork's recessed opening.
        static readonly Vector4 RecessRect = new Vector4(0.5f, 0.52f, 0.304f, 0.314f);
        const float ArtworkScale = 0.90f;

        LevelModel model;
        int roomId;
        Vector2Int cell;
        bool button, heavy;
        float goalFill, press, hueShift;
        SpriteRenderer art, lamp;
        Transform emblem;
        MaterialPropertyBlock properties;
        Vector4 artBounds;

        public static bool ArtAvailable
        {
            get
            {
                if (coralArt == null) coralArt = Resources.Load<Sprite>("UI/PremiumSocketCoral");
                if (emptyArt == null) emptyArt = Resources.Load<Sprite>("UI/PremiumSocketBase");
                if (socketMaterial == null)
                    socketMaterial = Resources.Load<Material>("UI/PremiumSocket");
                return coralArt != null && emptyArt != null && socketMaterial != null;
            }
        }

        public static bool HasCargoGoal(PRoom room, Vector2Int cell)
        {
            if (room.boxGoals.Contains(cell)) return true;
            foreach (var goal in room.colourGoals)
                if (goal.cell == cell) return true;
            return false;
        }

        public static bool IsPressedBy(PEntity occupant, bool isButton, bool isHeavyPlate)
            => occupant != null && !occupant.sunk
                && (isButton || (isHeavyPlate && occupant.IsCrate));

        public static bool TryBuild(GameObject root, BoardAssets assets, LevelModel model,
            PRoom room, Vector2Int cell, Color accent, int colour = 0)
        {
            if (!ArtAvailable || assets.cellSprite == null) return false;

            // Disable whole legacy objects, not just renderers: room-focus filtering is allowed
            // to toggle renderer.enabled and must never resurrect the old overlapping rings.
            foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
                renderer.gameObject.SetActive(false);

            var fx = root.AddComponent<PremiumCargoSocket>();
            fx.model = model;
            fx.roomId = room.id;
            fx.cell = cell;
            fx.button = room.IsButton(cell);
            fx.heavy = room.IsPlate(cell);
            Color.RGBToHSV(accent, out float hue, out _, out _);
            // The coral family uses the approved bitmap unchanged; the other cargo families
            // only shift the frame hue, retaining their established colour-matching rules.
            fx.hueShift = colour == 1 ? 0f : hue - 0.04f;
            fx.properties = new MaterialPropertyBlock();
            // Texture alpha stays untouched. Exclude only transparent-padding fragments outside
            // the measured tile silhouette, like selecting an atlas rectangle at draw time.
            fx.artBounds = colour == 1
                ? new Vector4(0.0574f, 0.0765f, 0.9410f, 0.9498f)
                : new Vector4(0.0375f, 0.0654f, 0.9625f, 0.9649f);

            var artwork = new GameObject("PremiumSocketArt");
            artwork.transform.SetParent(root.transform, false);
            fx.art = artwork.AddComponent<SpriteRenderer>();
            fx.art.sprite = colour == 1 ? coralArt : emptyArt;
            fx.art.sharedMaterial = socketMaterial;
            fx.art.sortingOrder = 4;
            // Import settings normalize the opaque tile to one unit, excluding transparent
            // padding. Thus its visible footprint stays at the same 0.90 size as the player.
            artwork.transform.localScale = Vector3.one * ArtworkScale;
            // The small status light must remain visible when cargo covers the socket. Reuse
            // its exact artwork fragment plus the thin goal inlay above actors. Never lift
            // the opaque frame or cavity over them, and never light the inlay for pressure alone.
            var light = new GameObject("PremiumSocketStatus");
            light.transform.SetParent(artwork.transform, false);
            fx.lamp = light.AddComponent<SpriteRenderer>();
            fx.lamp.sprite = fx.art.sprite;
            fx.lamp.sharedMaterial = socketMaterial;
            fx.lamp.sortingOrder = 118;
            if (colour != 1)
                fx.emblem = BuildEmblem(root.transform, assets.cellSprite, colour, fx.heavy);
            fx.Refresh(0f, true);
            return true;
        }

        // Called by the existing goal controller, after visual arrival has been confirmed.
        public void SetGoalFill(float fill) => goalFill = Mathf.Clamp01(fill);

        void LateUpdate() => Refresh(Time.deltaTime, false);

        void Refresh(float dt, bool instant)
        {
            if (art == null || model == null) return;
            bool held = IsPressedBy(model.EntityAt(roomId, cell), button, heavy);
            float target = button || heavy ? (held ? 1f : 0f) : goalFill;
            press = instant ? target : Mathf.MoveTowards(press, target, dt / 0.14f);
            art.GetPropertyBlock(properties);
            properties.SetFloat(LampId, press);
            properties.SetFloat(HueId, hueShift);
            properties.SetFloat(FillId, goalFill);
            properties.SetVector(BoundsId, artBounds);
            properties.SetVector(RecessId, RecessRect);
            properties.SetFloat(LampOnlyId, 0f);
            art.SetPropertyBlock(properties);

            // The empty socket's cargo emblem is a teaching cue, not a second piece. Once real
            // cargo arrives it must disappear completely so the occupant keeps one clean face.
            if (emblem != null)
                emblem.gameObject.SetActive(goalFill <= 0.001f);

            // Keep only the physical status lamp above a seated piece. The former foreground
            // inlay traced a second square across the cargo face and made the occupied socket
            // read as two mismatched objects.
            properties.SetFloat(LampOnlyId, 1f);
            properties.SetFloat(FillId, 0f);
            lamp.SetPropertyBlock(properties);
            lamp.gameObject.SetActive(press > 0.001f || goalFill > 0.001f);
        }

        static Transform BuildEmblem(Transform root, Sprite sprite, int colour, bool heavy)
        {
            var emblem = new GameObject("SocketEmblem").transform;
            emblem.SetParent(root, false);
            // Keep the symbols already used by the corresponding crates. Their raised cream
            // faces match the approved coral glyph, without piling hatch marks behind it.
            if (colour == 2)
            {
                Stroke(emblem, sprite, new Vector2(-0.16f, 0.13f), new Vector2(0.12f, 0.13f));
                Stroke(emblem, sprite, new Vector2(-0.20f, 0f), new Vector2(0.20f, 0f));
                Stroke(emblem, sprite, new Vector2(-0.17f, -0.13f), new Vector2(0.08f, -0.13f));
            }
            else if (colour == 3)
            {
                Stroke(emblem, sprite, new Vector2(-0.14f, -0.17f), new Vector2(0.16f, 0.17f));
                Stroke(emblem, sprite, new Vector2(-0.04f, -0.04f), new Vector2(-0.10f, 0.15f));
                Stroke(emblem, sprite, new Vector2(0.045f, 0.07f), new Vector2(0.18f, 0.015f));
            }
            else
            {
                // A rounded cargo outline and its familiar diagonal slats, not a direction arrow.
                Stroke(emblem, sprite, new Vector2(-0.19f, -0.17f), new Vector2(-0.19f, 0.19f), 0.045f);
                Stroke(emblem, sprite, new Vector2(-0.19f, 0.19f), new Vector2(0.19f, 0.19f), 0.045f);
                Stroke(emblem, sprite, new Vector2(0.19f, 0.19f), new Vector2(0.19f, -0.17f), 0.045f);
                Stroke(emblem, sprite, new Vector2(0.19f, -0.17f), new Vector2(-0.19f, -0.17f), 0.045f);
                Stroke(emblem, sprite, new Vector2(-0.13f, 0.03f), new Vector2(0.01f, 0.13f), 0.04f);
                Stroke(emblem, sprite, new Vector2(-0.10f, -0.10f), new Vector2(0.13f, 0.09f), 0.04f);
                if (heavy)
                    Stroke(emblem, sprite, new Vector2(-0.17f, -0.25f), new Vector2(0.17f, -0.25f), 0.055f);
            }
            return emblem;
        }

        static void Stroke(Transform root, Sprite sprite, Vector2 from, Vector2 to, float width = 0.062f)
        {
            Vector2 delta = to - from;
            Vector2 middle = (from + to) * 0.5f;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            Part(root, sprite, "EmbossShadow", middle + new Vector2(0.012f, -0.019f),
                delta.magnitude + width, width + 0.018f, angle, new Color(0.12f, 0.045f, 0.015f, 0.72f), 5);
            Part(root, sprite, "EmbossDepth", middle + new Vector2(0.005f, -0.008f),
                delta.magnitude + width, width, angle, new Color(0.68f, 0.36f, 0.16f, 1f), 6);
            Part(root, sprite, "EmbossFace", middle,
                delta.magnitude + width, width, angle, new Color(1f, 0.77f, 0.52f, 1f), 7);
            Part(root, sprite, "EmbossHighlight", middle + new Vector2(-0.006f, 0.009f),
                delta.magnitude + width * 0.45f, width * 0.34f, angle, new Color(1f, 0.94f, 0.77f, 0.8f), 8);
        }

        static void Part(Transform root, Sprite sprite, string name, Vector2 at, float length,
            float width, float angle, Color color, int order)
        {
            var part = new GameObject(name);
            part.transform.SetParent(root, false);
            part.transform.localPosition = new Vector3(at.x, at.y, 0f);
            part.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            part.transform.localScale = new Vector3(length, width, 1f);
            var renderer = part.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = order;
        }
    }
}
