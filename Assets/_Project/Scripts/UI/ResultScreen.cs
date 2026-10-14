using System.Collections.Generic;
using Demo.Collection;
using Demo.Core;
using Demo.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Demo.UI
{
    /// <summary>
    /// 구역 종료 화면 — 클리어/시간 초과와 항목별 내역 [구현 2026-10-13].
    ///
    /// <b>전에는 아무것도 안 나왔다.</b> <see cref="GameLoop"/>가 콘솔에 한 줄 찍고 끝나서,
    /// 구역이 끝나도 화면이 그냥 멈춰 있을 뿐이었다.
    ///
    /// <b>내역을 목록으로 들고 있는 이유.</b> 이 화면은 나중에 점수와 랭킹이 붙는
    /// 스코어보드가 된다 [기획자 2026-10-13]. 행을 <see cref="Row"/> 목록으로 쌓아 두면
    /// 점수·등급·순위를 넣을 때 그리기 코드를 안 건드리고 <b>행 하나를 더 넣으면</b> 된다.
    ///
    /// <b>시간을 멈춘다.</b> 구역이 끝나도 로봇은 계속 움직일 수 있어서,
    /// 결과를 보는 동안 뒤에서 화면이 흔들린다. timeScale을 0으로 두고 나갈 때 되돌린다.
    ///
    /// <b>아직 없는 것</b>: 점수 · 등급 · 랭킹 · "다음 구역". 다음 구역은 스테이지 목록이
    /// 데이터라 순서를 아직 정하지 않았다 — 지금은 스테이지 고르기로 돌려보낸다.
    /// </summary>
    public sealed class ResultScreen : MonoBehaviour
    {
        [SerializeField, Tooltip("스테이지 고르기로 돌아갈 때 불러올 씬 이름")]
        string menuScene = "Start";

        [SerializeField, Range(0.02f, 0.08f), Tooltip("내역 글자 크기. 화면 세로 대비")]
        float textFraction = 0.034f;

        /// <summary>내역 한 줄. 왼쪽에 이름, 오른쪽에 값.</summary>
        struct Row
        {
            public string label;
            public string value;
            public bool strong;     // 강조할 줄 (호박색)
        }

        readonly List<Row> rows = new List<Row>();

        bool shown;
        string headline;
        bool cleared;
        float keepTimeScale = 1f;

        int score;
        int stars;
        int maxStars;
        GameConfig.Grade grade;

        GUIStyle head, num, body;

        /// <summary>떠 있는 동안인가. 다른 UI가 자기를 숨길지 판단하는 데 쓴다.</summary>
        public static bool Blocking { get; private set; }

        /// <summary>GameLoop가 구역을 끝내면서 부른다.</summary>
        public void Show(GameLoop.Result result, int collected, int quota,
                         float timeRemaining, int structuresBroken, Hole hole)
        {
            if (shown) return;
            shown = true;
            Blocking = true;

            cleared = result == GameLoop.Result.Cleared;
            headline = cleared ? "철거 완료" : "시간 초과";

            // ── 점수와 별. 화면의 주인공이다 [지시 2026-10-13] ────────────────
            GameConfig cfg = GameConfigRef.Current;
            int grown = hole != null ? hole.Level : 0;      // 키운 칸 수 (시작은 0)
            score = Score.Of(cfg, collected, quota, timeRemaining, structuresBroken, grown);
            grade = Score.GradeOf(cfg, score);
            maxStars = Score.MaxStars(cfg);

            // 실패하면 별은 0개다 [지시 2026-10-14]. 점수만으로는 가를 수 없다 —
            // 건물만 많이 부수고 못 나르면 떨어져도 점수가 높게 나온다.
            stars = cleared && grade != null ? Mathf.Clamp(grade.stars, 0, maxStars) : 0;

            // ── 내역. 점수가 어디서 나왔는지 보이는 자리다 ─────────────────────
            rows.Clear();
            rows.Add(new Row { label = "수집", value = collected + " / " + quota });
            rows.Add(new Row { label = "남은 시간", value = Clock(timeRemaining) });
            rows.Add(new Row { label = "부순 건물", value = structuresBroken + "채" });

            if (hole != null)
                rows.Add(new Row
                {
                    label = "구멍",
                    value = (hole.Level + 1) + "단계 · " + hole.Radius.ToString("0.0") + "m"
                });

            // 순위는 아직 없다. 스코어보드가 생기면 여기에 한 줄 더 넣으면 된다
            // [기획자 2026-10-13] — 그리기 코드는 안 건드린다.

            keepTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        void OnDestroy()
        {
            if (Blocking) Time.timeScale = keepTimeScale;
            Blocking = false;
        }

        static string Clock(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            return Mathf.FloorToInt(seconds / 60f).ToString("0")
                 + ":" + Mathf.FloorToInt(seconds % 60f).ToString("00");
        }

        void OnGUI()
        {
            if (!shown) return;
            if (head == null || head.font != (UiSkin.Title != null ? UiSkin.Title : UiSkin.Body))
                Styles();

            float w = Screen.width, h = Screen.height;
            float line = h * textFraction;

            head.fontSize = Mathf.RoundToInt(h * 0.085f);
            num.fontSize = Mathf.RoundToInt(h * 0.085f);
            body.fontSize = Mathf.RoundToInt(line);

            UiSkin.Fill(new Rect(0f, 0f, w, h), UiSkin.Veil);

            // ── 제목. 클리어는 호박색, 실패는 붉은색 ───────────────────────────
            head.normal.textColor = cleared ? UiSkin.Accent : UiSkin.Urgent;
            UiSkin.Shadowed(new Rect(0f, h * 0.08f, w, h * 0.11f), headline, head);

            // ── 별. 제일 먼저 눈에 들어와야 하는 것이다 ────────────────────────
            if (maxStars > 0)
            {
                UiSkin.Stars(new Rect(w * 0.3f, h * 0.21f, w * 0.4f, h * 0.10f), stars, maxStars);
            }

            // ── 점수 ───────────────────────────────────────────────────────────
            num.normal.textColor = UiSkin.Ink;
            UiSkin.Shadowed(new Rect(0f, h * 0.33f, w, h * 0.10f), score.ToString("N0"), num);

            if (grade != null && !string.IsNullOrEmpty(grade.label))
            {
                body.alignment = TextAnchor.MiddleCenter;
                body.normal.textColor = UiSkin.InkDim;
                UiSkin.Shadowed(new Rect(0f, h * 0.43f, w, line * 1.4f), grade.label, body);
                body.alignment = TextAnchor.MiddleLeft;
            }

            // ── 내역 ───────────────────────────────────────────────────────────
            float tableW = w * 0.42f;
            float x = (w - tableW) * 0.5f;
            float rowH = line * 1.75f;
            float y = h * 0.50f;

            UiSkin.Plate(new Rect(x, y, tableW, rowH * rows.Count), line * 0.6f);

            for (int i = 0; i < rows.Count; i++)
            {
                Row r = rows[i];
                var cell = new Rect(x, y + rowH * i, tableW, rowH);

                if (i > 0) UiSkin.Fill(new Rect(cell.x, cell.y, cell.width, 1f), UiSkin.Line);

                body.alignment = TextAnchor.MiddleLeft;
                body.normal.textColor = UiSkin.InkDim;
                UiSkin.Shadowed(cell, r.label, body);

                body.alignment = TextAnchor.MiddleRight;
                body.normal.textColor = r.strong ? UiSkin.Accent : UiSkin.Ink;
                UiSkin.Shadowed(cell, r.value, body);
            }

            body.alignment = TextAnchor.MiddleLeft;

            // ── 버튼 둘 ────────────────────────────────────────────────────────
            float bw = w * 0.19f, bh = h * 0.095f, gap = w * 0.02f;
            float bx = (w - (bw * 2f + gap)) * 0.5f;
            float by = h * 0.80f;

            if (UiSkin.Button(new Rect(bx, by, bw, bh), "다시 하기", body)) Again();
            if (UiSkin.Button(new Rect(bx + bw + gap, by, bw, bh), "스테이지", body)) ToMenu();
        }

        /// <summary>
        /// 제목은 을지로체, 점수와 내역은 도현체다 [지시 2026-10-13].
        /// 숫자를 을지로체로 두면 자릿수가 들쭉날쭉해 읽기 나쁘다.
        /// </summary>
        void Styles()
        {
            head = UiSkin.Heading(TextAnchor.MiddleCenter);
            num = UiSkin.Label(true, TextAnchor.MiddleCenter);
            body = UiSkin.Label(false, TextAnchor.MiddleLeft);
        }

        /// <summary>같은 구역을 다시. 씬을 다시 읽는 것이 제일 확실하다 — 남은 상태가 없다.</summary>
        void Again()
        {
            Leave();
            Scene s = SceneManager.GetActiveScene();
            SceneManager.LoadScene(s.buildIndex);
        }

        void ToMenu()
        {
            Leave();
            if (string.IsNullOrEmpty(menuScene))
            {
                Debug.LogError("[ResultScreen] 돌아갈 씬 이름이 비어 있다.", this);
                return;
            }
            SceneManager.LoadScene(menuScene);
        }

        /// <summary>씬을 떠나기 전에 시간을 되돌린다. 안 그러면 다음 씬이 멈춘 채 시작한다.</summary>
        void Leave()
        {
            Time.timeScale = keepTimeScale <= 0f ? 1f : keepTimeScale;
            Blocking = false;
            shown = false;
        }
    }
}
