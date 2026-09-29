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

            // 방향으로 변환한다 — 위치로 하면 건물 스케일이 답을 비튼다.
            Vector3 d = t.InverseTransformDirection(from - t.position);

            float ax = Mathf.Abs(d.x) / Mathf.Max(0.01f, size.x);
            float az = Mathf.Abs(d.z) / Mathf.Max(0.01f, size.z);

            if (az >= ax) return d.z >= 0f ? Front : Back;
            return d.x >= 0f ? Right : Left;
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
