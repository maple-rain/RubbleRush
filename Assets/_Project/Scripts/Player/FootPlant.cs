using UnityEngine;

namespace Demo.Player
{
    /// <summary>
    /// 발이 <b>땅에 닿는 순간</b>을 집어낸다 [지시 2026-10-14].
    ///
    /// <b>원래 무엇이 틀렸나.</b> 발소리를 걸은 거리(2.2m마다)로 냈다. 다리는 애니메이션으로
    /// 움직이므로 둘은 서로를 모르고, 소리와 발이 계속 어긋났다.
    ///
    /// <b>그 다음 두 번도 틀렸다.</b> 기록으로 남긴다 —
    ///
    /// ① <b>클립별 위상표</b>로 "Walk은 0.292/0.792에 닿는다"를 들고 보간했다. 끝점은 맞았지만
    ///    블렌드 중간에서 깨졌다(blend 0.6에서 오른발이 0.29가 아니라 0.66~0.77에 바닥).
    ///    두 클립을 섞으면 <b>자세가 섞이는 것이지 착지 시점이 섞이는 것이 아니다.</b>
    ///
    /// ② <b>절대 문턱</b>으로 "바닥에서 1cm 안이면 착지"로 바꿨다. 그런데 바닥이 보행마다 다르다 —
    ///    실측하면 걷기 발끝 바닥 12.7cm, 달리기 9.5cm로 <b>32mm 차이, 문턱의 3.2배</b>다.
    ///    바닥 기준을 '최근 최소값'으로 들고 있었으므로, 달리다 걷기로 줄이면 달리기 바닥이
    ///    최대 3.2초간 붙잡혀 <b>착지가 한 번도 성립하지 않았다</b>(약 아홉 걸음 무음).
    ///    기획자가 말한 "들리다 안 들리다의 반복"이 이것이다.
    ///
    /// <b>지금 방식: 바닥을 치고 올라오는 순간을 잡는다.</b>
    /// 발 높이가 내려가다 <b>돌아서 올라가기 시작하는</b> 프레임이 착지다. 이것은
    /// <b>상수 오프셋에 면역</b>이라 보행이 바뀌든, 자세 보정이 몸을 기울이든, 땅이 기울든
    /// 돌아서는 순간 자체는 변하지 않는다.
    ///
    /// <b>얕은 골은 바닥 기준으로 거른다.</b> 걷기에서 발가락은 한 바퀴에 세 번 내려온다 —
    /// 진짜 착지(12.6cm)와 발끝을 굴리는 골(15.4cm)과 공중의 골(16.5cm)이다.
    /// 바닥 기준(<see cref="Foot.Floor"/>)은 <b>내려갈 때는 즉시 따라가고 올라올 때는
    /// 천천히</b> 돌아온다. 그래서 진짜 착지만 기준에 닿고, 얕은 골은 걸러지며,
    /// 보행이 바뀌어도 한두 걸음 안에 기준이 새 바닥을 찾는다.
    ///
    /// MonoBehaviour가 아닌 이유는 <see cref="LocomotionRate"/>와 같다.
    /// </summary>
    [System.Serializable]
    public sealed class FootPlant
    {
        [SerializeField, Tooltip("발가락 뼈 이름에 이 글자가 들어간 것을 찾는다.\n" +
                 "로봇은 LeftToeBase / RightToeBase다")]
        string boneMark = "ToeBase";

        [SerializeField, Range(0.002f, 0.05f),
         Tooltip("바닥 기준에서 이만큼(m) 안쪽의 골만 착지로 본다.\n" +
                 "실측: 걷기에서 진짜 착지와 발끝 굴림의 얕은 골이 28mm 떨어져 있다.\n" +
                 "그 사이를 가르는 값이면 된다")]
        float plantMargin = 0.012f;

        [SerializeField, Range(0.005f, 0.4f),
         Tooltip("바닥 기준이 1초에 몇 m나 올라오는가.\n\n" +
                 "<b>너무 빠르면</b> 한 걸음 안에서 기준이 발을 따라 올라가 얕은 골까지 착지로 센다.\n" +
                 "<b>너무 느리면</b> 달리다 걷기로 줄였을 때 달리기 바닥(32mm 아래)에 붙잡혀 " +
                 "발소리가 몇 걸음씩 통째로 빠진다 — 실측된 '들리다 안 들리다'의 원인이 그것이다")]
        float floorRise = 0.05f;

        [SerializeField, Range(0.0002f, 0.01f),
         Tooltip("이만큼(m) 다시 올라와야 '돌아섰다'고 본다. 미세한 떨림으로 울리지 않게 하는 값")]
        float turnEpsilon = 0.0015f;

        [SerializeField, Range(0f, 0.5f),
         Tooltip("이보다 느리면 발소리를 내지 않는다. 제자리 대기에서 울리지 않게 하는 문턱")]
        float minBlend = 0.08f;

        Transform[] toes;
        Foot[] feet;

        /// <summary>실측·디버그용. 지금까지 집어낸 착지 횟수.</summary>
        public int Plants { get; private set; }

        /// <summary>
        /// 발 하나가 기억하는 것. <b>창(window)도 구간도 없다</b> — 지난 프레임 높이와
        /// 지금 하강의 최저, 그리고 바닥 기준뿐이다.
        /// </summary>
        struct Foot
        {
            public float Floor;     // 바닥 기준. 내려갈 때 즉시, 올라올 때 천천히
            public float PrevY;     // 지난 프레임 높이
            public float DipY;      // 지금 하강에서 본 최저
            public bool Falling;    // 내려가는 중인가
            public bool Fresh;      // 아직 첫 프레임인가
            public bool Primed;     // 걷기 시작 후 한 번이라도 착지했는가
        }

        /// <summary>
        /// 발가락 뼈를 찾아 둔다. <b>Awake에서 한 번만</b> 부른다 (CLAUDE.md 규칙 5).
        /// </summary>
        public void Bind(Transform root)
        {
            var found = new System.Collections.Generic.List<Transform>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>())
                if (t.name.IndexOf(boneMark, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    found.Add(t);

            toes = found.ToArray();
            feet = new Foot[toes.Length];
            Forget();
        }

        /// <summary>기억을 비운다. 멈췄을 때와 처음 물릴 때 부른다.</summary>
        void Forget()
        {
            for (int i = 0; i < feet.Length; i++)
                feet[i] = new Foot { Fresh = true };
        }

        /// <summary>
        /// 한 프레임 보고, <b>이번 프레임에 땅에 닿은 발의 수</b>를 돌려준다.
        /// 보통 0이고 가끔 1이다.
        /// </summary>
        /// <param name="root">높이를 재는 기준. 플레이어의 몸이다</param>
        /// <param name="blend">0=정지, 1=최고속. <see cref="LocomotionRate.Blend"/></param>
        public int Tick(Transform root, float blend, float dt)
        {
            if (toes == null || toes.Length == 0 || root == null) return 0;

            // 멈춰 있을 때는 재지 않는다. 대기 동작의 미세한 발 흔들림으로 울리면 안 된다.
            if (blend < minBlend)
            {
                Forget();
                return 0;
            }

            int hits = 0;

            for (int i = 0; i < toes.Length; i++)
            {
                if (toes[i] == null) continue;

                // 몸 기준 높이. 월드 y에서 몸의 움직임(걷기·낙하·지형)을 뺀 값이다.
                //
                // <b>이것만으로는 자세 보정이 안 걷힌다</b> [실측 2026-10-14]. PostureFix는
                // 몸이 아니라 <b>몸 아래의 Hips</b>를 돌리므로 이 좌표에 그대로 들어온다
                // (3도 기울면 발 하나가 약 16mm 오르내린다). 아래의 '돌아서는 순간' 판정이
                // 상수 오프셋에 면역이라 그 흔들림을 견딘다 — 절대 문턱이면 못 견딘다.
                float y = root.InverseTransformPoint(toes[i].position).y;

                Foot f = feet[i];
                if (f.Fresh)
                {
                    f.Floor = f.PrevY = f.DipY = y;
                    f.Fresh = false;
                    feet[i] = f;
                    continue;
                }

                // 바닥 기준: 천천히 올라오다가, 발이 그 아래로 내려가면 즉시 따라간다.
                f.Floor += floorRise * dt;
                if (y < f.Floor) f.Floor = y;

                if (y < f.PrevY)
                {
                    if (!f.Falling) { f.Falling = true; f.DipY = f.PrevY; }
                    if (y < f.DipY) f.DipY = y;
                }
                else if (f.Falling && y >= f.DipY + turnEpsilon)
                {
                    // 바닥을 치고 올라오기 시작했다. 이 골이 바닥 기준에 닿았으면 착지다.
                    //
                    // <b>걷기 시작 뒤 첫 골은 조건 없이 받는다</b> [고침 2026-10-15].
                    // 서 있을 때 발 높이(실측 5.7cm)와 걷기 바닥(13.3cm)이 7.6cm 떨어져 있어,
                    // 기준이 선 자세에 묶여 있으면 <see cref="floorRise"/>로 올라오는 데
                    // 1.5초가 걸린다. 그동안 착지가 다 걸러져 <b>걷기 시작하고 한참 뒤에야
                    // 첫 발소리가 났다.</b> 첫 골에 기준을 묶어 두면 그 다음부터는 정상이다.
                    f.Falling = false;
                    if (!f.Primed || f.DipY <= f.Floor + plantMargin)
                    {
                        f.Floor = f.DipY;        // 기준을 이번 착지에 다시 묶는다
                        f.Primed = true;
                        hits++;
                    }
                }

                f.PrevY = y;
                feet[i] = f;
            }

            Plants += hits;
            return hits;
        }
    }
}
