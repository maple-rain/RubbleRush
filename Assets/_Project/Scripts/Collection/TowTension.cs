using UnityEngine;

namespace Demo.Collection
{
    /// <summary>
    /// 짐이 매달린 점을 <b>줄 끝의 추</b>로 다룬다 — 장력 [지시 13번, 2026-10-01].
    ///
    /// <b>무엇이 문제였나.</b> 지금은 짐의 기준점이 <c>플레이어 위치 − 앞방향 × 거리</c>다.
    /// 앞방향은 차체 회전(720°/s)을 그대로 따라가므로, 방향을 꺾으면 <b>짐이 0.125초 만에
    /// 플레이어 주위를 통째로 돈다.</b> 무게가 없는 것처럼 보이는 원인이 이것이고,
    /// 잔해에 Rigidbody를 달지 않아서가 아니다 — 실측에서 이 각속도가 그대로 나온다.
    ///
    /// <b>고치는 법.</b> 기준점에 관성을 준다. 점 하나짜리 추이고, 줄 길이를 넘으면
    /// 늘어나지 않는 줄처럼 되돌린다(반지름 방향 속도를 깎는다). 꺾으면 짐이 바깥으로
    /// 쏠렸다가 끌려 들어온다. <b>점 하나</b>라 잔해가 500개여도 비용이 같다.
    ///
    /// <see cref="CableRope"/>가 모양을 그리고, 이쪽은 그 모양이 어디에 있을지를 정한다.
    /// </summary>
    [System.Serializable]
    public sealed class TowTension
    {
        [SerializeField, Tooltip("빈 줄의 길이(m). 실제 길이는 여기에 짐 반지름이 더해진다")]
        float leashLength = 2.5f;

        [SerializeField, Tooltip("공기·바닥 저항. 클수록 빨리 멎는다. " +
                 "0이면 영원히 흔들리고, 너무 크면 예전처럼 딱 붙어 따라온다")]
        float drag = 3.2f;

        [SerializeField, Tooltip("짐을 플레이어 뒤로 되돌리려는 힘(m/s²). " +
                 "0이면 옆에 매달린 채로 끌려온다")]
        float restoring = 9f;

        [SerializeField, Range(0f, 1f), Tooltip("줄이 팽팽해졌을 때 반지름 방향 속도를 얼마나 죽일 것인가. " +
                 "1이면 늘어나지 않는 줄, 0이면 고무줄")]
        float inextensible = 0.9f;

        [SerializeField, Tooltip("끌고 있는 단위당 관성 증가. 많이 끌수록 늦게 반응한다.\n" +
                 "0이면 양에 상관없이 똑같이 움직인다")]
        float inertiaPerUnit = 0.012f;

        Vector3 pos, vel;
        bool ready;

        /// <summary>짐 뭉치의 중심. CargoTow가 슬롯을 여기 기준으로 깐다.</summary>
        public Vector3 Position => pos;

        /// <summary>플레이어에서 짐으로 가는 단위 벡터. 그물의 방향이 된다.</summary>
        public Vector3 Direction { get; private set; } = Vector3.back;

        /// <summary>줄이 플레이어 뒤쪽에서 몇 도 벌어져 있는가. 실측·연출 트리거가 읽는다.</summary>
        public float SwingDegrees { get; private set; }

        /// <summary>
        /// 지금 줄 길이(m). <see cref="CargoTow"/>가 매 프레임 짐 크기에 맞춰 넣는다.
        ///
        /// <b>왜 고정이 아닌가</b> [고침 2026-10-01]. 잔해 20개의 원판 반지름은 4.02m인데
        /// 줄이 2.5m면 <b>원판이 플레이어보다 1.5m 앞까지 덮는다</b> — 플레이어가 자기 짐 안에
        /// 들어가 서 있게 된다. 예전부터 그랬지만 줄을 안 그려서 안 보였고, 그물을 그리자 드러났다.
        /// 짐이 커지면 줄도 길어져야 뒤에서 끌리는 것으로 보인다.
        /// </summary>
        public float Length { get; set; }

        /// <summary>인스펙터에 박힌 기본 길이. 짐이 없을 때의 길이다.</summary>
        public float BaseLength => leashLength;
        public bool Ready => ready;

        /// <summary>포획 직후 한 번. 짐을 플레이어 뒤에 얌전히 놓고 시작한다.</summary>
        public void Reset(Vector3 player, Vector3 back)
        {
            if (Length <= 0f) Length = leashLength;
            pos = player + back.normalized * Length;
            pos.y = player.y;
            vel = Vector3.zero;
            Direction = back.normalized;
            SwingDegrees = 0f;
            ready = true;
        }

        public void Clear() => ready = false;

        /// <summary>
        /// 한 프레임 전진한다.
        /// </summary>
        /// <param name="dt">프레임 시간</param>
        /// <param name="player">플레이어 위치</param>
        /// <param name="back">플레이어의 뒤쪽(= −forward). 되돌리려는 방향</param>
        /// <param name="loadUnits">끌고 있는 총 단위. 관성에 쓴다</param>
        public void Tick(float dt, Vector3 player, Vector3 back, int loadUnits)
        {
            if (!ready || dt <= 0f) return;

            // 많이 끌수록 둔하게 — 가속과 저항을 같은 비율로 나눈다.
            // 저항만 키우면 '무겁다'가 아니라 '끈적하다'가 된다.
            float mass = 1f + inertiaPerUnit * Mathf.Max(0, loadUnits);

            float leash = Mathf.Max(0.1f, Length > 0f ? Length : leashLength);
            Vector3 want = player + back.normalized * leash;
            want.y = player.y;

            vel += (want - pos) * (restoring / mass * dt);
            vel -= vel * Mathf.Clamp01(drag / mass * dt);
            pos += vel * dt;
            pos.y = player.y;

            // 늘어나지 않는 줄: 길이를 넘으면 되돌리고 바깥으로 가던 속도를 깎는다
            Vector3 d = pos - player; d.y = 0f;
            float len = d.magnitude;
            if (len > leash && len > 1e-4f)
            {
                Vector3 n = d / len;
                pos = player + n * leash;
                float radial = Vector3.Dot(vel, n);
                if (radial > 0f) vel -= n * (radial * inextensible);
                len = leash;
            }

            Direction = len > 1e-4f ? d / len : back.normalized;
            SwingDegrees = Vector3.Angle(back.normalized, Direction);
        }
    }
}
