using UnityEngine;

namespace Demo.Destruction
{
    /// <summary>
    /// 건물의 네 옆면. 어느 면을 보고 있는가, 그 면이 얼마나 넓은가 [지시 2026-09-23].
    ///
    /// <b>입구는 정면(<see cref="Front"/>)에만 있다.</b> 그래서 "어느 면을 부수는가"가
    /// 연출에 직접 걸린다 — 앞에서 부수면 입구가 보이고, 뒤로 돌아가 부수면 안 보인다.
    /// 면은 상호작용한 <b>그 순간</b>에 정해져서 파괴가 끝날 때까지 바뀌지 않는다.
    ///
    /// 계산이 여기 모여 있는 이유는, 카메라와 조각 판이 <b>같은 답</b>을 써야 하기 때문이다.
    /// 각자 플레이어 방향을 계산하면 둘이 반 도만 어긋나도 텍스처가 비스듬히 잘린다.
    /// 소유권을 한 곳에 두는 것은 <see cref="Core.GameModeController"/>와 같은 이유다.
    ///
    /// 면 번호는 건물의 <b>로컬</b> 축 기준이다. 구역은 건물을 임의의 yaw로 깔기 때문에
    /// (ZoneBuilder), 월드 기준으로 잡으면 건물마다 입구가 어디인지 알 수 없다.
    /// </summary>
    public static class StructureFace
    {
        public const int Front = 0;   // +Z — 입구가 있는 면
        public const int Right = 1;   // +X
        public const int Back  = 2;   // -Z
        public const int Left  = 3;   // -X
        public const int Count = 4;

        /// <summary>정면인가. 입구가 보이는 유일한 면이다.</summary>
        public static bool HasEntrance(int face) => face == Front;

        public static string Name(int face)
        {
            switch (Wrap(face))
            {
                case Front: return "정면";
                case Right: return "우측면";
                case Back:  return "후면";
                default:    return "좌측면";
            }
        }

        static int Wrap(int face) => ((face % Count) + Count) % Count;

        /// <summary>
        /// <paramref name="from"/>에 선 사람이 보고 있는 면.
        ///
        /// <b>반드시 반너비로 정규화해야 한다.</b> <c>|x| &gt; |z|</c>로 그냥 비교하면
        /// 납작한 건물에서 무너진다 — 5.5 × 1.0짜리 건물은 정면에서 옆으로 조금만 비켜도
        /// x가 z보다 커져서 "측면"이 되고, 조각 판이 5.5m가 아니라 <b>1.0m</b> 폭으로 선다.
        /// 어느 면을 보는가는 거리가 아니라 <b>제 반너비 대비 얼마나 벗어났는가</b>가 정한다.
        /// </summary>
        public static int Facing(Transform t, Vector3 from, Vector3 size)
        {
            if (t == null) return Front;

            float best = float.MinValue;
            int pick = Front;
            for (int f = 0; f < Count; f++)
            {
                float s = Score(t, from, size, f);
                if (s > best) { best = s; pick = f; }
            }
            return pick;
        }

        /// <summary>
        /// 그 사람이 선 쪽의 면 <b>중에서, 화면에 실제로 보이던 것</b> [고침 2026-10-12].
        ///
        /// <b>왜 카메라를 봐야 하는가.</b> 필드 카메라는 수평각이 고정이다
        /// (<see cref="Core.FollowCamera.FieldYaw"/>). 그래서 건물 하나에서 화면에 나오는 벽은
        /// <b>언제나 두 면뿐</b>이고, 나머지 두 면은 플레이어가 한 번도 본 적이 없다.
        /// 실측(2026-10-12): 구역 39채가 전부 yaw 0 아니면 90이라
        /// <b>정면은 단 한 채에서도 화면에 나오지 않는다.</b>
        ///
        /// 서 있는 자리만 보고 면을 고르면, 24방향 중 <b>51%</b>에서 "본 적 없는 벽"이 나왔다.
        /// 기획자가 세 번 적은 "정면에서 눌렀는데 측면이 나온다"가 이것이다 —
        /// 돌아간 것이 아니라, <b>보고 있던 벽이 아닌 벽</b>을 보여 준 것이다.
        ///
        /// 그래서 후보를 <b>화면에 보이던 면</b>으로 먼저 거르고, 그 안에서 선 자리로 고른다.
        /// 보이는 면이 둘이므로 "어느 쪽으로 돌아가 눌렀는가"는 그대로 살아 있다.
        /// </summary>
        /// <param name="viewDir">카메라가 보는 수평 방향. 길이는 상관없다</param>
        public static int Facing(Transform t, Vector3 from, Vector3 size, Vector3 viewDir)
        {
            if (t == null) return Front;

            viewDir.y = 0f;
            if (viewDir.sqrMagnitude < 0.0001f) return Facing(t, from, size);
            viewDir.Normalize();

            float best = float.MinValue;
            int pick = -1;
            for (int f = 0; f < Count; f++)
            {
                Vector3 n = Normal(t, f);
                n.y = 0f;
                // 바깥쪽이 카메라를 마주 보는 면만 화면에 나온다.
                if (Vector3.Dot(n.normalized, -viewDir) <= 0.01f) continue;

                float s = Score(t, from, size, f);
                if (s > best) { best = s; pick = f; }
            }

            // 보이는 면이 하나도 없는 일은 기하학적으로 없지만, 없으면 예전 규칙으로 돌아간다.
            return pick >= 0 ? pick : Facing(t, from, size);
        }

        /// <summary>
        /// 그 면 쪽에 얼마나 치우쳐 서 있는가. 클수록 그 벽 앞이다.
        ///
        /// <b>반드시 반너비로 정규화해야 한다.</b> <c>|x| &gt; |z|</c>로 그냥 비교하면
        /// 납작한 건물에서 무너진다 — 5.5 × 1.0짜리 건물은 정면에서 옆으로 조금만 비켜도
        /// x가 z보다 커져서 "측면"이 되고, 조각 판이 5.5m가 아니라 <b>1.0m</b> 폭으로 선다.
        /// 어느 면인가는 거리가 아니라 <b>제 반너비 대비 얼마나 벗어났는가</b>가 정한다.
        /// </summary>
        static float Score(Transform t, Vector3 from, Vector3 size, int face)
        {
            // 방향으로 변환한다 — 위치로 하면 건물 스케일이 답을 비튼다.
            Vector3 d = t.InverseTransformDirection(from - t.position);
            float x = d.x / Mathf.Max(0.01f, size.x);
            float z = d.z / Mathf.Max(0.01f, size.z);

            switch (Wrap(face))
            {
                case Front: return z;
                case Right: return x;
                case Back:  return -z;
                default:    return -x;
            }
        }

        /// <summary>그 면의 바깥쪽 법선(월드). 카메라가 설 방향이자 조각 판이 볼 방향이다.</summary>
        public static Vector3 Normal(Transform t, int face)
        {
            Vector3 local;
            switch (Wrap(face))
            {
                case Front: local = Vector3.forward; break;
                case Right: local = Vector3.right;   break;
                case Back:  local = Vector3.back;    break;
                default:    local = Vector3.left;    break;
            }
            return t != null ? t.TransformDirection(local).normalized : local;
        }

        /// <summary>그 면의 가로 길이(m). 앞뒤면은 x, 옆면은 z다.</summary>
        public static float Width(Vector3 size, int face)
        {
            int f = Wrap(face);
            return f == Front || f == Back ? size.x : size.z;
        }

        /// <summary>
        /// 중심에서 그 면까지의 거리(m). 앞뒤면은 z의 절반, 옆면은 x의 절반이다.
        ///
        /// 카메라가 이만큼 더 물러나야 한다 — 조각 판은 <b>면</b>에 서는데 Transform은
        /// <b>중심</b>에 있어서, 중심 기준으로만 물러나면 판까지의 거리가 그만큼 짧아진다.
        /// </summary>
        public static float Offset(Vector3 size, int face)
        {
            int f = Wrap(face);
            return (f == Front || f == Back ? size.z : size.x) * 0.5f;
        }
    }
}
