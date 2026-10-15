using System.Collections.Generic;
using UnityEngine;

namespace Demo.Collection
{
    /// <summary>
    /// 플레이어가 지나온 자리 [지시 2026-10-15].
    ///
    /// 기획자: "구멍이 플레이어가 못가는 곳은 가고, 갈 수 있는 곳을 못가는 현상 →
    /// 플레이어가 갈 수 있는 만큼만 가고, 못가는 곳은 같이 못가는게 좋지 않을까."
    ///
    /// <b>왜 목줄이 아니라 자취인가.</b> 전에는 구멍이 <b>시작점에서 45m</b>라는 원 안에
    /// 묶여 있었다. 실측하면 구역은 x −51.8~51.8 · z 2.4~84.8이고 구멍 시작점은 그
    /// 가장자리(0, −10)에 있어서, <b>39채 중 18채(46%)가 그 원 밖</b>이었다.
    /// 동시에 그 원 안쪽이면 건물 속이든 어디든 지나다녔다. 즉 못 가는 곳은 가고
    /// 갈 수 있는 곳은 못 가는, 정확히 거꾸로 된 제한이었다.
    ///
    /// 자취를 따라가면 둘이 한꺼번에 풀린다. <b>플레이어가 밟은 자리만</b> 목적지가 되므로
    /// 갈 수 없는 곳은 자취에 없고, 갈 수 있는 곳은 걸어가기만 하면 자취가 된다.
    /// 숫자로 범위를 정할 필요가 사라진다 — 범위를 플레이어가 정한다.
    ///
    /// <b>되돌아오면 고리가 잘린다.</b> 구멍이 덮은 점은 지난 것으로 친다. 그래서
    /// 플레이어가 건물을 한 바퀴 돌아 제자리로 오면 구멍이 그 바퀴를 따라 돌지 않고
    /// 가로질러 간다 — 뒤를 끝없이 끌고 다니는 뱀이 되지 않는다.
    ///
    /// <b>간격은 새 수치가 아니다.</b> 구멍 반지름에서 끌어온다 (<see cref="Record"/>).
    /// </summary>
    public sealed class HoleTrail
    {
        readonly List<Vector3> points = new List<Vector3>();

        /// <summary>남아 있는 자취 점의 수. 진단용이다.</summary>
        public int Count => points.Count;

        public void Clear() => points.Clear();

        /// <summary>
        /// 플레이어가 충분히 움직였으면 한 점 찍는다.
        ///
        /// <paramref name="spacing"/>는 <b>구멍 반지름의 절반</b>을 넣는다 — 부르는 쪽이 정한다.
        /// 해상도는 구멍 크기에 비례해야 한다: 지름 8m짜리가 25cm마다 점을 찍을 이유가 없고,
        /// 반지름의 절반이면 구멍 안에 늘 두 점 이상이 들어와 <see cref="Next"/>가 끊기지 않는다.
        ///
        /// 높이는 버린다. 구멍은 땅에 붙어 있고 플레이어는 뛰기도 한다.
        /// </summary>
        public void Record(Vector3 at, float spacing)
        {
            if (spacing <= 0.001f) return;

            if (points.Count > 0)
            {
                Vector3 last = points[points.Count - 1];
                float dx = at.x - last.x, dz = at.z - last.z;
                if (dx * dx + dz * dz < spacing * spacing) return;
            }
            points.Add(new Vector3(at.x, 0f, at.z));
        }

        /// <summary>
        /// 구멍이 다음으로 향할 자리. 자취가 다 소진됐으면 false다.
        ///
        /// 구멍이 <b>덮고 있는</b> 점은 먼저 버린다. 이것이 고리를 자르는 장치다 —
        /// 지나온 길 위로 플레이어가 다시 지나가면 그 사이의 점들이 통째로 사라진다.
        /// </summary>
        /// <param name="holeAt">구멍의 지금 자리</param>
        /// <param name="holeRadius">구멍 반지름. 이 안에 든 점은 지난 것으로 친다</param>
        /// <param name="to">다음 목적지 (높이는 holeAt의 것을 쓴다)</param>
        public bool Next(Vector3 holeAt, float holeRadius, out Vector3 to)
        {
            float rr = Mathf.Max(0.05f, holeRadius);
            rr *= rr;

            // <b>구멍이 덮은 점 중 가장 늦은 것까지 통째로 버린다</b> [고침 2026-10-15].
            //
            // 전에는 앞에서부터 <b>연속으로</b> 든 점만 버렸다. 그래서 플레이어가 나갔다가
            // 거의 같은 길로 되돌아오면 돌아온 쪽 점이 안 버려지고, 구멍이 <b>그 바퀴를
            // 통째로 돌아야</b> 했다. 그동안 구멍은 엉뚱한 데서 옛 길을 걷고 있고
            // 플레이어 발밑의 잔해는 안 먹혔다 — 기획자가 "끝에 가로막혀 잔해를 먹지
            // 못하는 모습"으로 찍어 준 그림이다.
            //
            // 목록 전체를 보고 <b>마지막으로 덮인 점</b>을 찾아 거기까지 자르면 고리가
            // 실제로 끊어진다. 점이 2천 개라도 프레임당 2천 번 비교라 값이 나가지 않는다.
            int last = -1;
            for (int i = 0; i < points.Count; i++)
            {
                float dx = points[i].x - holeAt.x, dz = points[i].z - holeAt.z;
                if (dx * dx + dz * dz <= rr) last = i;
            }
            if (last >= 0) points.RemoveRange(0, last + 1);

            if (points.Count == 0) { to = holeAt; return false; }

            to = new Vector3(points[0].x, holeAt.y, points[0].z);
            return true;
        }
    }
}
