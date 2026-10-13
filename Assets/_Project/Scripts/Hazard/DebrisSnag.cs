using UnityEngine;

namespace Demo.Hazard
{
    /// <summary>
    /// 끌려오던 잔해가 건물·토대에 걸렸을 때 무슨 일이 일어나는가 [지시 14번, 2026-10-01].
    ///
    /// <b>지금은 아무 일도 안 일어난다.</b> 잔해에는 콜라이더가 0개라 건물을 그냥 통과한다.
    /// "부딪혔을 때의 리스크"를 만들려면 <b>부딪히는 일부터</b> 만들어야 한다.
    /// 부딪힘 판정은 <see cref="ObstacleField"/>가 하고, 이 클래스는 <b>그 다음에 무엇을 할지</b>만 정한다.
    ///
    /// <b>다섯 가지를 전부 넣고 인스펙터에서 바꿀 수 있게 했다</b> — 기획자가 직접 겪어 보고
    /// 고르라는 뜻이다. 어느 쪽이 맞는지는 실측으로 정해지지 않는다 (아래 '재지 않는 것' 참조).
    ///
    /// <b>이 클래스가 재지 않는 것</b>: 억울함. 걸려서 잃는 것이 '정당한 실수의 대가'로 읽히는지
    /// '조작 불능'으로 읽히는지는 사람이 플레이해야 나온다. 여기서 나오는 숫자는
    /// <b>얼마나 자주 걸리고 얼마를 잃는가</b>뿐이다.
    /// </summary>
    [System.Serializable]
    public sealed class DebrisSnag
    {
        public enum Response
        {
            /// <summary>지금 그대로. 잔해가 건물을 통과한다. 리스크 없음</summary>
            Pass = 0,
            /// <summary>건물 밖으로 밀려난다. 뭉치가 일그러지지만 잃는 것은 없다</summary>
            Block = 1,
            /// <summary>걸린 조각이 그물에서 빠져 그 자리에 남는다. 다시 주우러 가야 한다</summary>
            DropOff = 2,
            /// <summary>줄이 끊어져 <b>전부</b> 놓친다. 제일 가혹하다</summary>
            Snap = 3,
            /// <summary>걸려 있는 동안 느려진다. 피해를 시간으로 환산한다 (GAME_DESIGN §6)</summary>
            Slow = 4,
            /// <summary>부딪힐 때마다 <b>보유량의 일정 비율</b>을 흘린다 [지시 2026-10-12]</summary>
            DropShare = 5,
        }

        [SerializeField, Tooltip("걸렸을 때 무엇을 할 것인가.\n" +
                 "Pass = 2026-10-01 이전 동작(통과) · Block = 밀어냄 · DropOff = 그 조각만 놓침 · " +
                 "Snap = 전부 놓침 · Slow = 느려짐")]
        Response mode = Response.Pass;

        [SerializeField, Range(0f, 2f), Tooltip("이만큼 계속 걸려 있어야 DropOff·Snap이 터진다(초).\n" +
                 "0이면 모서리를 스치기만 해도 터져서 조작 불능으로 읽힌다")]
        float graceSeconds = 0.35f;

        [SerializeField, Range(0.1f, 1f), Tooltip("Slow에서 걸려 있는 동안의 속도 배율")]
        float slowFactor = 0.45f;

        [SerializeField, Range(0.2f, 2f), Tooltip("판정에 쓸 잔해 반경 배율.\n" +
                 "1이면 보이는 크기 그대로. 작게 하면 스칠 때는 안 걸린다")]
        float radiusScale = 0.8f;

        [SerializeField, Range(0f, 1f), Tooltip("DropShare에서 한 번 부딪힐 때 흘리는 비율.")]
        float dropShare = 0.10f;

        [SerializeField, Range(0f, 10f), Tooltip("거둔 짐이 토대를 못 벗어날 때 면제를 끊는 상한(초). "
                 + "보통은 시간이 아니라 토대를 벗어나는 순간 끝난다 [지시 2026-10-12]")]
        float pickupExemptSeconds = 4f;

        [SerializeField, Range(0f, 5f), Tooltip("한 번 터진 뒤 다시 터질 때까지의 쉬는 시간(초).\n" +
                 "0이면 접촉이 이어지는 동안 유예가 계속 다시 차서 연달아 터진다 — " +
                 "실측에서 건물 한 채를 스치는 데 6번 터져 20개 중 18개를 잃었다 [고침 2026-10-02]")]
        float cooldownSeconds = 1.5f;

        float stuckFor;
        float cooldownLeft;
        float exemptLeft;
        bool stillOnFoundation;

        /// <summary>이번 프레임에 걸린 조각 수. 실측·연출이 읽는다.</summary>
        public int SnaggedNow { get; private set; }

        /// <summary>이 구역에서 걸린 총 횟수(프레임 단위가 아니라 사건 단위).</summary>
        public int SnagEvents { get; private set; }

        /// <summary>걸려서 잃은 총 조각 수.</summary>
        public int LostPieces { get; private set; }

        /// <summary>지금 몇 초째 걸려 있는가. HUD 경고가 읽는다.</summary>
        public float StuckFor => stuckFor;

        public Response Mode { get => mode; set => mode = value; }

        /// <summary>Slow일 때의 속도 배율. 안 걸려 있으면 1.</summary>
        public float SpeedScale =>
            mode == Response.Slow && SnaggedNow > 0 ? slowFactor : 1f;

        /// <summary>무엇을 잃는 반응이 터질 조건을 채웠는가. 쉬는 중이면 안 터진다.</summary>
        public bool Triggered =>
            (mode == Response.DropOff || mode == Response.Snap || mode == Response.DropShare) &&
            cooldownLeft <= 0f && stuckFor >= graceSeconds;

        /// <summary>DropShare에서 한 번에 흘리는 비율.</summary>
        public float DropShareRatio => dropShare;

        /// <summary>
        /// 방금 거둬들였으니 잠깐 판정을 쉰다 [지시 2026-10-12].
        ///
        /// <b>왜 필요한가.</b> 잔해는 제가 나온 건물의 토대 <b>위에</b> 놓인다. 토대가 장애물이
        /// 되면 그 조각들은 <b>태어나자마자 장애물 안에 있다</b> — 원을 그려 거두는 순간
        /// 전부 걸린 것으로 판정돼 10%씩 흘리기 시작한다. 플레이어가 잘못한 것이 하나도 없는데
        /// 벌칙만 터지는 자리다.
        ///
        /// 그래서 <b>끌어내는 동안만</b> 쉰다. 토대를 벗어난 뒤부터가 진짜 판정이다.
        /// </summary>
        public void ExemptAfterPickup()
        {
            exemptLeft = pickupExemptSeconds;
            stillOnFoundation = true;      // 막 거뒀으니 아직 토대 위다
        }

        /// <summary>지금 면제 중인가. 남은 초. 0이면 아니다.</summary>
        public float ExemptLeft => exemptLeft;

        /// <summary>다시 터지기까지 남은 시간(초). 0이면 지금 터질 수 있다.</summary>
        public float CooldownLeft => cooldownLeft;

        public void Reset()
        {
            stuckFor = 0f;
            cooldownLeft = 0f;
            exemptLeft = 0f;
            SnaggedNow = 0;
        }

        public void ResetCounters()
        {
            Reset();
            SnagEvents = 0;
            LostPieces = 0;
        }

        /// <summary>프레임 시작. 세는 것을 비운다.</summary>
        public void Begin() => SnaggedNow = 0;

        /// <summary>
        /// 조각 하나를 본다. 걸렸으면 <paramref name="position"/>을 어떻게 고칠지 돌려준다.
        /// </summary>
        /// <param name="position">[입출력] 조각의 자리. Block·DropOff·Snap에서 밖으로 밀어낸다</param>
        /// <param name="radius">조각의 판정 반경</param>
        /// <returns>걸렸으면 true</returns>
        public bool Check(ref Vector3 position, float radius)
        {
            if (mode == Response.Pass) return false;
            // <b>첫 고리에 한해서만</b> 면제한다 [지시 2026-10-12].
            // 초로 끊지 않는다 — 티어가 높을수록 토대가 넓어 빠져나오는 데 더 걸리기 때문이다.
            // 짐이 토대를 벗어나는 순간 면제가 끝나고, 거기서부터가 진짜 판정이다.
            if (exemptLeft > 0f)
            {
                if (ObstacleField.OnFoundation(position)) stillOnFoundation = true;
                return false;
            }
            if (!ObstacleField.Overlap(position, radius * radiusScale, out Vector3 push)) return false;

            SnaggedNow++;

            // <b>밀어내는 것은 벌칙이 아니라 알림이다</b> [고침 2026-10-01].
            // 처음에는 Slow일 때 안 밀어냈다 — "밀어내면서 느려지면 두 벌칙"이라고 봤는데,
            // 밀어내는 데 드는 시간은 0이다 (실측: Block의 클리어 타임 증가 +0.0초).
            // 벌칙이 아니라면 빼야 할 이유가 없고, <b>빼면 플레이어가 왜 느려졌는지 못 본다</b> —
            // 11번에서 D안(케이블만 막힘)을 물린 것과 같은 '관측 불가능한 규칙' 문제가 된다.
            // 그래서 Pass가 아닌 모든 반응이 밀어낸다. 잃는 것은 모드마다 다르고, 보이는 것은 같다.
            position += push;
            return true;
        }

        /// <summary>
        /// 프레임 끝. 걸려 있던 시간을 누적하고 사건을 센다.
        /// </summary>
        /// <param name="dt">프레임 시간</param>
        public void End(float dt)
        {
            if (cooldownLeft > 0f) cooldownLeft -= dt;
            if (exemptLeft > 0f)
            {
                // 토대를 다 벗어났으면 시간이 남았어도 끝낸다. 못 벗어나면 상한이 끊는다.
                if (!stillOnFoundation) exemptLeft = 0f;
                else exemptLeft -= dt;
                stillOnFoundation = false;
            }

            if (SnaggedNow > 0)
            {
                if (stuckFor <= 0f) SnagEvents++;
                stuckFor += dt;
            }
            else stuckFor = 0f;
        }

        /// <summary>터진 뒤 호출한다. 잃은 수를 기록하고 시계를 되돌린 뒤 쉬는 시간을 건다.</summary>
        public void Consume(int lost)
        {
            LostPieces += lost;
            stuckFor = 0f;
            cooldownLeft = cooldownSeconds;
        }
    }
}
