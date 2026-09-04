using UnityEngine;
using UnityEngine.UI;

namespace Parabox
{
    // Live, native UI version of the approved Progress Rings design. No rasterized numbers,
    // replacement scoring rules, materials, raycast targets, or looping warning animations.
    [DisallowMultipleComponent]
    public sealed class PremiumScoreRings : MaskableGraphic
    {
        // The approved concept is a hero HUD element. At 300 x 324 it remains clear at arcade
        // distance while still fitting inside the width previously occupied by the old cards.
        public const float PanelWidth = 300f, PanelHeight = 324f;
        public const float HudLeftInset = 18f, HudTopInset = 28f;
        const float NoticeDuration = .48f;
        Text score, timeTitle, timeValue, movesTitle, movesValue;
        CanvasGroup[] parentGroups;
        bool bound, timePenalty, movePenalty;
        float timeTarget, moveTarget, timeProgress, moveProgress;
        float timeNotice = -1f, moveNotice = -1f;
        static readonly Color White = C(243,251,255), Cyan = C(61,233,255);
        static readonly Color Violet = C(175,104,255), Coral = C(255,123,103);

        public bool IsBound => bound && score != null && timeValue != null && movesValue != null;
        public float TimeProgress => timeProgress;
        public float MoveProgress => moveProgress;

        public static PremiumScoreRings Apply(RectTransform root, Text scoreText, Text timeMain,
            Text timeDetail, Text moveMain, Text moveDetail)
        {
            if (root == null || scoreText == null) return null;
            Transform old = root.Find("ProgressRingSurface");
            PremiumScoreRings view = old != null ? old.GetComponent<PremiumScoreRings>() : null;
            if (view == null)
            {
                var go = new GameObject("ProgressRingSurface", typeof(RectTransform), typeof(CanvasRenderer), typeof(PremiumScoreRings));
                go.layer = root.gameObject.layer;
                go.transform.SetParent(root, false);
                view = go.GetComponent<PremiumScoreRings>();
            }
            // Give the housing its own top-left pocket instead of letting its right-hand depth
            // touch the cyan puzzle frame. The small lift and left shift preserve screen margins
            // while opening a deliberate dark gutter between the two surfaces.
            root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0f, 1f);
            root.anchoredPosition = new Vector2(HudLeftInset, -HudTopInset);
            root.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            RectTransform r = view.rectTransform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
            r.localScale = Vector3.one;
            view.raycastTarget = false;
            view.color = Color.white;
            view.material = null;
            view.enabled = true;
            view.gameObject.SetActive(true);
            view.transform.SetAsLastSibling();

            Font font = scoreText.font != null ? scoreText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Transform oldTitle = root.Find("ScoreTitle");
            Text title = oldTitle != null ? oldTitle.GetComponent<Text>() : null;
            view.Place(title, "ScoreTitle", font, "SCORE", 27, new Vector2(0,113), new Vector2(185,34));
            view.score = view.Place(scoreText, "ScoreValue", font, scoreText.text, 78,
                new Vector2(0,38), new Vector2(230,116));
            view.timeTitle = view.Place(timeMain, "TimeTitle", font, "TIME", 16,
                new Vector2(-67,-59), new Vector2(76,23));
            view.timeValue = view.Place(timeDetail, "TimeReadout", font, "0 / 10s", 21,
                new Vector2(-67,-88), new Vector2(88,30));
            view.movesTitle = view.Place(moveMain, "MovesTitle", font, "MOVES", 16,
                new Vector2(67,-59), new Vector2(76,23));
            view.movesValue = view.Place(moveDetail, "MovesReadout", font, "0 / 1", 21,
                new Vector2(67,-88), new Vector2(88,30));

            // Reuse all five serialized Text references; retire only the old HUD decoration.
            // This also removes the rotating inner diamond and the two persistent -0 cards.
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child != view.transform) child.gameObject.SetActive(false);
            }
            CanvasGroup group = root.GetComponent<CanvasGroup>();
            if (group != null) { group.interactable = false; group.blocksRaycasts = false; }
            view.parentGroups = view.GetComponentsInParent<CanvasGroup>(true);
            view.bound = true;
            view.timePenalty = view.movePenalty = false;
            view.timeProgress = view.moveProgress = view.timeTarget = view.moveTarget = 0f;
            view.timeNotice = view.moveNotice = -1f;
            view.PaintReadouts();
            view.SetVerticesDirty();
            return view;
        }

        Text Place(Text existing, string name, Font font, string value, int size, Vector2 point, Vector2 bounds)
        {
            Text text = existing;
            if (text == null)
            {
                Transform old = transform.Find(name);
                if (old != null) text = old.GetComponent<Text>();
            }
            if (text == null)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                go.layer = gameObject.layer;
                text = go.GetComponent<Text>();
            }
            text.transform.SetParent(transform, false);
            text.name = name;
            text.gameObject.SetActive(true);
            text.enabled = true;
            text.font = font; text.fontSize = size; text.fontStyle = FontStyle.Normal;
            text.text = value; text.color = White;
            text.alignment = TextAnchor.MiddleCenter;
            text.resizeTextForBestFit = false;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            RectTransform r = text.rectTransform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f,.5f);
            r.anchoredPosition = Scale(point);
            r.sizeDelta = bounds;
            r.localScale = Vector3.one;
            foreach (BaseMeshEffect effect in text.GetComponents<BaseMeshEffect>()) effect.enabled = false;
            Shadow shadow = null;
            foreach (Shadow candidate in text.GetComponents<Shadow>())
                if (!(candidate is Outline)) { shadow = candidate; break; }
            if (shadow == null) shadow = text.gameObject.AddComponent<Shadow>();
            shadow.enabled = true; shadow.effectColor = new Color(0f,.02f,.05f,.52f);
            shadow.effectDistance = new Vector2(0,-1f); shadow.useGraphicAlpha = true;
            CrispUiTypography.Polish(text);
            return text;
        }

        public void SetBreakdown(ScoreSystem.Breakdown live)
        {
            if (!IsBound) return;
            bool nextTime = live.timePenalty > 0, nextMove = live.movePenalty > 0;
            // A threshold crossing gets one short reveal. Increasing -3 to -6, etc. only updates
            // the value: it must not repeatedly flash or interrupt the player's concentration.
            if (nextTime && !timePenalty) timeNotice = 0f;
            if (nextMove && !movePenalty) moveNotice = 0f;
            if (!nextTime) timeNotice = -1f;
            if (!nextMove) moveNotice = -1f;
            bool stateChanged = timePenalty != nextTime || movePenalty != nextMove;
            timePenalty = nextTime; movePenalty = nextMove;
            timeTarget = nextTime ? 1f : Mathf.Clamp01(live.elapsedSeconds / (float)ScoreSystem.TimeGraceSeconds);
            moveTarget = nextMove ? 1f : Mathf.Clamp01(live.usedMoves / (float)Mathf.Max(1,live.targetMoves));
            SetText(score, Mathf.Max(0,live.runScore).ToString());
            score.color = live.runScore <= 30 ? Coral : White;
            SetText(timeValue, nextTime ? "−" + live.timePenalty
                : Mathf.Max(0,live.elapsedSeconds) + " / " + ScoreSystem.TimeGraceSeconds + "s");
            SetText(movesValue, nextMove ? "−" + live.movePenalty
                : Mathf.Max(0,live.usedMoves) + " / " + Mathf.Max(1,live.targetMoves));
            // Long campaign values stay inside the gauge instead of touching the lit arc.
            timeValue.fontSize = timeValue.text.Length > 8 ? 18 : 21;
            movesValue.fontSize = movesValue.text.Length > 8 ? 18 : 21;
            PaintReadouts();
            if (stateChanged) SetVerticesDirty();
        }

        static void SetText(Text label, string text) { if (label.text != text) label.text = text; }
        void Update() { Advance(Mathf.Min(Time.unscaledDeltaTime, .05f)); }
        void Advance(float delta)
        {
            if (!IsBound || !VisibleToPlayer()) return;
            float oldTime = timeProgress, oldMove = moveProgress;
            timeProgress = Mathf.MoveTowards(timeProgress,timeTarget,delta * 3f);
            moveProgress = Mathf.MoveTowards(moveProgress,moveTarget,delta * 3f);
            bool animation = timeNotice >= 0f || moveNotice >= 0f;
            if (timeNotice >= 0f) timeNotice = timeNotice + delta >= NoticeDuration ? -1f : timeNotice + delta;
            if (moveNotice >= 0f) moveNotice = moveNotice + delta >= NoticeDuration ? -1f : moveNotice + delta;
            if (animation) PaintReadouts();
            if (animation || oldTime != timeProgress || oldMove != moveProgress) SetVerticesDirty();
        }

        bool VisibleToPlayer()
        {
            if (!gameObject.activeInHierarchy) return false;
            // Do not spend the one-shot warning behind the tutorial/cinematic HUD fade.
            if (parentGroups != null)
                foreach (CanvasGroup group in parentGroups)
                    if (group != null && group.alpha <= .01f) return false;
            return true;
        }
        void PaintReadouts()
        {
            if (!IsBound) return;
            PaintReadout(timeValue,new Vector2(-67,-88),timePenalty,timeNotice);
            PaintReadout(movesValue,new Vector2(67,-88),movePenalty,moveNotice);
        }
        void PaintReadout(Text text, Vector2 position, bool penalty, float notice)
        {
            float pulse = Pulse(notice);
            text.rectTransform.anchoredPosition = Scale(position) + Vector2.up * pulse * 1.8f;
            text.rectTransform.localScale = Vector3.one * (1f + pulse * .045f);
            text.color = penalty ? Color.Lerp(White,Coral,notice < 0f ? 1f : Mathf.Clamp01(notice / .18f)) : White;
        }
        static float Pulse(float t) => t < 0f ? 0f : Mathf.Sin(Mathf.Clamp01(t / NoticeDuration) * Mathf.PI);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            // The game board's cyan perimeter can run behind this screen-space HUD. Fade out
            // only the short section immediately beside the housing, creating a real visual
            // break without removing or moving the board frame itself.
            IsolationGutter(vh);
            // A contained base light makes this read as a floating HUD module. Its extrusion
            // falls straight down rather than leaning right into the puzzle-board frame.
            Oct(vh,0,-173,112,7,7,C(43,231,255,45),C(24,91,255,3));
            // Extruded navy housing, multi-surface cyan chamfer, recessed navy face.
            Oct(vh,0,-20,146,150,27,C(1,7,18),C(0,2,9));
            Oct(vh,0,-14,149,153,27,C(4,16,33),C(0,5,16));
            Oct(vh,0,-7,149,153,26,C(37,70,96),C(3,14,32));
            Oct(vh,0,3,149,153,27,C(150,244,255),C(15,60,108));
            Band(vh,new Vector2(0,3),149,153,27,7,C(198,255,255),C(8,69,129));
            Oct(vh,0,3,141,145,23,C(34,152,198),C(12,64,108));
            Band(vh,new Vector2(0,3),141,145,23,7,C(82,224,249),C(13,81,145));
            Oct(vh,0,3,134,138,18,C(2,13,29),C(1,7,21));
            Band(vh,new Vector2(0,3),133,137,18,2,C(112,206,237),C(30,58,111));
            Oct(vh,0,3,130,134,16,C(10,32,68),C(3,12,31));
            // The score well has a small lower central notch, echoing the approved housing.
            Band(vh,new Vector2(0,3),127,131,16,4,C(69,145,208),C(5,23,61));
            Well(vh,1.03f,C(54,97,144),C(34,77,127));
            Well(vh,1f,C(6,22,53),C(3,12,33));
            Gloss(vh);
            Accent(vh,0,137,39,2.5f,Violet);
            Accent(vh,0,-25,17,2f,Violet);
            Accent(vh,0,-140,40,2f,Violet);
            Accent(vh,-143,8,2f,43,Violet);
            Accent(vh,143,8,2f,43,Violet);
            // Controlled specular strips give the upper rim a polished highlight, not bloom.
            Accent(vh,0,154,103,.75f,C(215,255,255,210));
            Accent(vh,-145,72,.65f,50,C(185,255,255,180));
            Accent(vh,144,80,.65f,43,C(153,221,255,150));
            // A small raised lower plate and clearly contained dial wells make the housing
            // read as a single dimensional object, not circles pasted over its lower rim.
            Oct(vh,0,-151,42,8,7,C(70,123,171),C(3,15,35));
            Oct(vh,0,-152,39,6,5,C(16,42,77),C(4,15,34));
            DrawGauge(vh,new Vector2(-67,-75),timeProgress,Cyan,timePenalty,timeNotice);
            DrawGauge(vh,new Vector2(67,-75),moveProgress,Violet,movePenalty,moveNotice);
        }

        void DrawGauge(VertexHelper vh,Vector2 c,float progress,Color normal,bool penalty,float notice)
        {
            const float dial = .85f;
            Disc(vh,c + new Vector2(0,-2),62*dial,C(1,7,18),C(0,3,12));
            Disc(vh,c,61*dial,C(97,146,185),C(8,29,57));
            Disc(vh,c,59.5f*dial,C(5,18,40),C(1,8,22));
            Disc(vh,c,57*dial,C(35,66,105),C(5,20,45));
            Disc(vh,c,54.5f*dial,C(1,8,24),C(7,18,39));
            Disc(vh,c,49*dial,C(8,22,51),C(3,11,30));
            float p = Pulse(notice);
            Color lit = penalty ? Color.Lerp(normal,Coral,notice < 0f ? 1f : Mathf.Clamp01(notice / .18f)) : normal;
            // Fractions are actual data: 12/32 is exactly 37.5%, clockwise from twelve o'clock.
            float fill = Mathf.Clamp01(progress);
            if (fill > .0001f)
            {
                Arc(vh,c,52*dial,(7.4f + p * 3.0f)*dial,fill,WithAlpha(lit,.20f + p * .20f));
                Arc(vh,c,52*dial,(4.7f + p * .8f)*dial,fill,lit);
                Arc(vh,c,51.15f*dial,1.05f*dial,fill,Color.Lerp(lit,Color.white,.65f));
            }
        }
        static Color WithAlpha(Color c,float a) { c.a=a; return c; }
        static Color C(byte r,byte g,byte b,byte a=255)=>new Color32(r,g,b,a);
        Vector2 Scale(Vector2 p)=>new Vector2(p.x * PanelWidth / 300f,p.y * PanelHeight / 324f);
        Vector2 Local(Vector2 p)=>rectTransform.rect.center + new Vector2(
            p.x * rectTransform.rect.width / 300f,p.y * rectTransform.rect.height / 324f);
        void V(VertexHelper vh,Vector2 p,Color c)=>vh.AddVert(Local(p),c * color,Vector2.zero);

        static Vector2 Corner(int i,float hx,float hy,float cut)
        {
            switch(i) {
                case 0:return new Vector2(-hx+cut,hy); case 1:return new Vector2(hx-cut,hy);
                case 2:return new Vector2(hx,hy-cut); case 3:return new Vector2(hx,-hy+cut);
                case 4:return new Vector2(hx-cut,-hy); case 5:return new Vector2(-hx+cut,-hy);
                case 6:return new Vector2(-hx,-hy+cut); default:return new Vector2(-hx,hy-cut);
            }
        }
        void Oct(VertexHelper vh,float x,float y,float hx,float hy,float cut,Color top,Color bottom)
        {
            int s=vh.currentVertCount; Vector2 c=new Vector2(x,y);
            V(vh,c,Color.Lerp(bottom,top,.5f));
            for(int i=0;i<8;i++) {Vector2 p=Corner(i,hx,hy,cut);V(vh,c+p,Color.Lerp(bottom,top,(p.y+hy)/(2*hy)));}
            for(int i=0;i<8;i++)vh.AddTriangle(s,s+1+i,s+1+(i+1)%8);
        }
        void Band(VertexHelper vh,Vector2 c,float hx,float hy,float cut,float width,Color top,Color bottom)
        {
            int s=vh.currentVertCount;
            for(int i=0;i<8;i++)
            {
                Vector2 p=Corner(i,hx,hy,cut),q=Corner(i,hx-width,hy-width,Mathf.Max(1,cut-width*.6f));
                float light=Mathf.Clamp01(.5f+p.y/(2*hy)-p.x/(hx*7));
                V(vh,c+p,Color.Lerp(bottom,top,light));
                V(vh,c+q,Color.Lerp(bottom,top,Mathf.Clamp01(light-.17f)));
            }
            for(int i=0;i<8;i++) {int a=s+i*2,b=s+(i+1)%8*2;vh.AddTriangle(a,b,a+1);vh.AddTriangle(a+1,b,b+1);}
        }
        void Well(VertexHelper vh,float scale,Color top,Color bottom)
        {
            Vector2[] p={new Vector2(-108,128),new Vector2(108,128),new Vector2(122,114),
                new Vector2(122,-6),new Vector2(106,-20),new Vector2(25,-20),new Vector2(17,-28),
                new Vector2(-17,-28),new Vector2(-25,-20),new Vector2(-106,-20),new Vector2(-122,-6),new Vector2(-122,114)};
            int s=vh.currentVertCount;V(vh,new Vector2(0,46),Color.Lerp(bottom,top,.5f));
            for(int i=0;i<p.Length;i++)V(vh,p[i]*scale,Color.Lerp(bottom,top,Mathf.InverseLerp(-28,128,p[i].y)));
            for(int i=0;i<p.Length;i++)vh.AddTriangle(s,s+i+1,s+1+(i+1)%p.Length);
        }
        void Accent(VertexHelper vh,float x,float y,float hx,float hy,Color c)
        {Oct(vh,x,y,hx,hy,Mathf.Min(hx,hy),Color.Lerp(c,Color.white,.2f),c);}
        void IsolationGutter(VertexHelper vh)
        {
            // Inner vertices are hidden by the opaque housing. The exposed portion fades from
            // near-black to transparent across 55 units, masking the rail without a hard edge.
            int s=vh.currentVertCount;
            Color inner=C(1,7,20,248),middle=C(1,7,20,176),outer=C(1,7,20,0);
            V(vh,new Vector2(145,170),inner); V(vh,new Vector2(175,170),middle);
            V(vh,new Vector2(175,-180),middle); V(vh,new Vector2(145,-180),inner);
            V(vh,new Vector2(200,170),outer); V(vh,new Vector2(200,-180),outer);
            vh.AddTriangle(s,s+1,s+2); vh.AddTriangle(s,s+2,s+3);
            vh.AddTriangle(s+1,s+4,s+5); vh.AddTriangle(s+1,s+5,s+2);
        }
        void Gloss(VertexHelper vh)
        {
            // A restrained glass reflection across the upper well gives the face real material
            // separation while keeping SCORE fully readable.
            int s=vh.currentVertCount;
            V(vh,new Vector2(-106,111),C(172,232,255,38));
            V(vh,new Vector2(93,111),C(116,204,255,25));
            V(vh,new Vector2(57,68),C(76,163,230,2));
            V(vh,new Vector2(-82,68),C(76,163,230,8));
            vh.AddTriangle(s,s+1,s+2);vh.AddTriangle(s,s+2,s+3);
        }
        void Disc(VertexHelper vh,Vector2 c,float radius,Color top,Color bottom)
        {
            const int steps=96;int s=vh.currentVertCount;
            V(vh,c,Color.Lerp(bottom,top,.5f));
            for(int i=0;i<steps;i++) {
                float a=i*Mathf.PI*2f/steps;Vector2 p=new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius;
                V(vh,c+p,Color.Lerp(bottom,top,(p.y/radius+1f)*.5f));
            }
            for(int i=0;i<steps;i++)vh.AddTriangle(s,s+i+1,s+1+(i+1)%steps);
        }
        void Arc(VertexHelper vh,Vector2 c,float radius,float width,float fill,Color tint)
        {
            int steps=Mathf.Max(1,Mathf.CeilToInt(128*fill)),s=vh.currentVertCount;
            for(int i=0;i<=steps;i++) {
                float a=Mathf.PI*.5f-i/(float)steps*fill*Mathf.PI*2f;
                Vector2 d=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
                V(vh,c+d*(radius+width*.5f),tint);V(vh,c+d*(radius-width*.5f),tint);
            }
            for(int i=0;i<steps;i++) {int a=s+i*2;vh.AddTriangle(a,a+2,a+1);vh.AddTriangle(a+1,a+2,a+3);}
            if(fill<.9999f) {
                float end=Mathf.PI*.5f-fill*Mathf.PI*2f;
                Disc(vh,c+Vector2.up*radius,width*.5f,tint,tint);
                Disc(vh,c+new Vector2(Mathf.Cos(end),Mathf.Sin(end))*radius,width*.5f,tint,tint);
            }
        }
    }
}
