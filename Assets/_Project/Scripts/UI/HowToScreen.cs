using UnityEngine;

namespace Demo.UI
{
    /// <summary>
    /// 조작법 — 게임의 한 바퀴를 세 칸으로 보여 준다 [지시 2026-10-14].
    ///
    /// <b>이 게임은 설명 없이 알 수가 없다.</b> 부수고, 케이블로 둘러싸고, 구멍에 밀어넣는
    /// 세 단계가 서로 다른 조작이고 순서가 있다. 지금까지 그걸 말해 주는 화면이 아예 없어서,
    /// 처음 켠 사람은 건물을 부순 뒤 잔해를 어떻게 하는지 알 방법이 없었다.
    ///
    /// <b>그림이 없어도 돌아간다.</b> 그림은 VARCO로 따로 뽑아 물릴 예정이라,
    /// 비어 있으면 번호와 글자만 나온다 — 그림을 기다리느라 화면이 막히지 않게 한다.
    ///
    /// <b>타이틀에서만 연다.</b> 게임 중에 끼어들면 흐름이 끊긴다. 나중에 첫 판에 한 번
    /// 자동으로 띄우고 싶으면 <see cref="Open"/>를 그때 부르면 된다.
    ///
    /// Canvas를 안 쓰는 이유는 <see cref="TouchControls"/>와 같다 (CLAUDE.md 규칙 1).
    /// </summary>
    public sealed class HowToScreen : MonoBehaviour
    {
        [SerializeField, Tooltip("세 단계의 그림. 비면 글자만 나온다.\n" +
                 "순서: 부순다 · 둘러싼다 · 밀어넣는다")]
        Texture2D[] steps = new Texture2D[3];

        [SerializeField, Range(0.02f, 0.05f), Tooltip("설명 글자 크기. 화면 세로 대비")]
        float textFraction = 0.026f;

        static readonly string[] Titles = { "부순다", "둘러싼다", "밀어넣는다" };

        static readonly string[] Lines =
        {
            "건물 가까이에서 부수기를 누르면 화면이 바뀝니다.\n두드려서 무너뜨리세요.",
            "케이블을 켜고 잔해 둘레를 한 바퀴 도세요.\n고리가 닫히면 안에 든 것이 딸려 옵니다.",
            "끌고 구멍 위를 지나가면 들어갑니다.\n구멍은 삼킨 만큼 커집니다.",
        };

        static HowToScreen s_current;

        /// <summary>떠 있는 동안인가. 다른 UI가 자기를 숨기는 데 쓴다.</summary>
        public static bool Blocking { get; private set; }

        /// <summary>어디서든 연다. 화면이 씬에 없으면 아무 일도 안 한다.</summary>
        public static void Open()
        {
            if (s_current == null) s_current = FindFirstObjectByType<HowToScreen>();
            if (s_current != null) Blocking = true;
        }

        GUIStyle head, title, body;

        void Awake() => s_current = this;

        void OnDestroy()
        {
            if (s_current == this) s_current = null;
            Blocking = false;
        }

        void OnGUI()
        {
            if (!Blocking) return;
            if (title == null || title.font != UiSkin.Body) Styles();

            float w = Screen.width, h = Screen.height;
            float line = h * textFraction;

            head.fontSize = Mathf.RoundToInt(h * 0.07f);
            title.fontSize = Mathf.RoundToInt(line * 1.7f);
            body.fontSize = Mathf.RoundToInt(line);

            UiSkin.Fill(new Rect(0f, 0f, w, h), UiSkin.Veil);

            head.normal.textColor = UiSkin.Accent;
            UiSkin.Shadowed(new Rect(0f, h * 0.07f, w, h * 0.10f), "조작법", head);

            // ── 세 칸 ──────────────────────────────────────────────────────────
            float gap = w * 0.025f;
            float cw = Mathf.Min(w * 0.27f, (w * 0.88f - gap * 2f) / 3f);
            float ch = h * 0.54f;
            float x = (w - (cw * 3f + gap * 2f)) * 0.5f;
            float y = h * 0.21f;

            for (int i = 0; i < 3; i++)
                Step(new Rect(x + (cw + gap) * i, y, cw, ch), i, line);

            // ── 닫기 ───────────────────────────────────────────────────────────
            float bw = w * 0.18f, bh = h * 0.085f;
            if (UiSkin.Button(new Rect((w - bw) * 0.5f, h * 0.81f, bw, bh), "닫기", body))
                Blocking = false;
        }

        /// <summary>
        /// 한 칸. 위에 그림(없으면 번호), 아래에 제목과 한 줄.
        ///
        /// 그림 자리는 <b>비어 있어도 같은 크기를 차지한다</b> — 그래야 그림이 들어온 뒤에
        /// 배치가 흔들리지 않는다.
        /// </summary>
        void Step(Rect r, int i, float line)
        {
            if (Event.current.type != EventType.Repaint) return;

            UiSkin.Fill(r, UiSkin.Panel);
            UiSkin.Outline(r, UiSkin.Line, 1f);

            float pad = r.width * 0.07f;
            var art = new Rect(r.x + pad, r.y + pad, r.width - pad * 2f, r.height * 0.46f);

            Texture2D tex = steps != null && i < steps.Length ? steps[i] : null;
            if (tex != null)
            {
                GUI.DrawTexture(art, tex, ScaleMode.ScaleAndCrop);
                UiSkin.Outline(art, UiSkin.Line, 1f);
            }
            else
            {
                // 그림이 아직 없다 — 번호로 자리를 지킨다
                UiSkin.Fill(art, new Color(1f, 1f, 1f, 0.04f));
                head.normal.textColor = new Color(1f, 1f, 1f, 0.18f);
                int keep = head.fontSize;
                head.fontSize = Mathf.RoundToInt(art.height * 0.55f);
                GUI.Label(art, (i + 1).ToString(), head);
                head.fontSize = keep;
            }

            float ty = art.yMax + line * 0.7f;
            title.normal.textColor = UiSkin.Ink;
            UiSkin.Shadowed(new Rect(r.x + pad, ty, r.width - pad * 2f, line * 2f), Titles[i], title);

            body.normal.textColor = UiSkin.InkDim;
            body.wordWrap = true;
            GUI.Label(new Rect(r.x + pad, ty + line * 2.2f, r.width - pad * 2f, r.height * 0.3f),
                      Lines[i], body);
            body.wordWrap = false;
        }

        void Styles()
        {
            head = UiSkin.Label(true, TextAnchor.MiddleCenter);
            title = UiSkin.Label(true, TextAnchor.MiddleCenter);
            body = UiSkin.Label(false, TextAnchor.UpperCenter);
        }
    }
}
