using UnityEngine;

namespace Demo.Player
{
    /// <summary>
    /// 클립에 구워진 <b>자세 쏠림</b>을 되돌린다 [지시 10번, 2026-10-01].
    ///
    /// <b>무엇이 틀어져 있었나</b> [실측 2026-10-01, 사이클 두 지점에서 각각 확인].
    ///
    /// | | 몸통(Spine2) 좌우틀림 | 머리 좌우틀림 | 옆기울 |
    /// |---|---|---|---|
    /// | 정지 | −1.65도 (몸은 똑바름) | <b>+6.19도</b> | 머리 +3.81 |
    /// | 걷기 | −10.7 / −7.7 | −4.0 / −1.8 | 몸통 +3.5~+4.4 |
    /// | 달리기 | <b>−21.5 / −18.3</b> | −13.5 / −9.3 | 몸통 +3.7~+5.2, 머리 +8.2 |
    ///
    /// 두 지점 모두 <b>같은 부호</b>라 흔들림이 아니라 상수 쏠림이다.
    /// 클립(Idle/Walk/Run)에 그대로 구워져 있고, 리그가 generic이라 클립이 뼈를 직접 쓴다.
    ///
    /// <b>척추는 안 휘었다</b> [검증 2026-10-01]. 달릴 때 옆기울이 Hips 4.35 → Neck 4.54로
    /// 거의 평평하다(변동 0.7도). 측만증이면 중간에서 부호가 뒤집혀야 한다.
    /// 지금은 <b>골반부터 통째로 기운 것</b>이고, 머리가 혼자 +4를 더 얹어 최종 8도가 된다.
    ///
    /// 그래서 손대는 자리는 <b>둘뿐</b>이다 — Hips(몸 전체)와 Head(머리 몫).
    /// 척추 중간을 건드리면 없던 휨을 만든다.
    ///
    /// <b>왜 뼈를 직접 돌리나.</b> 클립을 다시 굽는 것이 정석이지만 GLB → Blender → FBX를
    /// 다시 타야 하고, 쏠림이 상수라 런타임에서 빼는 것으로 같은 결과가 나온다.
    /// 클립을 고치게 되면 여기 값을 0으로 두면 된다.
    ///
    /// <b>파괴 화면에서는 끈다.</b> <see cref="StrikeArmRig"/>이 미리 잰 고정 오프셋으로
    /// 주먹을 과녁에 박아 두는데(어긋남 0.0005), Hips를 돌리면 그 조준이 통째로 틀어진다.
    ///
    /// MonoBehaviour가 아닌 이유는 <see cref="PunchRush"/>와 같다 —
    /// 수명주기도 코루틴도 필요 없고, 컴포넌트로 만들면 배선과 실행 순서 문제만 새로 생긴다.
    /// </summary>
    [System.Serializable]
    public sealed class PostureFix
    {
        [Tooltip("켜고 끄기. 끄면 뼈를 건드리지 않는다 — 클립을 다시 구우면 여기를 끈다")]
        [SerializeField] bool enabled = true;

        [Tooltip("되돌리는 정도. 1이면 실측한 쏠림을 전부 뺀다 [제안·미승인 2026-10-01]. " +
                 "전부 빼도 되는 이유는 쏠림이 '연출'이 아니라 '치우침'이기 때문이다 — " +
                 "팔다리 흔들림은 Hips 아래/위에서 그대로 살아 있다")]
        [SerializeField, Range(0f, 1f)] float strength = 1f;

        [Header("몸통 — 움직일 때만. 멈춰 있을 때는 이미 똑바르다")]
        [Tooltip("달릴 때 되돌릴 좌우틀림(도) [제안·미승인 2026-10-01].\n" +
                 "<b>2.5초(약 150프레임)씩 50표본으로 쟀다</b> — 짧게 재면 걸음 한 주기도 못 덮어서, " +
                 "쟤는 때마다 값이 −3.7에서 −7.2로 널뛴다.\n" +
                 "보정 전 Hips: 걷기 −5.83도(범위 −7.82~−2.54) / 달리기 −8.79도(범위 −17.95~−1.94). " +
                 "<b>범위 전체가 음수</b>라 흔들림이 아니라 치우침이다.\n" +
                 "걸 때는 √속도를 곱한다 — 5.83/√0.45 = 8.69로 달리기 8.79와 거의 같다")]
        [SerializeField, Range(-30f, 30f)] float torsoYaw = 8.75f;

        [Tooltip("<b>걸을 때</b> 되돌릴 좌우 기울기(도) [제안·미승인 2026-10-01].\n" +
                 "값은 <b>위치로 잰 것</b>이다 — Hips에서 Neck으로 가는 방향이 좌우로 얼마나 " +
                 "눕는가. 뼈의 로컬 축으로 재면 <b>앞뒤 숙임이 좌우 기울기로 샌다</b>. " +
                 "그 탓에 한때 4.09도로 잘못 읽고 멀쩡한 허리를 꺾어 놓은 적이 있다.\n" +
                 "실측: 걷기 +2.7도(진짜 기울어져 있다) / 달리기 +0.4도(이미 똑바르다).\n" +
                 "<b>달리기에는 안 건다</b> — 아래 leanScale이 거기서 0이 된다")]
        [SerializeField, Range(-15f, 15f)] float torsoLean = 3.0f;

        [Header("머리 — 멈춰 있을 때도 틀어져 있다")]
        [Tooltip("머리가 <b>몸통보다 더</b> 틀어진 몫(도) [제안·미승인]. " +
                 "정지에서 머리 +6.2 / 골반 +1.6이라 차이가 +4.6이고, " +
                 "달리기에서 머리 −13.5 / 골반 −17.6이라 차이가 +4.1이다 — " +
                 "자세와 무관하게 늘 같은 몫이라 상수로 뺀다")]
        [SerializeField, Range(-20f, 20f)] float headYaw = 4.3f;

        [Tooltip("머리가 몸통보다 더 기운 몫(도) [제안·미승인]. " +
                 "부호는 <b>실측으로 정했다</b> — 포즈를 얼려 놓고 +4와 −4를 같은 포즈에 걸어 본 결과, " +
                 "+4는 머리 옆기울을 2.98 -> 8.20으로 <b>악화</b>시켰고 −4가 2.98 -> 0.24로 폈다. " +
                 "좌우틀림도 같이 4.44 -> −0.86이 됐다 (회전이 서로 물려 있다)")]
        [SerializeField, Range(-15f, 15f)] float headLean = -4.0f;

        [Tooltip("머리 앞뒤 기울기(도) [제안·미승인]. 정지에서 +6.3도 숙이고 있다. " +
                 "로봇 머리가 큰 상자라 이 각이 '고개를 떨구고 있다'로 아주 크게 읽힌다")]
        [SerializeField, Range(-20f, 20f)] float headPitch = 6.0f;

        Transform hips, neck, head;
        bool bound;

        /// <summary>
        /// 뼈를 한 번만 찾아 둔다. 규칙 5 — Update에서 Find를 돌리지 않는다.
        /// Neck이 필요한 이유는 <b>척추 방향</b>을 Hips→Neck으로 재기 때문이다 (Apply 참조).
        /// </summary>
        public void Bind(Transform root)
        {
            hips = null; neck = null; head = null;
            if (root != null)
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (hips == null && t.name == "Hips") hips = t;
                    else if (neck == null && t.name == "Neck") neck = t;
                    else if (head == null && t.name == "Head") head = t;
                }
            bound = hips != null || head != null;
        }

        public bool Ready => bound && enabled;

        /// <summary>
        /// 애니메이터가 뼈를 쓴 <b>뒤에</b> 불러야 한다 — 즉 LateUpdate다.
        /// Update에서 부르면 같은 프레임에 애니메이터가 덮어써서 아무 일도 안 일어난다.
        /// </summary>
        /// <param name="owner">기준 축을 줄 트랜스폼. 플레이어 루트다</param>
        /// <param name="move01">0=정지, 1=최고속. 몸통 보정이 이만큼만 걸린다</param>
        public void Apply(Transform owner, float move01)
        {
            if (!enabled || !bound || owner == null) return;

            float k = Mathf.Clamp01(strength);
            Vector3 up = owner.up, fwd = owner.forward, right = owner.right;

            // 몸통. 멈춰 있을 때는 건드릴 것이 없다 — 실측이 +1.34도다.
            // Hips를 돌리면 척추도 다리도 같이 따라온다. 그게 맞다:
            // 쏠림이 골반에서 시작해 위아래로 똑같이 퍼져 있었다.
            float m = Mathf.Clamp01(move01);

            // <b>좌우틀림은 √속도에 비례한다</b> [실측 2026-10-01, 2.5초씩 50표본].
            // 걷기 −5.83도 / 달리기 −8.79도인데 5.83/√0.45 = 8.69로 달리기 값과 거의 같다.
            // 그냥 속도에 비례시키면 걷기가 절반밖에 안 펴진다.
            float yawScale = Mathf.Sqrt(m);

            // <b>옆기울 치우침은 걷기에만 있다</b> [실측 2026-10-01].
            // 걷기는 범위가 +2.23~+5.22로 전부 양수(치우침)인데,
            // 달리기는 −5.93~+5.52로 <b>0을 중심으로 대칭</b>이다 — 그건 달릴 때 몸이
            // 좌우로 흔들리는 정상 동작이라 빼면 오히려 뻣뻣해진다.
            // 치우침이 Walk 클립에서 오므로, 보정도 Walk의 블렌드 가중치를 따라가야 한다 —
            // 가운데서 최대, 달리기(1)에서 0인 모양이 그것이다.
            float leanScale = 4f * m * (1f - m);

            if (hips != null && m > 0.0001f)
            {
                // <b>척추 자신의 축으로 돌린다. 월드 수직축이 아니다</b> [고침 2026-10-01].
                //
                // 달릴 때 척추는 앞으로 17.9도 숙어 있다. 그걸 월드 수직축으로 8.75도 돌리면
                // 숙인 축이 옆으로 쓸려 나가면서 <b>없던 좌우 기울기가 생긴다</b> —
                // asin(sin17.9 × sin8.75) = 2.68도, 실측 +2.8도와 같다.
                // 달리기는 원래 기울기가 0.4도로 멀쩡했는데 보정이 3.2도로 만들어 놓고 있었다.
                //
                // 척추 축으로 돌리면 척추 방향 자체가 안 움직이므로 기울기가 생길 수 없다.
                // "몸을 틀었다"는 말뜻도 수직축이 아니라 <b>제 몸통 축</b>을 중심으로 돈 것이다.
                Vector3 twistAxis = up;
                if (neck != null)
                {
                    Vector3 spine = neck.position - hips.position;   // 돌리기 <b>전에</b> 잰다
                    if (spine.sqrMagnitude > 1e-6f) twistAxis = spine.normalized;
                }

                hips.rotation = Quaternion.AngleAxis(torsoYaw * yawScale * k, twistAxis)
                              * Quaternion.AngleAxis(torsoLean * leanScale * k, fwd)
                              * hips.rotation;
            }

            // 머리. 몸통과 달리 멈춰 있을 때도 틀어져 있어서 늘 같은 몫을 뺀다.
            // Hips 보정이 이미 들어간 뒤라, 여기서 빼는 것은 '머리가 더 틀어진 몫'뿐이다.
            if (head != null && k > 0.0001f)
                head.rotation = Quaternion.AngleAxis(-headYaw * k, up)
                              * Quaternion.AngleAxis(-headLean * k, fwd)
                              * Quaternion.AngleAxis(-headPitch * k, right)
                              * head.rotation;
        }
    }
}
