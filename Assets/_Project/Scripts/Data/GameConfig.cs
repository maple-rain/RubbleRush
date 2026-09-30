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

        [Header("성능 예산 — CLAUDE.md 표에서 온 값. 기획 수치가 아니다")]
        [Tooltip("동시 잔해 상한. 초과분은 오래된 것부터 페이드아웃. "
                 + "<b>80은 실측 없이 CLAUDE.md 표에서 옮겨 온 자리표시자였다.</b> "
                 + "그 표도 'D2에 실측하기 전까지의 작업 기준값'이라고 적고 있고 D2는 안 했다. "
                 + "구역 전체(30채)를 다 부수면 잔해가 344개 나온다 — 80이면 264개(77%)가 "
                 + "잘려서, 다 부숴도 플레이어는 자기가 한 일의 1/4만 본다 [지적 2026-09-30]. "
                 + "실측: 잔해 한 개는 삼각형 12개짜리 큐브라 344개를 다 띄워도 삼각형 "
                 + "예산의 2.8%다. 드는 것은 드로우콜 하나씩뿐이고, 데스크톱에서는 400개에서도 "
                 + "160fps가 나왔다. 500은 344에 여유를 둔 값이다 [지시 2026-09-30]. "
                 + "저사양 폰에서 드로우콜을 감당하는지는 DebrisBenchmark(F9)로 확인할 것")]
        public int debrisMaxActive = 500;

        [Tooltip("동시 구조물 상한. ZoneBuilder가 넘기면 경고한다")]
        public int structureMaxActive = 60;

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
