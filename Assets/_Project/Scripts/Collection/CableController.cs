using System.Collections.Generic;
using Demo.Core;
using Demo.Data;
using UnityEngine;

namespace Demo.Collection
{
    /// <summary>
    /// 케이블 전개와 폐합. 플레이어에 붙인다.
    ///
    /// 버튼을 누른 채 이동하면 선이 남고, 선이 다 이어지면 그 안의 잔해를 끌어서 옮긴다.
    /// 판정은 전부 바닥 평면 2D다 (GAME_DESIGN §5.1).
    ///
    /// 무거운 계산(내부 판정)은 <b>닫히는 순간 1회만</b> 돈다. 매 프레임 돌리면 모바일에서 죽는다.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class CableController : MonoBehaviour
    {
        [SerializeField, Tooltip("케이블 선을 그리는 높이(m)")]
        float drawHeight = 0.15f;

        [SerializeField] CargoTow cargo;

        readonly List<Vector2> points = new List<Vector2>(256);
        readonly List<Vector2> polygon = new List<Vector2>(256);
        readonly List<Debris> captured = new List<Debris>(64);
        readonly List<Debris> loose = new List<Debris>(128);   // 폐합마다 새로 만들면 GC가 튄다

        LineRenderer line;
        float trailLength;
        bool deploying;

        /// <summary>지금 전개 중인 궤적 길이(m). HUD가 읽는다.</summary>
        public float TrailLength => trailLength;

        /// <summary>마지막 폐합에서 잡은 잔해 수. 연출 트리거용.</summary>
        public int LastCaptureCount { get; private set; }

        /// <summary>토대에 막혀 궤적이 끊긴 횟수 [11번 CableOnly]. 실측용.</summary>
        public int BlockedCount { get; private set; }

        void Awake()
        {
            line = GetComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 0;
            if (cargo == null) cargo = GetComponent<CargoTow>();
        }

        /// <summary>
        /// 매 프레임 InputRouter/Breaker 쪽에서 부른다.
        /// </summary>
        /// <param name="active">버튼을 누른 채 이동 중인가</param>
        public void Tick(bool active)
        {
            if (!active)
            {
                if (deploying) Clear();
                return;
            }

            deploying = true;

            GameConfig cfg = GameConfigRef.Current;
            if (cfg == null) return;

            Vector2 here = LoopDetector.Flatten(transform.position);

            if (points.Count == 0)
            {
                Add(here);
                return;
            }

            Vector2 last = points[points.Count - 1];
            if (Vector2.Distance(last, here) < cfg.cablePointInterval) return;

            // 토대 위로는 케이블이 지나가지 못한다 [11번 CableOnly, 2026-10-02].
            //
            // 막히면 <b>궤적을 끊고 그때까지의 고리를 포기한다.</b> 거기서 멈추고 기다리게 하면
            // 플레이어는 자기가 왜 못 그리는지 모른 채 서 있게 된다 — 끊어 주면 선이 사라져서
            // "여기는 안 된다"가 바로 보인다. 규칙이 None이면 이 줄은 한 번도 참이 되지 않는다.
            if (cfg.foundationRule == GameConfig.FoundationRule.CableOnly &&
                Hazard.ObstacleField.BlocksCable(last, here))
            {
                BlockedCount++;
                Clear();
                return;
            }

            Add(here);
            TrimToMaxLength(cfg.cableMaxLength);

            if (LoopDetector.TryFindClosure(points, out int start, out Vector2 hit))
                CloseLoop(cfg, start, hit);
        }

        void Add(Vector2 p)
        {
            if (points.Count > 0) trailLength += Vector2.Distance(points[points.Count - 1], p);
            points.Add(p);
            Redraw();
        }

        /// <summary>길이 상한을 넘으면 오래된 쪽부터 버린다. 케이블이 무한히 길어지면 고리가 공짜가 된다.</summary>
        void TrimToMaxLength(float maxLength)
        {
            while (trailLength > maxLength && points.Count > 2)
            {
                trailLength -= Vector2.Distance(points[0], points[1]);
                points.RemoveAt(0);
            }
        }

        void CloseLoop(GameConfig cfg, int start, Vector2 hit)
        {
            LoopDetector.BuildPolygon(points, start, hit, polygon);

            // 제자리에서 비벼 만든 바늘 모양 고리를 걸러낸다.
            if (PolygonCapture.Area(polygon) < cfg.cableMinLoopArea)
            {
                Clear();
                return;
            }

            // 견인 중인 잔해는 다시 잡지 않는다 — 이미 내 뒤에 딸려 있다.
            loose.Clear();
            for (int i = 0; i < Debris.Active.Count; i++)
            {
                Debris d = Debris.Active[i];
                if (d != null && !d.IsTowed) loose.Add(d);
            }

            PolygonCapture.SelectInside(polygon, loose, d => d.FlatPosition, captured);
            LastCaptureCount = captured.Count;

            if (captured.Count > 0 && cargo != null) cargo.Add(captured);

            // 고리는 소모된다. 이어서 또 둘러싸면 CargoTow에서 합쳐진다.
            Clear();
        }

        void Clear()
        {
            points.Clear();
            polygon.Clear();
            captured.Clear();
            trailLength = 0f;
            deploying = false;
            line.positionCount = 0;
        }

        void Redraw()
        {
            line.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++)
                line.SetPosition(i, new Vector3(points[i].x, drawHeight, points[i].y));
        }
    }
}
