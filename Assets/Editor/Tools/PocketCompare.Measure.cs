using System.Collections.Generic;
using Demo.Collection;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 비교 도구의 <b>자</b> 쪽 [분할 2026-10-08]. 저쪽(<see cref="PocketCompare"/>)은
    /// 두 모형을 돌리고, 이쪽은 돌아간 결과에 자를 댄다.
    ///
    /// 300줄 규칙(CLAUDE.md 3)을 넘겨 갈랐다. 축이 둘이라 거기서 잘랐다 —
    /// 경로·설정을 고칠 때와 "무엇을 숫자로 셀 것인가"를 고칠 때가 서로 다른 작업이다.
    /// 실제로 이 세션에서 <b>자가 틀려서</b> 한 번 다시 쟀다: 처음엔 잔해 덩어리의
    /// 가로세로비를 쟀는데, 기획 의도에서 타원이 되는 것은 <b>그물</b>이었다.
    /// </summary>
    public static partial class PocketCompare
    {
        static Sample Measure(CargoTow tow, Transform player, float t)
        {
            var s = new Sample { T = t };
            IReadOnlyList<Debris> towed = tow.Towed;
            if (towed.Count == 0) return s;

            Vector3 dir = -player.forward; dir.y = 0f; dir.Normalize();
            Vector3 side = new Vector3(dir.z, 0f, -dir.x);

            Vector3 mid = Vector3.zero;
            for (int i = 0; i < towed.Count; i++) mid += towed[i].transform.position;
            mid /= towed.Count;

            float aMin = float.MaxValue, aMax = float.MinValue;
            float sMin = float.MaxValue, sMax = float.MinValue;
            float maxR = 0f, near = float.MaxValue;

            Vector3 knot = tow.Net.Ready ? tow.Net.Centroid : mid;
            Vector3 hand = player.position;

            for (int i = 0; i < towed.Count; i++)
            {
                Vector3 p = towed[i].transform.position; p.y = 0f;
                Vector3 o = p - mid;
                float a = Vector3.Dot(o, dir), c = Vector3.Dot(o, side);
                if (a < aMin) aMin = a; if (a > aMax) aMax = a;
                if (c < sMin) sMin = c; if (c > sMax) sMax = c;

                Vector3 fromKnot = p - knot; fromKnot.y = 0f;
                if (fromKnot.magnitude > maxR) maxR = fromKnot.magnitude;

                Vector3 fromHand = p - hand; fromHand.y = 0f;
                if (fromHand.magnitude < near) near = fromHand.magnitude;
            }

            s.Along = aMax - aMin;
            s.Across = sMax - sMin;

            // <b>그물</b>의 폭. 기획 의도에서 타원이 되는 것은 테두리이지 잔해 덩어리가 아니다 —
            // 처음엔 잔해만 재고 "타원이 안 된다"고 결론 낼 뻔했다 [고침 2026-10-08].
            if (tow.Net.Ready)
            {
                int rn = tow.Net.Count;
                Vector3 rc = tow.Net.Centroid;
                float raMin = float.MaxValue, raMax = float.MinValue;
                float rsMin = float.MaxValue, rsMax = float.MinValue;
                for (int i = 0; i < rn; i++)
                {
                    Vector3 o = tow.Net.At(i) - rc; o.y = 0f;
                    float a = Vector3.Dot(o, dir), c2 = Vector3.Dot(o, side);
                    if (a < raMin) raMin = a; if (a > raMax) raMax = a;
                    if (c2 < rsMin) rsMin = c2; if (c2 > rsMax) rsMax = c2;
                }
                s.NetAlong = raMax - raMin;
                s.NetAcross = rsMax - rsMin;

                // <b>"끝쪽에 모이는가"를 직접 묻는다</b> [2026-10-08].
                // 치우침(m)은 그물이 클수록 커져서 종류 사이에 비교가 안 된다.
                // 뒤쪽 절반에 든 조각의 비율은 크기와 무관하다 — 고르면 0.5, 쌓이면 1에 가깝다.
                float half = (raMin + raMax) * 0.5f;
                int rear = 0;
                for (int i = 0; i < towed.Count; i++)
                {
                    Vector3 o = towed[i].transform.position - rc; o.y = 0f;
                    if (Vector3.Dot(o, dir) > half) rear++;
                }
                s.RearShare = rear / (float)towed.Count;
            }

            // 무게중심이 '뒤쪽'(= 끄는 방향의 반대)으로 얼마나 치우쳤는가.
            // 짐 덩어리의 기하 중심을 기준으로 재야 크기와 무관해진다.
            Vector3 fromCenter = mid - knot; fromCenter.y = 0f;
            s.Bias = -Vector3.Dot(fromCenter, -dir);

            s.MaxR = maxR;
            s.NearHand = near;
            // <b>속도는 하네스가 직접 잰다</b> [2026-10-08]. NetPocket도 같은 값을 들고 있지만
            // 그걸 쓰면 Slots 쪽에는 댈 자가 없어 "주머니만 튕긴다"가 공짜로 나온다.
            // 두 모형 다 트랜스폼이 프레임 사이에 얼마나 움직였는가로 잰다 — 같은 자다.
            Speeds(towed, s);
            s.MoveInertia = tow.Pocket.MoveInertia;
            s.MoveContain = tow.Pocket.MoveContain;
            s.MoveSeparate = tow.Pocket.MoveSeparate;
            s.Escaped = tow.Pocket.Escaped;
            s.Overlaps = tow.Pocket.Overlaps;
            s.Contacts = tow.Pocket.Contacts;

            // Slots 모형은 NetPocket을 안 돌리므로 집계가 0이다 — 여기서 직접 센다
            if (tow.Shape == CargoTow.HaulShape.Slots) CountForSlots(tow, s);
            return s;
        }

        /// <summary>
        /// 프레임 사이 이동량으로 속도를 낸다 — <b>튕김을 재는 자다</b> [2026-10-08].
        /// 플레이어는 6m/s로 간다. 끌려오는 돌덩이가 그보다 훨씬 빠르면 끌리는 게 아니라 튕기는 것이다.
        /// </summary>
        static readonly List<Vector3> s_was = new List<Vector3>(64);

        static void Speeds(IReadOnlyList<Debris> towed, Sample s)
        {
            if (s_was.Count != towed.Count)
            {
                s_was.Clear();
                for (int i = 0; i < towed.Count; i++) s_was.Add(towed[i].transform.position);
                return;                                  // 첫 프레임은 기준이 없다
            }

            float sum = 0f, max = 0f;
            for (int i = 0; i < towed.Count; i++)
            {
                Vector3 now = towed[i].transform.position;
                Vector3 d = now - s_was[i]; d.y = 0f;
                float sp = d.magnitude * 60f;            // dt = 1/60 고정
                sum += sp;
                if (sp > max) max = sp;
                s_was[i] = now;
            }
            s.MeanSpeed = towed.Count > 0 ? sum / towed.Count : 0f;
            s.MaxSpeed = max;
        }

        /// <summary>새 경우를 시작할 때 부른다 — 앞 경우의 위치가 남으면 첫 속도가 터진다.</summary>
        internal static void ResetSpeeds() => s_was.Clear();

        /// <summary>
        /// Slots 모형에도 같은 자를 댄다. 안 그러면 "새어 나감 0·겹침 0"이 구속의 성과처럼 보이는데,
        /// 사실은 <b>아무도 안 센 것</b>이다 — 그 차이가 이 비교의 핵심이다.
        /// </summary>
        static void CountForSlots(CargoTow tow, Sample s)
        {
            IReadOnlyList<Debris> towed = tow.Towed;
            for (int i = 0; i < towed.Count; i++)
                for (int j = i + 1; j < towed.Count; j++)
                {
                    Vector3 d = towed[j].transform.position - towed[i].transform.position; d.y = 0f;
                    float need = towed[i].SizeRadius + towed[j].SizeRadius;
                    if (d.sqrMagnitude < need * need - 1e-4f) s.Overlaps++;
                }

            if (!tow.Net.Ready) return;
            Vector3 cen = tow.Net.Centroid;
            int n = tow.Net.Count;
            for (int k = 0; k < towed.Count; k++)
            {
                Vector3 p = towed[k].transform.position;
                bool outside = false, touch = false;
                for (int i = 0; i < n; i++)
                {
                    Vector3 a = tow.Net.At(i), b = tow.Net.At((i + 1) % n);
                    Vector3 ab = b - a; ab.y = 0f;
                    float l2 = Mathf.Max(1e-8f, ab.sqrMagnitude);
                    Vector3 aq = p - a; aq.y = 0f;
                    Vector3 c = a + ab * Mathf.Clamp01(Vector3.Dot(aq, ab) / l2);
                    Vector3 away = p - c; away.y = 0f;
                    Vector3 inward = cen - c; inward.y = 0f;
                    if (Vector3.Dot(away, inward) < 0f) outside = true;
                    if (away.magnitude < towed[k].SizeRadius + 0.05f) touch = true;
                }
                if (outside) s.Escaped++;
                if (touch) s.Contacts++;
            }
        }

    }
}
