using UnityEngine;

namespace Demo.Collection
{
    /// <summary>
    /// 주머니의 <b>움직임</b> 쪽 [분할 2026-10-08] — 관성·서로 떼어내기·한 프레임 상한.
    /// 저쪽(<see cref="NetPocket"/>)은 <b>경계와의 관계</b>를 다룬다.
    ///
    /// 300줄 규칙(CLAUDE.md 3)을 넘겨 갈랐고, 축은 이 세션의 작업이 정해 줬다 —
    /// "잔해끼리 튕기는 정도가 너무 크다"를 고치는 동안 건드린 것은 전부 이쪽 셋이었고,
    /// 경계 판정은 한 줄도 안 바뀌었다.
    /// </summary>
    public sealed partial class NetPocket
    {
        /// <summary>
        /// ① 적분 — 테두리와 같은 베를레다. 속도는 (지금 − 직전)에 들어 있다.
        /// 여기서는 안 묶는다. 관성만 묶으면 구속이 민 몫이 그대로 새어 나가서,
        /// <see cref="Brake"/>가 프레임 전체 이동을 한 번에 본다 [2026-10-08].
        /// </summary>
        void Integrate(float dt)
        {
            float keep = 1f - Mathf.Clamp01(groundDrag);
            for (int i = 0; i < count; i++)
            {
                Vector3 v = (pos[i] - prev[i]) * keep;
                v.y = 0f;
                prev[i] = pos[i];
                pos[i] += v;
                if (v.magnitude > MoveInertia) MoveInertia = v.magnitude;
            }
        }

        /// <summary>
        /// ② 잔해끼리 밀어낸다. <b>이것이 "끝쪽에 모인다"를 만든다</b> —
        /// 뒤쪽 테두리에 눌린 조각이 뒤에 오는 조각을 받아 주므로 더미가 쌓인다.
        /// 밀어내기가 없으면 전부 테두리 한 줄에 달라붙고 가운데가 빈다.
        ///
        /// <b>떼어낸 만큼을 직전 위치에도 더한다</b> [고침 2026-10-08].
        /// 베를레는 속도를 <c>(지금 − 직전)</c>으로 들고 있으므로, 위치만 옮기면
        /// <b>그 보정이 곧 속도가 된다</b> — 0.3m 떼어내면 0.3m/프레임, 즉 18m/s다.
        /// 기획자 플레이에서 "잔해끼리 튕기는 정도가 너무 크다"고 나온 것이 이것이다.
        /// 직전 위치를 같이 옮기면 겹침만 풀리고 속도는 안 변한다 — 반발 0,
        /// 콘크리트 덩어리에 맞는 거동이다. <see cref="bounce"/>로 그 사이를 고른다.
        /// </summary>
        void Separate()
        {
            float keepVel = 1f - Mathf.Clamp01(bounce);
            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                {
                    Vector3 d = pos[j] - pos[i]; d.y = 0f;
                    float need = rad[i] + rad[j];
                    float sq = d.sqrMagnitude;
                    if (sq >= need * need || sq < 1e-8f) continue;

                    float len = Mathf.Sqrt(sq);
                    Vector3 fix = d * ((need - len) / len * 0.5f);
                    pos[i] -= fix; pos[j] += fix;
                    prev[i] -= fix * keepVel; prev[j] += fix * keepVel;
                    if (fix.magnitude > MoveSeparate) MoveSeparate = fix.magnitude;
                }
        }

        /// <summary>
        /// 한 프레임에 조각이 갈 수 있는 거리를 묶는다 [2026-10-08].
        ///
        /// <b>왜 단계별 손질로는 안 되는가.</b> 보정이 속도로 남는 정도(<see cref="bounce"/>)를
        /// 양 끝까지 쓸어 봤지만 총량이 안 줄었다 — 0이면 관성이 죽는 대신 테두리 밀기가
        /// 0.98m(59m/s)로 터지고, 1이면 관성으로 남는다. <b>속도가 어느 통에 담기느냐만 바뀐다.</b>
        /// 그래서 통을 고르는 대신 합계를 묶는다.
        ///
        /// 물리적으로도 이게 맞다 — 잔해는 돌덩이지 총알이 아니다. 깊이 파고든 것은
        /// 한 프레임에 뽑아내지 말고 <b>여러 프레임에 걸쳐</b> 풀면 된다. 구속은 반복해서 풀리므로
        /// 늦게 풀릴 뿐 안 풀리지 않는다.
        ///
        /// 직전 위치도 같이 당겨서 브레이크 자체가 속도를 만들지 않게 한다.
        /// </summary>
        void Brake()
        {
            float lim = Mathf.Max(0.01f, maxStep);
            for (int i = 0; i < count; i++)
            {
                Vector3 moved = pos[i] - start[i]; moved.y = 0f;
                float len = moved.magnitude;
                if (len <= lim) continue;

                Vector3 cut = moved * (1f - lim / len);
                pos[i] -= cut;
                prev[i] -= cut;
            }
        }

    }
}
