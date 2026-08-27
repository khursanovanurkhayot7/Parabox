using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Parabox
{
    // Public data shape for the future online service. Replacing the placeholder only requires
    // passing ten real entries to SetEntries; the loss-screen layout does not need to change.
    public struct LeaderboardEntry
    {
        public int rank;
        public string playerName;
        public int score;

        public LeaderboardEntry(string playerName, int score)
            : this(0, playerName, score)
        {
        }

        public LeaderboardEntry(int rank, string playerName, int score)
        {
            this.rank = rank;
            this.playerName = playerName;
            this.score = score;
        }
    }

    public class LossLeaderboard : MonoBehaviour
    {
        public const int Capacity = 10;

        [SerializeField] CanvasGroup group;
        [SerializeField] Text[] rankLabels = new Text[Capacity];
        [SerializeField] Text[] playerLabels = new Text[Capacity];
        [SerializeField] Text[] scoreLabels = new Text[Capacity];
        [SerializeField] Image[] rowBackings = new Image[Capacity];
        [SerializeField] Text titleLabel;
        [SerializeField] Text columnLabel;
        [SerializeField] Image panel;
        [SerializeField] Outline outline;

        public CanvasGroup Group => group;
        public RectTransform Rect => (RectTransform)transform;
        Color accent;
        Color backing;

        public bool IsFullyPrebuilt
        {
            get
            {
                if (group == null || panel == null || outline == null || titleLabel == null
                    || columnLabel == null || rankLabels == null || playerLabels == null
                    || scoreLabels == null || rowBackings == null
                    || rankLabels.Length != Capacity || playerLabels.Length != Capacity
                    || scoreLabels.Length != Capacity || rowBackings.Length != Capacity)
                    return false;
                for (int i = 0; i < Capacity; i++)
                    if (rankLabels[i] == null || playerLabels[i] == null
                        || scoreLabels[i] == null || rowBackings[i] == null)
                        return false;
                return true;
            }
        }

        public static LossLeaderboard Create(Transform parent, Font font, Sprite panelSprite,
            Color accent, Color backing)
        {
            var go = new GameObject("LossLeaderboard", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(CanvasGroup), typeof(LossLeaderboard));
            var board = go.GetComponent<LossLeaderboard>();
            board.Rect.SetParent(parent, false);
            board.Rect.anchorMin = board.Rect.anchorMax = new Vector2(0.5f, 0.5f);
            board.Rect.pivot = new Vector2(0.5f, 0.5f);
            board.Rect.anchoredPosition = new Vector2(500f, 0f);
            board.Rect.sizeDelta = new Vector2(560f, 660f);

            board.panel = go.GetComponent<Image>();
            board.panel.raycastTarget = false;
            if (panelSprite != null)
            {
                board.panel.sprite = panelSprite;
                board.panel.type = Image.Type.Sliced;
            }

            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0.02f, 0.05f, 0.72f);
            shadow.effectDistance = new Vector2(0f, -7f);
            board.outline = go.AddComponent<Outline>();
            board.outline.effectDistance = new Vector2(2f, -2f);
            board.group = go.GetComponent<CanvasGroup>();
            board.group.alpha = 0f;
            board.group.interactable = false;
            board.group.blocksRaycasts = false;

            board.Build(font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            board.ApplyTheme(accent, backing);
            board.SetEntries(null);
            return board;
        }

        void Awake()
        {
            if (group == null) group = GetComponent<CanvasGroup>();
            LuxoddGameService.RegisterLeaderboard(this);
        }

        void OnDestroy()
        {
            LuxoddGameService.UnregisterLeaderboard(this);
        }

        void Build(Font font)
        {
            titleLabel = MakeText("Title", transform, font, 36, FontStyle.Bold,
                new Vector2(0f, 285f), new Vector2(510f, 50f), TextAnchor.MiddleCenter);
            titleLabel.text = "LEADERBOARD";

            var topTen = MakeText("TopTen", transform, font, 16, FontStyle.Bold,
                new Vector2(0f, 247f), new Vector2(510f, 26f), TextAnchor.MiddleCenter);
            topTen.text = "TOP 10 PLAYERS";
            topTen.color = new Color(0.70f, 0.80f, 0.87f, 1f);

            columnLabel = MakeText("Columns", transform, font, 14, FontStyle.Bold,
                new Vector2(0f, 210f), new Vector2(500f, 28f), TextAnchor.MiddleCenter);
            columnLabel.text = "RANK                 PLAYER                         SCORE";

            for (int i = 0; i < Capacity; i++)
            {
                float y = 168f - i * 44f;
                var row = new GameObject($"Row{i + 1}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var rowRect = (RectTransform)row.transform;
                rowRect.SetParent(transform, false);
                rowRect.anchorMin = rowRect.anchorMax = new Vector2(0.5f, 0.5f);
                rowRect.anchoredPosition = new Vector2(0f, y);
                rowRect.sizeDelta = new Vector2(500f, 38f);
                rowBackings[i] = row.GetComponent<Image>();
                rowBackings[i].raycastTarget = false;

                rankLabels[i] = MakeText("Rank", row.transform, font, 19, FontStyle.Bold,
                    new Vector2(-211f, 0f), new Vector2(56f, 34f), TextAnchor.MiddleCenter);
                playerLabels[i] = MakeText("Player", row.transform, font, 19, FontStyle.Bold,
                    new Vector2(-34f, 0f), new Vector2(280f, 34f), TextAnchor.MiddleLeft);
                scoreLabels[i] = MakeText("Score", row.transform, font, 19, FontStyle.Bold,
                    new Vector2(191f, 0f), new Vector2(110f, 34f), TextAnchor.MiddleRight);
            }
        }

        public void ApplyTheme(Color newAccent, Color newBacking)
        {
            accent = newAccent;
            backing = Color.Lerp(newBacking, Color.black, 0.34f);
            backing.a = 0.96f;
            if (panel != null) panel.color = backing;
            if (outline != null)
            {
                Color edge = accent; edge.a = 0.86f;
                outline.effectColor = edge;
            }
            if (titleLabel != null) titleLabel.color = Color.Lerp(accent, Color.white, 0.34f);
            if (columnLabel != null)
            {
                Color column = accent; column.a = 0.82f;
                columnLabel.color = column;
            }

            for (int i = 0; i < Capacity; i++)
            {
                if (rowBackings[i] != null)
                {
                    Color row = i % 2 == 0 ? Color.Lerp(backing, accent, 0.10f) : backing;
                    row.a = i % 2 == 0 ? 0.72f : 0.42f;
                    rowBackings[i].color = row;
                }
                if (rankLabels[i] != null) rankLabels[i].color = Color.Lerp(accent, Color.white, 0.16f);
                if (playerLabels[i] != null) playerLabels[i].color = new Color(0.87f, 0.94f, 0.97f, 1f);
                if (scoreLabels[i] != null) scoreLabels[i].color = Color.Lerp(accent, Color.white, 0.42f);
            }
        }

        // The player's own run is always visible even when their rank is outside the remote Top 10.
        public void SetPlayerScoreSummary(int levelScore, int totalScore)
        {
            if (titleLabel == null) return;
            titleLabel.resizeTextForBestFit = true;
            titleLabel.resizeTextMinSize = 20;
            titleLabel.resizeTextMaxSize = 36;
            titleLabel.text = $"LEVEL SCORE  {Mathf.Max(0, levelScore):n0}"
                + $"   •   TOTAL  {Mathf.Max(0, totalScore):n0}";
        }

        // Null or a short list deliberately leaves the remaining slots as PLAYER / 0 placeholders.
        public void SetEntries(IReadOnlyList<LeaderboardEntry> entries)
        {
            for (int i = 0; i < Capacity; i++)
            {
                bool supplied = entries != null && i < entries.Count;
                string player = supplied ? entries[i].playerName : null;
                int score = supplied ? Mathf.Max(0, entries[i].score) : 0;
                int rank = supplied && entries[i].rank > 0 ? entries[i].rank : i + 1;
                rankLabels[i].text = rank.ToString();
                playerLabels[i].text = string.IsNullOrWhiteSpace(player) ? "PLAYER" : player;
                scoreLabels[i].text = score.ToString("n0");
            }
        }

        static Text MakeText(string name, Transform parent, Font font, int size, FontStyle style,
            Vector2 position, Vector2 dimensions, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;

            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            CrispUiTypography.Polish(text);
            return text;
        }
    }
}
