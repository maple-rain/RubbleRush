using Demo.Collection;
using Demo.Core;
using UnityEngine;

namespace Demo.UI
{
    /// <summary>
    /// 인게임 HUD — 남은 시간, 할당량 진행, 견인 적재량 [구현 2026-10-12].
    ///
    /// <b>디버그 오버레이와 무엇이 다른가.</b> <see cref="Utils.DebugHotkeys"/>의 F1 오버레이는
    /// 값을 전부 글자로 늘어놓는다 — 검증용이라 그게 맞다. 이쪽은 플레이어가 <b>곁눈으로</b>
    /// 읽는 것이라 셋만 남기고, 숫자보다 막대의 길이로 말한다.
    ///
    /// <b>Canvas를 안 쓴다.</b> <see cref="TouchControls"/>와 같은 이유다 — 프리팹과 씬
    /// 와이어링은 사람이 손으로 하는 작업이고 (CLAUDE.md 규칙 1), 요소 셋에 EventSystem
    /// 한 겹을 얹을 이유가 없다. TextMeshPro로 가는 길은 열려 있다.
    ///
    /// 설계상 주의할 것 둘:
    /// <list type="bullet">
    /// <item>체력바가 없다. 자원은 <b>타이머 하나</b>뿐이다 — 피해는 전부 시간 손실로 환산된다
    ///       (GAME_DESIGN §6). UI가 가벼워지는 것이 이 선택의 배당금이니 도로 늘리지 말 것.</item>
    /// <item><b>경험치는 화면에 보이지 않는다</b> [확정]. 플레이어는 숫자가 아니라 구멍이 커지는
    ///       것으로 진행을 읽는다. 단 이 결정은 추후 번복될 수 있다고 명시돼 있으므로,
    ///       경험치 바를 넣을 자리는 비워 두되 기본은 꺼 둔다.</item>
    /// </list>
    ///
    /// <b>크기를 화면 세로에 비례시킨다.</b> 폰은 해상도가 제각각이라 고정 pt로 적으면
    /// 기종마다 글자가 깨알이거나 화면을 덮는다.
    /// </summary>
    public sealed class HUD : MonoBehaviour
    {
        [SerializeField, Tooltip("경험치 바를 보여줄지. 기본은 꺼짐 — 설계상 보이지 않는 값이다")]
        bool showXpBar = false;

        [SerializeField, Range(0.02f, 0.08f), Tooltip("기본 글자 크기. 화면 세로 대비")]
        float textFraction = 0.034f;

        [SerializeField, Range(0.2f, 0.6f), Tooltip("진행 막대의 가로 길이. 화면 가로 대비")]
        float barFraction = 0.30f;

        [SerializeField, Tooltip("남은 시간이 이 아래로 내려가면 붉어진다(초)")]
        float urgentSeconds = 30f;

        /// <summary>경험치 바를 그릴지. 이 결정은 추후 번복될 수 있어 스위치로 남겨 둔다.</summary>
        public bool ShowXpBar { get => showXpBar; set => showXpBar = value; }

        GameLoop loop;
        Hole hole;
        CargoTow cargo;

        GUIStyle big, small;
        Texture2D px;

        void Awake()
        {
            loop = GameLoop.Instance;
            cargo = FindFirstObjectByType<CargoTow>();

            px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            px.SetPixel(0, 0, Color.white);
            px.Apply();
            px.hideFlags = HideFlags.HideAndDontSave;
        }

        void OnDestroy()
        {
            if (px != null) Destroy(px);
        }

        void Update()
        {
            if (loop == null) loop = GameLoop.Instance;
            if (hole == null && loop != null) hole = loop.ActiveHole;
            if (cargo == null) cargo = FindFirstObjectByType<CargoTow>();
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || loop == null) return;
            if (TitleScreen.Blocking) return;
            if (big == null) Styles();

            float h = Screen.height;
            float pad = h * 0.035f;
            float line = h * textFraction;
            float barW = Screen.width * barFraction;
            float barH = h * 0.018f;

            big.fontSize = Mathf.RoundToInt(line);
            small.fontSize = Mathf.RoundToInt(line * 0.72f);

            // ── 남은 시간. 유일한 실패 조건이라 제일 크게 ──────────────────────
            bool urgent = loop.TimeRemaining <= urgentSeconds;
            big.normal.textColor = urgent ? new Color(1f, 0.45f, 0.38f) : Color.white;
            string t = Mathf.FloorToInt(loop.TimeRemaining / 60f).ToString("0")
                     + ":" + Mathf.FloorToInt(loop.TimeRemaining % 60f).ToString("00");
            Shadowed(new Rect(pad, pad, barW, line * 1.2f), t, big);

            // ── 할당량. 이 게임의 '퀘스트 진행도'다 ─────────────────────────────
            float y = pad + line * 1.45f;
            small.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
            Shadowed(new Rect(pad, y, barW, line), "할당량  " + loop.Collected + " / " + loop.Quota, small);

            y += line * 1.05f;
            Bar(new Rect(pad, y, barW, barH), loop.Progress01,
                new Color(1f, 0.78f, 0.25f, 0.95f));

            // ── 구멍 단계. 숫자가 아니라 '무엇이 들어가는가'의 표현이다 ────────
            if (hole != null)
            {
                y += barH + h * 0.012f;
                Shadowed(new Rect(pad, y, barW, line), "구멍  " + (hole.Level + 1) + "단계  "
                         + hole.Radius.ToString("0.0") + "m", small);
            }

            // ── 견인 적재량. 지금 무엇을 끌고 있는가 ────────────────────────────
            if (cargo != null && cargo.TotalUnits > 0)
            {
                float bw = Screen.width * 0.22f;
                var r = new Rect(Screen.width - pad - bw, pad, bw, line);
                small.alignment = TextAnchor.MiddleRight;
                Shadowed(r, "끄는 중  " + cargo.TotalUnits + "단위", small);
                small.alignment = TextAnchor.MiddleLeft;
            }
        }

        void Styles()
        {
            big = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                wordWrap = false,
            };
            small = new GUIStyle(big) { fontStyle = FontStyle.Normal };
        }

        /// <summary>
        /// 글자 뒤에 그림자를 깐다. 지면이 밝아서 흰 글자만 올리면 안 보이는 자리가 생긴다 —
        /// 배경을 모르는 채로 읽히게 하는 제일 싼 방법이다.
        /// </summary>
        void Shadowed(Rect r, string text, GUIStyle s)
        {
            Color keep = s.normal.textColor;
            s.normal.textColor = new Color(0f, 0f, 0f, 0.55f);
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, s);
            s.normal.textColor = keep;
            GUI.Label(r, text, s);
        }

        void Bar(Rect r, float fill01, Color color)
        {
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(r, px);
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fill01), r.height), px);
            GUI.color = prev;
        }
    }
}
