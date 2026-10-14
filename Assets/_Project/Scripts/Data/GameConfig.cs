using System.Text;
using UnityEngine;

namespace Demo.Data
{
    /// <summary>
    /// 전역 튜닝 값. 프로젝트에 단 하나만 존재한다.
    ///
    /// CLAUDE.md 규칙 7 — 수치는 코드에 하드코딩하지 않는다. 전부 여기로 뺀다.
    /// D16–18 시뮬레이션 밸런싱이 이 에셋 하나만 고쳐서 돌아가는 것이 전제다.
    ///
    /// 값은 기획자가 채운다. 초안(AI 창작·미승인)은 docs/GAME_DESIGN.md §12에 있다 —
    /// 쓰든 버리든 기획자가 판단한다. 여기에 미리 옮겨두지 않았다.
    ///
    /// 구멍 성장 필드가 없는 것은 누락이 아니다. 공식이냐 단계표냐가 D9로 보류됐고
    /// (HANDOFF §1), 지금 필드를 만들면 그 결정을 코드가 먼저 내려버린다.
    /// </summary>
    [CreateAssetMenu(menuName = "Demo/Game Config", fileName = "GameConfig")]
    public sealed class GameConfig : ScriptableObject
    {
        [Header("이동")]
        [Tooltip("견인량 0일 때 이동 속도(m/s). 달리기를 켠 상태의 속도다 " +
                 "[달리기 토글 도입 2026-09-28]")]
        public float moveSpeedBase;

        [Tooltip("달리기를 끈 채 걸을 때의 속도(m/s). [제안·미승인]\n" +
                 "걷기 클립이 1배속에서 내는 속도가 0.99m/s다(3m 로봇 기준 실측). " +
                 "이 값이 그보다 너무 크면 발이 미끄러지고, 너무 작으면 제자리걸음이 된다")]
        public float walkSpeedBase;

        [Tooltip("견인 1단위당 감속(m/s)")]
        public float speedPenaltyPerUnit;

        [Tooltip("아무리 많이 끌어도 이 아래로는 안 내려간다(m/s)")]
        public float moveSpeedMin;

        [Header("파괴")]
        [Tooltip("브레이커 사거리(m). 이 안에 있는 대상만 부술 수 있다")]
        public float breakerRange;

        [Tooltip("진행도 몇에서 메시가 온전→파손으로 바뀌는가 (0~1 사이)")]
        public float structureDamagedAt01;

        [Tooltip("파괴 화면에서 초당 몇 번 두드린다고 보는가. " +
                 "탭 하나가 올리는 양 = 1 / (breakTime × 이 값). " +
                 "'몇 초 걸리는가'는 대상별(breakTime)이지만 '초당 몇 번 두드리는가'는 " +
                 "손의 특성이라 전역이다")]
        public float tapsPerSecond;

        [Header("케이블")]
        [Tooltip("궤적 총 길이 상한(m). 넘으면 오래된 포인트부터 버린다")]
        public float cableMaxLength;

        [Tooltip("궤적 포인트를 찍는 간격(m). 작을수록 고리 판정이 정밀하고 비싸다")]
        public float cablePointInterval;

        [Tooltip("고리로 인정할 최소 면적(m²). 제자리에서 비비는 것을 걸러낸다")]
        public float cableMinLoopArea;

        [Header("구멍")]
        [Tooltip("흡인 판정 여유(m). 반경 + 이 값 안에 들어오면 빨려든다 — 정확히 올릴 필요 없다")]
        public float holeSuctionMargin;

        [Tooltip("흡인 범위 안의 잔해가 구멍으로 끌려오는 속도(m/s). " +
                 "가까울수록 세게 당긴다 — 균일하게 당기면 멀리서부터 질질 끌려와 " +
                 "'빨려드는' 느낌이 안 난다")]
        public float holeSuctionSpeed;

        [Tooltip("잔해 1단위가 주는 경험치. 경험치는 화면에 안 보인다 (가시화는 추후 판단)")]
        public int xpPerDebrisUnit = 1;

        [Tooltip("구멍 성장 단계표. [0]이 시작 상태다.\n" +
                 "경험치가 xpToNext에 도달하면 다음 단계로 올라가며 반경이 커진다.\n" +
                 "마지막 단계의 xpToNext는 0 — 더 이상 성장하지 않는다는 뜻")]
        public HoleLevel[] holeLevels;

        [Header("구멍 이동 — 최종 단계에서만")]
        [Tooltip("최종 단계에 도달한 구멍이 떠도는 속도(m/s). 0이면 움직이지 않는다.\n" +
                 "플레이어가 조작하지 않는다 — 스스로 느리게 돌아다니며 닿는 것을 삼킨다")]
        public float holeDriftSpeed;

        [Tooltip("최초 위치에서 이만큼 안에서만 떠돈다(m). 구역 밖으로 나가지 않게 한다")]
        public float holeDriftRadius;

        // ── 점수 · 등급 ───────────────────────────────────────────────────────
        // 전부 [제안·미승인 2026-10-13]. Tools/balance/score_rules.csv · grades.csv가
        // 이 값들의 기록처다. 0으로 두면 그 항목은 점수에 안 들어간다.

        [Header("점수")]
        [Tooltip("구멍에 넣은 잔해 1단위당. 할당량까지는 이 값 그대로")]
        public float pointsPerDebrisUnit;

        [Tooltip("할당량을 넘긴 잔해에 곱하는 배율. 1이면 넘겨도 같은 점수")]
        public float overQuotaMultiplier;

        [Tooltip("할당량을 채웠을 때 한 번 주는 보너스")]
        public float quotaBonus;

        [Tooltip("제한 시간이 남은 채로 끝냈을 때 1초당")]
        public float pointsPerSecondLeft;

        [Tooltip("완전히 부순 건물 한 채당. 안 날라도 부순 것은 친다")]
        public float pointsPerStructure;

        [Tooltip("끝났을 때 구멍 단계 1당")]
        public float holeLevelBonus;

        [Header("등급")]
        [Tooltip("점수 → 등급. 높은 등급을 위에 둔다. 맨 아래 minScore는 0이어야 한다 —\n" +
                 "어떤 점수든 등급이 나와야 결과 화면이 빈칸이 안 된다")]
        public Grade[] grades;

        /// <summary>
        /// 점수 한 구간. <b>별 개수로 쓴다</b> — 결과 화면이 <c>stars</c>만큼 별을 채운다
        /// [지시 2026-10-13]. <c>label</c>은 별 대신 글자로 보이고 싶을 때 쓴다.
        /// </summary>
        [System.Serializable]
        public sealed class Grade
        {
            [Tooltip("이 점수 이상이면 이 등급이다")]
            public int minScore;

            [Tooltip("채울 별 개수")]
            public int stars;

            [Tooltip("글자로도 부를 이름 (S/A/B … 또는 우수/양호). 비워도 된다")]
            public string label;
        }

        [Header("업그레이드")]
        [Tooltip("구멍 단계가 오를 때 고르는 카드들 [지시 2026-10-13].\n" +
                 "같은 단계에 풀리는 카드가 여럿이면 쌓였다가 3장씩 무작위로 뜬다.\n" +
                 "고르지 않은 카드는 다음 단계에도 다시 나온다")]
        public Upgrade[] upgrades;

        [Tooltip("한 번에 몇 장을 보여 줄 것인가 [지시 2026-10-13: 3장]")]
        public int upgradeChoices = 3;

        /// <summary>
        /// 업그레이드 카드 한 장.
        ///
        /// <b><c>id</c>가 코드와 데이터를 잇는 열쇠다.</b> 게임 쪽은 <c>Upgrades.Mul("moveSpeed")</c>
        /// 처럼 이름으로 묻고, 무엇이 얼마나 오르는지는 전부 이 표에 있다.
        /// <c>value</c>의 뜻은 id마다 다르다 — 배율인 것도 있고 거리(m)인 것도 있다.
        /// </summary>
        [System.Serializable]
        public sealed class Upgrade
        {
            [Tooltip("코드가 찾는 열쇠. 바꾸면 그 카드가 아무 일도 안 한다")]
            public string id;

            [Tooltip("카드에 크게 뜨는 이름")]
            public string title;

            [Tooltip("카드에 작게 뜨는 한 줄 설명. 화면에서 뭐가 달라지는지 적는다")]
            public string detail;

            [Tooltip("구멍이 이 단계가 될 때 후보에 들어온다 (2~6). 화면에 보이는 단계 숫자다")]
            public int unlockStage;

            [Tooltip("배율 또는 양. 뜻은 id마다 다르다")]
            public float value = 1f;

            [Tooltip("켜면 이 단계에서는 <b>같은 표시가 붙은 카드끼리만</b> 내민다 " +
                     "[지시 2026-10-14].\n" +
                     "마지막 단계용이다 — 앞에서 안 고른 카드가 섞여 올라오면 " +
                     "'다 키운 것에 대한 보상'이라는 뜻이 흐려진다")]
            public bool exclusive;
        }

        /// <summary>
        /// 구멍 성장 한 단계.
        ///
        /// [확정 2026-09-22] 공식이 아니라 단계표로 간다. 표가 곧 기획이라 값 하나를 바꿀 때
        /// 의도가 그대로 드러나고, 공식의 자기가속 논쟁이 생기지 않는다.
        /// </summary>
        [System.Serializable]
        public sealed class HoleLevel
        {
            [Tooltip("이 단계의 구멍 반경(m). 이보다 큰 잔해는 삼키지 못한다")]
            public float radius;

            [Tooltip("다음 단계까지 필요한 누적 경험치. 마지막 단계는 0")]
            public int xpToNext;

            [Tooltip("이 단계에서 쓸 수 있는 케이블 궤적 길이(m) [지시 2026-10-12]." + "\n" +
                     "구멍과 같은 사다리다 — 단계가 오르면 삼킬 수 있는 크기도, 감쌀 수 있는 " +
                     "덩어리도 같이 커진다. 고리를 닫으려면 둘레만큼 그려야 하므로 " +
                     "그 단계에서 새로 열리는 종류의 흩어짐 반경에서 끌어낸다." + "\n" +
                     "0이면 cableMaxLength로 물러선다")]
            public float cableLength;
        }

        [Header("연출")]
        [Tooltip("파괴 순간 히트스톱(초)")]
        public float hitStopSeconds;

        [Header("카메라")]
        [Tooltip("고정 쿼터뷰 거리(m). 근접 샷은 생성 메시 결함이 보여서 쓰지 않는다")]
        public float cameraDistance;

        [Tooltip("견인 1단위당 줌아웃(m)")]
        public float cameraZoomPerUnit;

        [Tooltip("줌아웃 상한(m)")]
        public float cameraZoomMax;

        [Tooltip("짐 뒤끝의 몇 할만큼 시선을 뒤로 밀 것인가 (0~1) [2026-10-08].\n" +
                 "카메라가 플레이어가 아니라 플레이어와 짐 사이를 보게 한다.\n" +
                 "줌아웃과 달리 화면 속 크기가 안 줄어든다 — 대신 플레이어가 화면 위로 밀린다.\n" +
                 "실측(최악 진행 각도): 시선을 1m 주면 필요한 카메라 거리가 2.07m 준다.\n" +
                 "0이면 2026-10-08 이전과 같다")]
        public float cameraLookBackShare;

        [Tooltip("시선을 뒤로 미는 거리의 상한(m). 너무 크면 플레이어가 화면 밖으로 나간다.\n" +
                 "실측: 거리 8m에서는 4m부터 밀려나고, 짐 때문에 19m까지 물러나면 8m도 버틴다")]
        public float cameraLookBackMax;

        [Tooltip("짐 뒤끝 1m를 담는 데 필요한 카메라 거리(m) [2026-10-08].\n" +
                 "쿼터뷰 각도·화각·종횡비로 정해지는 기하 상수다 — 취향이 아니다.\n" +
                 "지금 카메라(pitch 50 · yaw 45 · fov 60 · 16:9 · 여백 8%)에서 실측 2.07.\n" +
                 "다섯 종류 전부 2.07~2.08로 거의 정확히 비례했다. 각도나 화각을 바꾸면 다시 잴 것")]
        public float cameraFitPerMeter;

        [Header("성능 예산 — CLAUDE.md 표에서 온 값. 기획 수치가 아니다")]
        [Tooltip("프레임을 이 값으로 못 박는다. 0이면 건드리지 않는다(플랫폼 기본). "
                 + "<b>모바일에서 Unity의 기본값은 30이다</b> — 그대로 두면 "
                 + "'60 이하로 떨어지지 않는 선'을 확인할 수가 없다. 애초에 60이 안 나온다 "
                 + "[지시 2026-09-30]. "
                 + "vSync가 켜져 있으면 이 값은 무시되므로 GameConfigRef가 같이 끈다. "
                 + "주의: 못 박으면 여유가 있는지 없는지가 화면에서 안 보인다 — "
                 + "60이 나와도 '딱 60'인지 '200을 낼 수 있는데 눌린 것'인지 구분되지 않는다")]
        public int targetFrameRate = 60;

        [Tooltip("동시에 맵에 떠 있는 잔해의 상한. 0이면 상한 없음 [지시 2026-10-13]. "
                 + "상한을 두면 잘린 만큼 부쉈는데 잔해가 안 나온다가 된다 — 이 게임에서 제일 "
                 + "보여야 할 것이 지워진다. 드로우콜은 조각 하나당 하나씩 붙는다")]
        public int debrisMaxActive = 0;

        [Tooltip("동시 구조물 상한. ZoneBuilder가 넘기면 경고한다")]
        public int structureMaxActive = 60;

        // ── 11번: 다 부순 자리에 남는 토대를 어떻게 할 것인가 [구현 2026-10-02] ──────
        //
        // 다섯 안을 <b>전부 구현해 두고</b> 여기서 고른다. 기획자가 직접 겪어 보고 정하라는 뜻이다.
        // 기본값 None은 2026-10-01 이전 동작이라, 고르기 전까지 게임은 안 바뀐다.
        //
        // 토대는 원본 발자국의 (x 0.90, z 1.00)에 높이 0.40이다 — 5종 전부 같다 [실측].
        // 0.40은 CharacterController.stepOffset 0.30보다 높아서, 콜라이더만 켜면 바로 막힌다
        // (실측: x 15.75에서 완전 정지. 0.45로 올리면 올라타 넘어간다).

        public enum FoundationRule
        {
            /// <summary>장식. 아무것도 안 막는다. 2026-10-01 이전 동작</summary>
            None = 0,
            /// <summary>콜라이더를 켠다. 걸어서 못 지나간다</summary>
            Obstacle = 1,
            /// <summary>콜라이더를 켜고 stepOffset을 올려 올라설 수 있게 한다</summary>
            Platform = 2,
            /// <summary>걸어서는 지나가지만 케이블이 못 넘는다</summary>
            CableOnly = 3,
            /// <summary>막지만 일정 시간 뒤 사라진다</summary>
            Decay = 4,
        }

        [Tooltip("다 부순 자리에 남는 토대를 어떻게 할 것인가 [11번].\n" +
                 "None = 장식(지금) · Obstacle = 못 지나감 · Platform = 올라설 수 있음 · " +
                 "CableOnly = 케이블만 막힘 · Decay = 막지만 사라짐")]
        public FoundationRule foundationRule = FoundationRule.None;

        [Tooltip("Decay에서 토대가 사라지기까지(초). 다른 규칙에서는 안 쓴다")]
        public float foundationDecaySeconds = 20f;

        [Tooltip("Platform에서 쓸 CharacterController.stepOffset.\n" +
                 "토대 높이 0.40보다 커야 올라탄다. 실측: 0.30은 막히고 0.45는 넘어간다")]
        public float platformStepOffset = 0.45f;

        [Tooltip("Platform에서 <끌려오는 잔해>도 토대에 걸리는가 [11번 · 해석이 갈리는 자리].\n\n" +
                 "'발판'을 어떻게 읽느냐에 따라 둘 다 말이 된다:\n" +
                 "  켬 — 플레이어는 올라서고 짐은 걸린다. 턱에 짐이 걸리는 쪽\n" +
                 "  끔 — 플레이어도 짐도 넘어간다. 케이블이 짐을 살짝 들어 넘긴다는 쪽\n\n" +
                 "실측 차이가 크다: 켜면 클리어 +6.6%/걸림 69%, 끄면 +0.0%/걸림 37%.\n" +
                 "<b>어느 쪽인지는 기획 결정이다</b> — AI가 켬으로 두고 '발판은 공짜가 아니다'라고 " +
                 "보고한 적이 있는데, 그건 측정 결과가 아니라 해석이었다 [2026-10-02]")]
        public bool platformBlocksCargo = true;

        /// <summary>견인량에 따른 이동 속도. 곡선의 모양은 PlayerController가 정한다.</summary>
        public float SpeedFloor => moveSpeedMin;

        /// <summary>
        /// 미입력 필드를 찾는다. 전부 채워져 있으면 null.
        /// 성능 예산 두 개는 CLAUDE.md에서 온 값이라 검사하지 않는다.
        /// </summary>
        public string Validate()
        {
            var sb = new StringBuilder();

            if (moveSpeedBase <= 0f) sb.Append("moveSpeedBase, ");
            if (walkSpeedBase <= 0f) sb.Append("walkSpeedBase, ");
            if (walkSpeedBase > moveSpeedBase) sb.Append("walkSpeedBase(달리기보다 빠름), ");
            if (speedPenaltyPerUnit <= 0f) sb.Append("speedPenaltyPerUnit, ");
            if (moveSpeedMin <= 0f) sb.Append("moveSpeedMin, ");
            if (moveSpeedMin > moveSpeedBase) sb.Append("moveSpeedMin(기본속도보다 큼), ");
            if (breakerRange <= 0f) sb.Append("breakerRange, ");
            if (structureDamagedAt01 <= 0f || structureDamagedAt01 >= 1f)
                sb.Append("structureDamagedAt01(0~1 사이여야 함), ");
            if (tapsPerSecond <= 0f) sb.Append("tapsPerSecond, ");
            if (cableMaxLength <= 0f) sb.Append("cableMaxLength, ");
            if (cablePointInterval <= 0f) sb.Append("cablePointInterval, ");
            if (cableMinLoopArea <= 0f) sb.Append("cableMinLoopArea, ");
            if (holeSuctionMargin <= 0f) sb.Append("holeSuctionMargin, ");
            if (holeSuctionSpeed <= 0f) sb.Append("holeSuctionSpeed, ");
            if (cameraDistance <= 0f) sb.Append("cameraDistance, ");
            if (xpPerDebrisUnit <= 0) sb.Append("xpPerDebrisUnit, ");

            if (holeLevels == null || holeLevels.Length == 0)
            {
                sb.Append("holeLevels 비어 있음, ");
            }
            else
            {
                for (int i = 0; i < holeLevels.Length; i++)
                {
                    if (holeLevels[i].radius <= 0f)
                        sb.Append($"holeLevels[{i}].radius, ");
                    // 반경이 줄어드는 단계는 항상 실수다 — 구멍은 커지기만 한다
                    if (i > 0 && holeLevels[i].radius <= holeLevels[i - 1].radius)
                        sb.Append($"holeLevels[{i}].radius(앞 단계보다 크지 않음), ");
                    if (i < holeLevels.Length - 1 && holeLevels[i].xpToNext <= 0)
                        sb.Append($"holeLevels[{i}].xpToNext, ");
                }
            }

            if (sb.Length == 0) return null;
            sb.Length -= 2;
            return $"GameConfig '{name}' 미입력: {sb}\n" +
                   "  → 초안 수치는 docs/GAME_DESIGN.md §12에 있다 (AI 창작·미승인). " +
                   "쓸지 말지는 기획자가 정한다.";
        }
    }
}
