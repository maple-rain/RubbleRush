using Demo.Collection;
using Demo.Core;
using UnityEngine;

namespace Demo.UI
{
    /// <summary>
    /// 구멍이 화면 밖으로 나가면 화면 가장자리에 화살표로 방향을 표시한다 [지시 2026-09-23].
    ///
    /// <b>미니맵이 아니다.</b> 거리도 지도도 좌표도 주지 않는다. 주는 것은 방향 하나다 —
    /// 구멍은 구역에 하나뿐이고(<see cref="Hole"/>) 플레이어는 잔해를 안고 그리로 돌아가야
    /// 하는데, 쿼터뷰 카메라가 플레이어만 따라다니므로 조금만 멀어져도 구멍이 화면에서 사라진다.
    /// 그때 "저쪽"만 알려주면 충분하다.
    ///
    /// <b>Canvas도 EventSystem도 쓰지 않는다.</b> <see cref="TouchControls"/>와 같은 이유다 —
    /// uGUI로 만들면 프리팹과 씬 와이어링이 늘어나는데 그건 사람이 손으로 하는 작업이고
    /// (CLAUDE.md 규칙 1), 텍스처 한 장을 회전시켜 그리는 데 EventSystem 한 겹이 필요하지 않다.
    /// 텍스처는 씬에 물려 둔다 — 예전에는 SceneBootstrap이 코드로 꽂아 줬지만
    /// 그 도구는 지웠다 [2026-10-01]. 씬이 1인칭 팔 리그처럼 되살릴 수 없는 참조를
    /// 들고 있게 되면서, 씬을 다시 짓는 도구는 쓸모보다 위험이 커졌다.
    ///
    /// <b>파괴 화면에서는 나오지 않는다.</b> 그때 카메라는 건물 1층에 붙어 있고 구멍은
    /// 거의 항상 화면 밖이라, 켜 두면 화살표가 가장자리에 붙박이로 남아 시야만 가린다.
    ///
    /// 카메라와 같은 오브젝트에 붙인다 (<see cref="TargetSightline"/>와 같은 자리).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class OffscreenMarker : MonoBehaviour
    {
        [SerializeField, Tooltip("화면 밖 방향을 가리킬 화살표. 오른쪽(+X)을 향한 그림이어야 한다.\n" +
                                 "씬에 T_ArrowOffscreen이 물려 있다. 비면 화살표가 안 그려진다")]
        Texture2D arrow;

        [SerializeField, Range(0.03f, 0.25f), Tooltip("화살표 한 변. 화면 높이 대비")]
        float sizeFraction = 0.075f;

        [SerializeField, Range(0.5f, 2f),
         Tooltip("가장자리에서 띄우는 거리. 화살표 한 변의 몇 배인가.\n" +
                 "회전하면 정사각형의 대각 반지름(0.707배)까지 삐져나오므로 그보다 커야 안 잘린다")]
        float edgeInset = 0.75f;

        [SerializeField, Tooltip("화살표 색. 알파는 Style()이 다시 정한다")]
        Color tint = Color.white;

        [SerializeField, Tooltip("노치·펀치홀을 피해 안전 영역 안에만 그린다. 모바일용")]
        bool respectSafeArea = true;

        Camera cam;
        Hole hole;

        void Awake() => cam = GetComponent<Camera>();

        /// <summary>
        /// 지금 가리킬 대상. 구멍은 ZoneBuilder가 구역을 지을 때 생기므로 Awake에서는 아직 없다.
        /// 그래서 캐시하되 매번 확인한다 — 구역이 다시 깔리면 예전 구멍은 파괴된 객체가 된다.
        /// </summary>
        Hole Target()
        {
            if (hole != null) return hole;
            GameLoop loop = GameLoop.Instance;
            return hole = loop != null ? loop.ActiveHole : null;
        }

        /// <summary>
        /// 구멍이 <b>어디로 가는지</b> 바닥에 표시한다 [지시 2026-10-14].
        ///
        /// <b>왜 필요한가.</b> 구멍은 최종 단계가 되면 스스로 떠도는데 목적지가 무작위다.
        /// 속도를 1.2에서 4m/s로 올리고 나니 "어디로 갈지 예측이 안 된다"가 더 커졌다.
        /// 지금 어디 있는지(화살표)만으로는 짐을 안고 마중 나갈 수가 없다.
        ///
        /// <b>따라오는 카드를 골랐으면 안 그린다.</b> 그때 목적지는 나 자신이라
        /// 표시가 늘 발밑에 붙어 시야만 가린다.
        ///
        /// 원은 점 여덟 개로 그린다 — 원형 텍스처를 따로 만들 만한 일이 아니다.
        /// </summary>
        void DriftHint(Vector3 holeScreen)
        {
            if (!hole.IsDrifting) return;
            if (Core.Upgrades.Has(Core.Upgrades.HoleFollows)) return;

            Vector3 t = cam.WorldToScreenPoint(hole.DriftTarget);
            if (t.z <= 0f) return;                       // 카메라 뒤 — 그릴 자리가 없다

            Vector2 to = new Vector2(t.x, Screen.height - t.y);
            float ring = Screen.height * 0.022f;
            float dot = Mathf.Max(2f, Screen.height * 0.004f);

            // 구멍에서 목적지까지 점선 — '여기서 저기로'가 선으로 읽힌다
            if (holeScreen.z > 0f)
            {
                Vector2 from = new Vector2(holeScreen.x, Screen.height - holeScreen.y);
                float span = Vector2.Distance(from, to);
                int steps = Mathf.Clamp(Mathf.RoundToInt(span / (dot * 5f)), 0, 40);
                for (int i = 1; i < steps; i++)
                {
                    Vector2 p = Vector2.Lerp(from, to, i / (float)steps);
                    UiSkin.Fill(new Rect(p.x - dot * 0.5f, p.y - dot * 0.5f, dot, dot),
                                new Color(1f, 1f, 1f, 0.22f));
                }
            }

            // 목적지 원
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                var p = new Vector2(to.x + Mathf.Cos(a) * ring, to.y + Mathf.Sin(a) * ring);
                UiSkin.Fill(new Rect(p.x - dot, p.y - dot, dot * 2f, dot * 2f), UiSkin.Accent);
            }
        }

        bool ShouldDraw()
        {
            if (arrow == null || cam == null) return false;

            GameModeController mode = GameModeController.Instance;
            if (mode != null && !mode.IsField) return false;      // 파괴 화면·종료 화면에서는 안 그린다

            GameLoop loop = GameLoop.Instance;
            return loop != null && loop.IsRunning && Target() != null;
        }

        void OnGUI()
        {
            // IMGUI는 한 프레임에 Layout과 Repaint로 두 번 들어온다. 걸러내지 않으면
            // 같은 계산을 두 번 하고, 행렬을 두 번 건드린다.
            if (Event.current.type != EventType.Repaint || !ShouldDraw()) return;

            Vector3 world = hole.transform.position;
            Vector3 sp = cam.WorldToScreenPoint(world);

            DriftHint(sp);

            // 카메라 뒤에 있으면 WorldToScreenPoint가 중심 대칭으로 뒤집힌 점을 준다.
            // 그 점의 위치는 의미가 없다 — 쓸 수 있는 것은 방향뿐이고, 그마저 뒤집어야 한다.
            bool behind = sp.z <= 0f;

            // 보이는가를 판정하는 사각형과 화살표를 붙이는 사각형은 <b>같아야 한다.</b>
            // 다르면 그 틈에서 구멍도 안 보이고 화살표도 안 나오는 구간이 생긴다 —
            // 노치 밑에 구멍이 들어간 순간이 정확히 그 틈이다.
            Rect box = respectSafeArea ? Screen.safeArea
                                       : new Rect(0f, 0f, Screen.width, Screen.height);

            Vector2 center = box.center;
            Vector2 dir = new Vector2(sp.x, sp.y) - center;
            if (behind) dir = -dir;

            if (!behind && box.Contains(new Vector2(sp.x, sp.y)))
                return;                                            // 화면 안이다 — 표시할 이유가 없다

            // 정확히 화면 중앙(또는 카메라 바로 뒤 중심축)에 걸린 순간. 방향이 정의되지 않는다.
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();

            float size = Screen.height * sizeFraction;
            Style(Vector3.Distance(world, transform.position), ref size, out float alpha);
            if (alpha <= 0.001f) return;

            Vector2 edge = ClampToEdge(box, center, dir, size * edgeInset);

            // 여기서부터 GUI 좌표계다 — y가 위가 아니라 아래로 간다.
            Vector2 pivot = new Vector2(edge.x, Screen.height - edge.y);

            // 화면 좌표(y 위)의 각도를 GUI 좌표(y 아래)로 옮기면 부호가 뒤집힌다.
            // 원본 그림이 오른쪽(+X)을 보고 있으므로 0도가 곧 "오른쪽"이다.
            float angle = -Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            Matrix4x4 saved = GUI.matrix;
            Color savedColor = GUI.color;

            GUIUtility.RotateAroundPivot(angle, pivot);
            GUI.color = new Color(tint.r, tint.g, tint.b, tint.a * alpha);
            GUI.DrawTexture(new Rect(pivot.x - size * 0.5f, pivot.y - size * 0.5f, size, size), arrow);

            GUI.color = savedColor;
            GUI.matrix = saved;
        }

        /// <summary>
        /// 화면 중심에서 <paramref name="dir"/>로 쏜 반직선이 가장자리 사각형과 만나는 점.
        ///
        /// 사각형에 그냥 Clamp하지 않는 이유는, Clamp는 x와 y를 따로 자르기 때문에 결과가
        /// 중심–대상을 잇는 선 위에서 벗어난다는 것이다. 모서리 근처에서 화살표가 가리키는
        /// 방향과 화살표가 놓인 자리가 어긋나 보인다. 슬래브 교차는 항상 선 위에 얹는다.
        /// </summary>
        static Vector2 ClampToEdge(Rect box, Vector2 from, Vector2 dir, float inset)
        {
            // 안전 영역이 화살표보다 작을 수는 없다. 작으면 inset을 포기하고 영역만 지킨다.
            float ix = Mathf.Min(inset, box.width * 0.45f);
            float iy = Mathf.Min(inset, box.height * 0.45f);

            float tx = dir.x > 0f ? (box.xMax - ix - from.x) / dir.x
                     : dir.x < 0f ? (box.xMin + ix - from.x) / dir.x
                     : float.MaxValue;

            float ty = dir.y > 0f ? (box.yMax - iy - from.y) / dir.y
                     : dir.y < 0f ? (box.yMin + iy - from.y) / dir.y
                     : float.MaxValue;

            return from + dir * Mathf.Max(0f, Mathf.Min(tx, ty));
        }

        /// <summary>
        /// 거리에 따라 화살표가 어떻게 반응하는가.
        ///
        /// <b>지금은 아무 반응도 하지 않는다</b> — 크기 고정, 완전 불투명. "미니맵이 아니다"를
        /// 가장 좁게 읽은 것이고, 기획자가 고칠 자리로 일부러 비워 둔 것이다.
        /// 여기 한 곳만 고치면 표시의 성격이 바뀐다. TODO(기획): <c>docs/STAGE_SCORE_PLOT.md</c> 참조.
        /// </summary>
        /// <param name="distance">카메라에서 구멍까지의 거리(m)</param>
        /// <param name="size">[입출력] 화살표 한 변(px). 들어올 때 기본 크기가 들어 있다</param>
        /// <param name="alpha">[출력] 0~1. 0이면 그리지 않는다</param>
        void Style(float distance, ref float size, out float alpha)
        {
            alpha = 1f;
        }
    }
}
