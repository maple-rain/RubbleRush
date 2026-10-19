using Demo.Core;
using Demo.Data;
using UnityEngine;

namespace Demo.UI
{
    /// <summary>
    /// 처음 잡는 사람에게 <b>순서대로</b> 가르친다 [지시 2026-10-17].
    ///
    /// 기획자: "튜토리얼이 총 3분정도의 분량이 될 수 있도록 수정하자."
    /// 물어보니 <b>가르치는 튜토리얼</b>을 원했다 — 분량만 줄이는 쪽이 아니라.
    ///
    /// <b>그전에는 튜토리얼이 없었다.</b> 스테이지 목록의 "튜토리얼"은 테스트 구역에
    /// 이름만 붙인 것이었다 [실측 2026-10-17: ZoneData 가 <c>Zone_TEMP_Test</c> 하나뿐].
    /// 안내라고는 <see cref="ActionPrompt"/>가 대상 옆에 띄우는 "부수기"가 전부라,
    /// <b>무엇부터 해야 하는지</b>는 아무도 알려주지 않았다.
    ///
    /// <b>씬을 안 건드린다.</b> <see cref="GameLoop.StartZone"/>이 한 줄로 부르고,
    /// 여기서 스스로 오브젝트를 만든다 — <see cref="HitFlash"/>와 같은 수법이다.
    /// 씬 와이어링은 사람 몫이라 (CLAUDE.md 규칙 1) 코드가 소유할 수 있는 쪽을 고른다.
    ///
    /// <b>시간을 잰다.</b> 기획자가 "한 판을 끝까지 해 본 적이 없다"고 했다
    /// [2026-10-17]. 이동·견인 시간은 CSV 로 계산이 안 되므로 — 순수 두드리기는
    /// 740탭이라 초당 8타로 1.5분이 나오지만 나머지는 알 수 없다 — <b>단계마다
    /// 실제로 걸린 시간을 적어 두고</b> 끝에 표로 찍는다. 3분이 맞는 분량인지는
    /// 그 표를 보고 정한다.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed partial class TutorialFlow : MonoBehaviour
    {
        // ── 어디서 켜지는가 ──────────────────────────────────────────────────

        /// <summary>이 구역에서만 튜토리얼이 돈다. 다른 구역에서는 아예 안 만들어진다.</summary>
        const string TutorialZoneId = "zone_test";          // [제안·미승인 2026-10-17]

        /// <summary>끝난 단계를 몇 초 동안 더 보여 주는가.</summary>
        const float DoneHold = 1.4f;                        // [제안·미승인]

        /// <summary>단계가 바뀌고 이만큼은 조건을 안 본다. 직전 동작이 다음 단계를
        /// 곧바로 끝내 버리는 것을 막는다 — 예: 잔해를 넣자마자 '구멍을 키워라'가
        /// 이미 충족돼 글만 번쩍이고 지나간다.</summary>
        const float Settle = 0.35f;                         // [제안·미승인]

        // ── 단계 ────────────────────────────────────────────────────────────

        enum Goal
        {
            Move,              // Amount 미터만큼 움직인다
            EnterDemolition,   // 부수기 화면에 들어간다
            Break,             // Amount 채를 부순다
            Capture,           // 케이블로 Amount 단위를 담는다
            Deliver,           // 구멍에 Amount 단위를 넣는다
            HoleLevel,         // 구멍이 Amount 단계가 된다
            Free,              // 끝나지 않는다 — 남은 시간은 마음대로
        }

        struct Step
        {
            public Goal Goal;
            public float Amount;
            public string Line;     // 하는 동안 띄울 말
            public string Done;     // 끝났을 때 잠깐 띄울 말
        }

        /// <summary>
        /// <b>문구와 목표치는 전부 [제안·미승인 2026-10-17]이다.</b>
        /// 기획자가 읽고 고칠 자리다 — 순서는 기획자가 말한 네 가지
        /// (이동 → 부수기 → 케이블 → 구멍)를 그대로 따랐다.
        /// </summary>
        static readonly Step[] Script =
        {
            new Step { Goal = Goal.Move, Amount = 8f,
                       Line = "왼쪽 아래를 끌어 로봇을 움직여라",
                       Done = "좋다" },

            new Step { Goal = Goal.EnterDemolition,
                       Line = "건물에 다가가면 '부수기'가 뜬다. 눌러라",
                       Done = "그 건물을 맡았다" },

            new Step { Goal = Goal.Break, Amount = 1f,
                       Line = "화면을 빠르게 두드려 건물을 부숴라",
                       Done = "한 채 무너뜨렸다" },

            new Step { Goal = Goal.Capture, Amount = 1f,
                       Line = "'케이블'을 눌러 잔해를 둘러싸라",
                       Done = "끌고 다닐 수 있다" },

            new Step { Goal = Goal.Deliver, Amount = 1f,
                       Line = "잔해를 끌고 구멍으로 가라",
                       Done = "구멍이 먹었다" },

            new Step { Goal = Goal.HoleLevel, Amount = 2f,
                       Line = "많이 넣을수록 구멍이 커진다. 더 넣어라",
                       Done = "구멍이 자랐다 — 이제 더 큰 것도 삼킨다" },

            new Step { Goal = Goal.Free,
                       Line = "남은 시간 동안 마음껏 부숴라",
                       Done = null },
        };

        // ── 상태 ────────────────────────────────────────────────────────────

        static TutorialFlow s_current;

        int at;                     // 지금 몇 번째 단계인가
        float stepStarted;          // 그 단계가 시작된 시각
        float doneUntil;            // 끝난 말을 이때까지 보여 준다
        bool finished;              // 마지막 단계까지 갔는가

        // 단계 시작 시점의 기준값. "지금부터 한 채"가 되도록 빼고 센다.
        int baseBroken, baseCollected;
        float moved;                // Move 단계에서 지금까지 움직인 거리
        Vector3 lastAt;

        readonly float[] spent = new float[Script.Length];

        GameLoop loop;
        GameModeController mode;

        // ── 들어오는 문 ─────────────────────────────────────────────────────

        /// <summary>
        /// <see cref="GameLoop.StartZone"/>이 부른다. 튜토리얼 구역이 아니면
        /// 아무것도 만들지 않고, 이미 떠 있던 것은 치운다.
        /// </summary>
        public static void OnZoneStart(ZoneData zone)
        {
            bool want = zone != null && zone.id == TutorialZoneId;

            if (!want)
            {
                if (s_current != null) Destroy(s_current.gameObject);
                return;
            }

            if (s_current == null)
            {
                var go = new GameObject("TutorialFlow");
                s_current = go.AddComponent<TutorialFlow>();
            }
            s_current.Restart();
        }

        void Restart()
        {
            at = 0;
            finished = false;
            doneUntil = 0f;
            for (int i = 0; i < spent.Length; i++) spent[i] = 0f;
            EnterStep();
        }

        void OnDestroy() { if (s_current == this) s_current = null; }

        // ── 진행 ────────────────────────────────────────────────────────────

        void EnterStep()
        {
            stepStarted = Time.time;
            moved = 0f;

            if (loop == null) loop = GameLoop.Instance;
            baseBroken = loop != null ? loop.StructuresBroken : 0;
            baseCollected = loop != null ? loop.Collected : 0;
            lastAt = loop != null ? loop.PlayerPosition : Vector3.zero;
        }

        void Update()
        {
            if (finished) return;
            if (loop == null) loop = GameLoop.Instance;
            if (loop == null || !loop.IsRunning) return;
            if (mode == null) mode = GameModeController.Instance;

            Track();

            if (Time.time - stepStarted < Settle) return;
            if (!Reached(Script[at])) return;

            spent[at] = Time.time - stepStarted;
            if (!string.IsNullOrEmpty(Script[at].Done)) doneUntil = Time.time + DoneHold;

            at++;
            if (at >= Script.Length) { finished = true; Report(); return; }
            EnterStep();
        }

        /// <summary>단계와 무관하게 매 프레임 쌓아야 하는 것 — 지금은 이동 거리뿐이다.</summary>
        void Track()
        {
            Vector3 now = loop.PlayerPosition;
            // 부수기 화면에서는 몸이 순간이동하듯 옮겨질 수 있다. 큰 뜀은 안 센다.
            float d = Vector3.Distance(now, lastAt);
            if (d < 2f) moved += d;
            lastAt = now;
        }

        bool Reached(Step s)
        {
            switch (s.Goal)
            {
                case Goal.Move:
                    return moved >= s.Amount;

                case Goal.EnterDemolition:
                    return mode != null && mode.IsDemolition;

                case Goal.Break:
                    return loop.StructuresBroken - baseBroken >= s.Amount;

                case Goal.Capture:
                    return loop.Cargo != null && loop.Cargo.TotalUnits >= s.Amount;

                case Goal.Deliver:
                    return loop.Collected - baseCollected >= s.Amount;

                case Goal.HoleLevel:
                    return loop.ActiveHole != null && loop.ActiveHole.Level >= s.Amount;

                default:
                    return false;           // Free 는 끝나지 않는다
            }
        }

        /// <summary>
        /// <b>이것이 이 파일의 두 번째 목적이다.</b> 단계마다 실제로 걸린 시간을
        /// 표로 찍는다. 기획자가 한 판을 끝까지 돌려 본 적이 없어서, 3분이
        /// 맞는 분량인지 판단할 근거가 아직 없다.
        /// </summary>
        void Report()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[튜토리얼] 가르치는 단계가 끝났다 — 단계별 걸린 시간");

            float total = 0f;
            for (int i = 0; i < Script.Length - 1; i++)      // 마지막 Free 는 뺀다
            {
                total += spent[i];
                sb.AppendLine($"  {i + 1}. {Script[i].Line}  —  {spent[i]:0.0}초");
            }

            sb.AppendLine($"  합계 {total:0}초 ({total / 60f:0.0}분)");
            sb.AppendLine($"  남은 시간 {loop.TimeRemaining:0}초 / 제한 {loop.Zone.timeLimit}초");
            sb.AppendLine($"  부순 건물 {loop.StructuresBroken}채 · 모은 잔해 {loop.Collected}단위" +
                          $" · 구멍 {(loop.ActiveHole != null ? loop.ActiveHole.Level : 0)}단계");
            Debug.Log(sb.ToString());
        }
    }
}
