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

        void Awake()
        {
            loop = GameLoop.Instance;
            cargo = FindFirstObjectByType<CargoTow>();
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
            if (TitleScreen.Blocking || ResultScreen.Blocking || UpgradeScreen.Blocking) return;
            if (big == null || big.font != UiSkin.Body) Styles();

            float h = Screen.height;
            float pad = h * 0.035f;
            float line = h * textFraction;
            float barW = Screen.width * barFraction;
            float barH = h * 0.018f;
            float gap = h * 0.010f;

            big.fontSize = Mathf.RoundToInt(line);
            small.fontSize = Mathf.RoundToInt(line * 0.72f);

            float timeH = line * 1.2f;
            float pipsH = line * 0.42f;
            float gradeLabelH = line * 0.78f;   // 등급 글자가 앉는 띄. 막대 바로 위다
            int levels = Levels();
            bool showPips = hole != null && levels > 0;

            // 판을 먼저 깔아 둔다. 지면이 밝은 자리에서는 글자 그림자만으로 모자란다.
            float blockH = timeH + gap + line + gap * 0.5f + gradeLabelH + barH
                         + (showPips ? gap + pipsH : 0f);
            UiSkin.Plate(new Rect(pad, pad, barW, blockH), pad * 0.4f);

            float y = pad;

            // ── 남은 시간. 유일한 실패 조건이라 제일 크게 ──────────────────────
            bool urgent = loop.TimeRemaining <= urgentSeconds;
            big.normal.textColor = urgent ? UiSkin.Urgent : UiSkin.Ink;
            string t = Mathf.FloorToInt(loop.TimeRemaining / 60f).ToString("0")
                     + ":" + Mathf.FloorToInt(loop.TimeRemaining % 60f).ToString("00");
            UiSkin.Shadowed(new Rect(pad, y, barW, timeH), t, big);
            y += timeH + gap;

            // ── 점수와 등급. 할당량 막대를 대신한다 [지시 2026-10-15] ──────────
            //
            // 할당량 막대는 60단위를 채우면 끝이라 그 뒤로 아무것도 안 알려 줬다.
            // 점수는 판이 끝날 때까지 계속 움직이고, 눈금이 "다음 글자까지 얼마"를 말한다.
            small.normal.textColor = UiSkin.Ink;
            UiSkin.Shadowed(new Rect(pad, y, barW, line),
                            "점수  " + loop.CurrentScore.ToString("N0"), small);
            y += line + gap * 0.5f;

            UiSkin.GradeBar(new Rect(pad, y + gradeLabelH, barW, barH),
                            new Rect(pad, y, barW, gradeLabelH),
                            loop.CurrentScore, Grades(), small);
            y += gradeLabelH + barH;

            // ── 구멍 단계. 숫자보다 칸이 곁눈으로 읽힌다 ───────────────────────
            if (showPips)
            {
                y += gap;
                float labelW = barW * 0.44f;
                small.normal.textColor = UiSkin.InkDim;
                UiSkin.Shadowed(new Rect(pad, y - line * 0.29f, labelW, line),
                                "구멍  " + hole.Radius.ToString("0.0") + "m", small);
                UiSkin.Pips(new Rect(pad + labelW, y, barW - labelW, pipsH),
                            hole.Level + 1, levels, UiSkin.Accent);
            }

            // ── 견인 적재량. 지금 무엇을 끌고 있는가 ────────────────────────────
            if (cargo != null && cargo.TotalUnits > 0)
            {
                float bw = Screen.width * 0.22f;
                var r = new Rect(Screen.width - pad - bw, pad, bw, line);
                UiSkin.Plate(r, pad * 0.35f);
                small.normal.textColor = UiSkin.Ink;
                small.alignment = TextAnchor.MiddleRight;
                UiSkin.Shadowed(r, "끄는 중  " + cargo.TotalUnits + "단위", small);
                small.alignment = TextAnchor.MiddleLeft;
            }
        }

        /// <summary>구멍 사다리가 몇 칸인가. 칸 표시의 총 개수다.</summary>
        static int Levels()
        {
            var cfg = GameConfigRef.Current;
            return cfg != null && cfg.holeLevels != null ? cfg.holeLevels.Length : 0;
        }

        /// <summary>
        /// 등급표. 비어 있으면 null이고, 그때 막대는 눈금 없이 빈 채로 선다 —
        /// <see cref="UiSkin.GradeBar"/>가 그렇게 그린다. 기획자가 표를 채우기 전까지는
        /// 거짓 눈금을 세우지 않는다.
        /// </summary>
        static Data.GameConfig.Grade[] Grades()
        {
            var cfg = GameConfigRef.Current;
            return cfg != null ? cfg.grades : null;
        }

        /// <summary>글꼴·색·그리기는 전부 <see cref="UiSkin"/>이 가진다.</summary>
        void Styles()
        {
            big = UiSkin.Label(true, TextAnchor.MiddleLeft);
            small = UiSkin.Label(false, TextAnchor.MiddleLeft);
        }
    }
}
