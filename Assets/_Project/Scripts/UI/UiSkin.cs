using UnityEngine;

namespace Demo.UI
{
    /// <summary>
    /// UI 공용 스킨 — 글꼴 · 색 · 1픽셀 텍스처 · 그리기 헬퍼 [2026-10-13].
    ///
    /// <b>왜 만들었나.</b> 실측하니 UI 여섯 파일이 색을 <b>47번</b> 따로 박고 있었고,
    /// 똑같은 1픽셀 흰 텍스처를 <b>세 번</b> 따로 만들고 있었다. 색 하나를 바꾸려면
    /// 47군데를 뒤져야 하는 상태였다. 여기로 모으면 한 곳이 된다.
    ///
    /// <b>색은 새로 고르지 않았다.</b> 아래 상수는 전부 이미 코드에 박혀 있던 값을
    /// 그대로 옮긴 것이다 — 호박색은 HUD 진행 막대와 타이틀 버튼이 이미 같은 값을
    /// 쓰고 있었다. 이름만 붙였으므로 이 커밋만으로는 화면이 바뀌지 않는다.
    ///
    /// <b>글꼴만 새로 붙는다.</b> 배민 도현체·을지로체가 초기 커밋(9/22)부터
    /// 프로젝트에 들어와 있었는데 UI가 한 번도 쓰지 않고 있었다 [확인 2026-10-13].
    ///
    /// <b>왜 MonoBehaviour인가.</b> 글꼴은 에셋이라 코드에서 경로로 못 읽는다
    /// (Resources를 쓰면 되지만 이 프로젝트는 Resources를 안 쓴다). 그래서
    /// <see cref="Demo.Core.GameConfigRef"/>와 같은 모양으로 간다 —
    /// [Bootstrap]에 하나만 붙이고 어디서든 static으로 꺼내 쓴다.
    ///
    /// <b>글꼴이 안 물려 있어도 죽지 않는다.</b> GUIStyle.font가 null이면 유니티가
    /// 기본 글꼴로 그린다. 경고만 띄우고 넘어간다.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed partial class UiSkin : MonoBehaviour
    {
        [SerializeField, Tooltip("본문용. HUD·버튼·안내가 전부 이걸 쓴다 (배민 도현체)")]
        Font bodyFont;

        [SerializeField, Tooltip("제목용. 타이틀 로고와 결과 화면 제목에만 (배민 을지로체)")]
        Font titleFont;

        static Font s_body, s_title;
        static Texture2D s_px;

        /// <summary>본문 글꼴. 없으면 null이고, 그러면 유니티 기본 글꼴로 그려진다.</summary>
        public static Font Body => s_body;

        /// <summary>제목 글꼴. 큰 글자에만 쓴다 — 획이 불균일해 작은 글씨에서 읽기 나쁘다.</summary>
        public static Font Title => s_title;

        // ── 색 ────────────────────────────────────────────────────────────────
        // 전부 기존 코드에서 그대로 옮겨 온 값이다. 새로 고른 것은 하나도 없다.

        /// <summary>강조. 진행 막대·눌린 버튼. 호박색.</summary>
        public static readonly Color Accent = new Color(1f, 0.78f, 0.25f, 0.95f);

        /// <summary>위급. 남은 시간이 얼마 안 남았을 때.</summary>
        public static readonly Color Urgent = new Color(1f, 0.45f, 0.38f, 1f);

        /// <summary>글자 기본색.</summary>
        public static readonly Color Ink = Color.white;

        /// <summary>한 겹 죽인 글자. 부연 설명용.</summary>
        public static readonly Color InkDim = new Color(1f, 1f, 1f, 0.45f);

        /// <summary>화면을 덮는 장막. 타이틀·결과 화면 배경.</summary>
        public static readonly Color Veil = new Color(0.06f, 0.07f, 0.09f, 0.88f);

        /// <summary>글자 뒤에 까는 판. 지면이 밝아도 읽히게 한다.</summary>
        public static readonly Color Panel = new Color(0.04f, 0.05f, 0.07f, 0.55f);

        /// <summary>막대의 빈 부분.</summary>
        public static readonly Color Trough = new Color(0f, 0f, 0f, 0.45f);

        /// <summary>선. 판의 테두리.</summary>
        public static readonly Color Line = new Color(1f, 1f, 1f, 0.16f);

        void Awake()
        {
            s_body = bodyFont;
            s_title = titleFont;

            if (bodyFont == null)
                Debug.LogWarning("[UiSkin] 본문 글꼴이 안 물려 있다 — 유니티 기본 글꼴로 그린다. " +
                                 "[Bootstrap] 오브젝트의 인스펙터에서 연결할 것.", this);
        }

        void OnDestroy()
        {
            if (s_body == bodyFont) s_body = null;
            if (s_title == titleFont) s_title = null;
        }

        // ── 그리기 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 공용 1픽셀 흰 텍스처. 늘려서 네모를 칠하는 데 쓴다.
        ///
        /// 전에는 HUD·TitleScreen·TouchStrike가 각자 하나씩 만들었다. 한 장이면 된다.
        /// </summary>
        public static Texture2D Px
        {
            get
            {
                if (s_px != null) return s_px;
                s_px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                s_px.SetPixel(0, 0, Color.white);
                s_px.Apply();
                s_px.hideFlags = HideFlags.HideAndDontSave;
                return s_px;
            }
        }

        /// <summary>네모를 칠한다.</summary>
        public static void Fill(Rect r, Color c)
        {
            Color keep = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Px);
            GUI.color = keep;
        }

        /// <summary>
        /// 글자 뒤에 판을 깔고 테두리를 두른다.
        ///
        /// 전에는 글자마다 2픽셀 그림자만 깔았는데, 지면이 밝은 자리에서는
        /// 그림자만으로 모자란다 — 판을 깔면 배경을 모르는 채로도 읽힌다.
        /// </summary>
        public static void Plate(Rect r, float pad)
        {
            var box = new Rect(r.x - pad, r.y - pad, r.width + pad * 2f, r.height + pad * 2f);
            Fill(box, Panel);
            Outline(box, Line, 1f);
        }

        /// <summary>네모의 테두리만 그린다.</summary>
        public static void Outline(Rect r, Color c, float w)
        {
            Fill(new Rect(r.x, r.y, r.width, w), c);                      // 위
            Fill(new Rect(r.x, r.yMax - w, r.width, w), c);               // 아래
            Fill(new Rect(r.x, r.y, w, r.height), c);                     // 왼
            Fill(new Rect(r.xMax - w, r.y, w, r.height), c);              // 오른
        }

        /// <summary>
        /// 글자 뒤에 그림자를 깔아 그린다. 배경이 밝든 어둡든 읽히게 하는 제일 싼 방법이다.
        /// </summary>
        public static void Shadowed(Rect r, string text, GUIStyle s)
        {
            Color keep = s.normal.textColor;
            float off = Mathf.Max(2f, s.fontSize * 0.06f);

            s.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(r.x + off, r.y + off, r.width, r.height), text, s);

            s.normal.textColor = keep;
            GUI.Label(r, text, s);
        }

        /// <summary>진행 막대. 빈 부분 · 찬 부분 · 테두리.</summary>
        public static void Bar(Rect r, float fill01, Color fg)
        {
            Fill(r, Trough);
            float w = r.width * Mathf.Clamp01(fill01);
            if (w > 0f) Fill(new Rect(r.x, r.y, w, r.height), fg);
            Outline(r, Line, 1f);
        }

        /// <summary>
        /// 칸을 점으로 보여 준다 — 찬 칸은 꽉 찬 네모, 빈 칸은 테두리만.
        ///
        /// 숫자 "3단계"보다 ■■■□□□ 쪽이 곁눈으로 읽힌다. HUD는 곁눈으로 보는 것이다.
        /// </summary>
        public static void Pips(Rect r, int filled, int total, Color fg)
        {
            if (total <= 0) return;
            float gap = r.height * 0.35f;
            float w = (r.width - gap * (total - 1)) / total;
            if (w <= 0f) return;

            for (int i = 0; i < total; i++)
            {
                var cell = new Rect(r.x + (w + gap) * i, r.y, w, r.height);
                if (i < filled) Fill(cell, fg);
                else Outline(cell, Line, 1f);
            }
        }

        /// <summary>
        /// 버튼. 타이틀 화면이 쓰던 모양 그대로다 — 올리면 호박색이 차고 글자가 어두워진다.
        ///
        /// <b>그리기와 누르기를 나눈다.</b> 그리기는 Repaint에서만 하고, 누름 판정은
        /// 보이지 않는 버튼에 맡긴다. 한데 묶으면 Layout 단계에서 색이 계산돼
        /// 마우스 위치가 한 프레임 늦는다.
        /// </summary>
        public static bool Button(Rect r, string text, GUIStyle s)
        {
            bool over = r.Contains(Event.current.mousePosition);

            if (Event.current.type == EventType.Repaint)
            {
                Fill(r, over ? Accent : new Color(1f, 1f, 1f, 0.14f));
                Outline(r, Line, 1f);

                Color keepColor = s.normal.textColor;
                TextAnchor keepAlign = s.alignment;
                s.normal.textColor = over ? new Color(0.1f, 0.08f, 0.04f) : Ink;
                s.alignment = TextAnchor.MiddleCenter;
                GUI.Label(r, text, s);
                s.alignment = keepAlign;
                s.normal.textColor = keepColor;
            }

            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        /// <summary>본문 스타일 하나. 크기는 부르는 쪽이 매 프레임 정한다.</summary>
        public static GUIStyle Label(bool bold, TextAnchor anchor)
        {
            return new GUIStyle(GUI.skin.label)
            {
                font = Body,
                alignment = anchor,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                wordWrap = false,
            };
        }

        /// <summary>제목 스타일. 큰 글자 전용이다.</summary>
        public static GUIStyle Heading(TextAnchor anchor)
        {
            return new GUIStyle(GUI.skin.label)
            {
                font = Title != null ? Title : Body,
                alignment = anchor,
                fontStyle = FontStyle.Normal,
                wordWrap = false,
            };
        }
    }
}
