using UnityEngine;
using UnityEngine.UI;

namespace Parabox
{
    // Decorative geometry under the existing 50 Buttons. The original map artwork remains
    // behind it, retaining the sci-fi background and floor; only its flat chapter plates retire.
    [DisallowMultipleComponent]
    public sealed class PremiumMapDeck : MaskableGraphic
    {
        readonly Vector2[] nodes = new Vector2[50];
        readonly bool[] completed = new bool[50];
        static readonly float[] Centers = { -736f, -373f, -7f, 360f, 727f };
        static readonly string[] Chapters = { "CHAPTER I", "CHAPTER II", "CHAPTER III", "CHAPTER IV", "CHAPTER V" };
        static readonly int[,] Links = { {0,3}, {1,3}, {1,4}, {2,4}, {3,5}, {4,6}, {5,7}, {5,8}, {6,8}, {6,9} };

        public static PremiumMapDeck Apply(Transform artworkRoot, Vector2[] positions, Font font)
        {
            if (artworkRoot == null || positions == null || positions.Length != 50) return null;
            Transform existing = artworkRoot.Find("PremiumChapterDeck");
            var deck = existing != null ? existing.GetComponent<PremiumMapDeck>() : null;
            if (deck == null)
            {
                var go = new GameObject("PremiumChapterDeck", typeof(RectTransform), typeof(CanvasRenderer), typeof(PremiumMapDeck));
                go.layer = artworkRoot.gameObject.layer;
                go.transform.SetParent(artworkRoot, false);
                deck = go.GetComponent<PremiumMapDeck>();
            }
            RectTransform rect = deck.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(1920f, 1080f);
            deck.raycastTarget = false;
            deck.color = Color.white;
            deck.transform.SetAsLastSibling();
            System.Array.Copy(positions, deck.nodes, 50);
            for (int ch = 0; ch < 5; ch++)
            {
                string key = "ChapterTitle" + ch;
                Transform old = rect.Find(key);
                Text text;
                if (old == null)
                {
                    var title = new GameObject(key, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Shadow));
                    title.layer = rect.gameObject.layer;
                    title.transform.SetParent(rect, false);
                    text = title.GetComponent<Text>();
                }
                else text = old.GetComponent<Text>();
                text.font = font;
                text.fontSize = 24;
                text.fontStyle = FontStyle.Normal;
                text.text = Chapters[ch];
                text.alignment = TextAnchor.MiddleCenter;
                text.color = PremiumMapPalette.ForChapter(ch).glow;
                text.raycastTarget = false;
                text.resizeTextForBestFit = false;
                RectTransform t = text.rectTransform;
                t.anchorMin = t.anchorMax = t.pivot = new Vector2(.5f,.5f);
                t.anchoredPosition = new Vector2(Centers[ch], 325f);
                t.sizeDelta = new Vector2(294f, 66f);
                Shadow shadow = text.GetComponent<Shadow>();
                if (shadow != null) { shadow.effectColor = new Color(0, .01f, .03f, .9f); shadow.effectDistance = new Vector2(0,-2f); }
            }
            deck.SetVerticesDirty();
            return deck;
        }

        public void SetCompleted(int index, bool value)
        {
            if (index < 0 || index >= completed.Length || completed[index] == value) return;
            completed[index] = value;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            for (int ch = 0; ch < 5; ch++) DrawChapter(mesh, ch);
        }
        static Color C(byte r, byte g, byte b, byte a = 255) => new Color32(r,g,b,a);
        static void Plate(VertexHelper mesh, float x, float y, float w, float h, float radius, Color top, Color bottom) =>
            PremiumMapMesh.Plate(mesh, new Rect(x - w * .5f, y - h * .5f, w, h), radius, top, bottom);

        void DrawChapter(VertexHelper mesh, int chapter)
        {
            float x = Centers[chapter];
            PremiumMapPalette p = PremiumMapPalette.ForChapter(chapter);
            // Covers the original plate exactly, while retaining the authored map positions.
            Plate(mesh, x, 28f, 359f, 705f, 19f, C(1,7,15,210), C(0,3,8,225));
            Plate(mesh, x, 30f, 355f, 695f, 18f, C(26,47,65), C(2,8,17));
            Plate(mesh, x, 39f, 355f, 685f, 17f, C(154,183,206), C(65,92,115));
            Plate(mesh, x, 39f, 352f, 682f, 16f, C(40,65,87), C(14,31,49));
            Plate(mesh, x, 40f, 345f, 674f, 13f, C(81,117,147), C(42,68,95));
            Plate(mesh, x, 40f, 340f, 669f, 11f, C(2,10,20), C(2,8,17));
            Plate(mesh, x, 40f, 336f, 665f, 10f, C(29,57,80), C(11,29,45));
            Plate(mesh, x, 39f, 332f, 661f, 9f, C(12,33,54), C(5,17,31));
            // Recessed heading with a polished upper lip, live type and matching accent.
            Plate(mesh, x, 326f, 309f, 73f, 9f, C(0,7,16), C(0,4,10));
            Plate(mesh, x, 330f, 307f, 69f, 8f, C(59,89,117), C(6,17,31));
            Plate(mesh, x, 329f, 304f, 66f, 7f, C(25,47,69), C(8,20,35));
            Plate(mesh, x, 367f, 222f, 1.7f, .8f, C(203,230,246,140), C(111,177,210,35));
            for (int side = -1; side <= 1; side += 2)
                Plate(mesh, x + side * 168f, 37f, 1.2f, 560f, .6f,
                    Color.Lerp(p.glow, C(78,111,140), .65f), C(15,39,57));
            // Wide colored underlight, resting on the original matching floor reflection.
            Plate(mesh, x, -301f, 148f, 8f, 4f, p.deep, p.shadow);
            Plate(mesh, x, -297.5f, 143f, 3.5f, 1.7f, p.glow, p.light);
            for (int link = 0; link < Links.GetLength(0); link++)
            {
                int a = chapter * 10 + Links[link, 0], b = chapter * 10 + Links[link, 1];
                Vector2 start = nodes[a], end = nodes[b];
                float halfway = (start.y + end.y) * .5f;
                Vector2 turnA = new Vector2(start.x, halfway), turnB = new Vector2(end.x, halfway);
                bool lit = completed[a] && completed[b];
                Color wire = Color.Lerp(C(12,28,44), p.mid, lit ? .95f : .42f);
                Route(mesh, start, turnA, turnB, end, 6f, C(1,7,15));
                Route(mesh, start, turnA, turnB, end, 2f, wire);
                if (lit) Route(mesh, start, turnA, turnB, end, .7f, p.glow);
            }
        }
        static void Route(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, float width, Color color)
        {
            PremiumMapMesh.Line(mesh, a, b, width, color);
            PremiumMapMesh.Line(mesh, b, c, width, color);
            PremiumMapMesh.Line(mesh, c, d, width, color);
        }
    }
}
