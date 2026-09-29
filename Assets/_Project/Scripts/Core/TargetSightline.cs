using Demo.Collection;
using Demo.Destruction;
using UnityEngine;

namespace Demo.Core
{
    /// <summary>
    /// 부수려는 건물이 잔해에 가려지지 않게 한다 [지시 2026-09-22].
    ///
    /// 3인칭 쿼터뷰에서는 카메라가 낮고 비스듬해서, 방금 부순 잔해가 다음 목표 앞에 그대로 쌓인다.
    /// 상호작용 키가 떴는데 <b>무엇에 뜬 건지 안 보이는</b> 상황이 생긴다.
    ///
    /// 카메라에서 목표까지 선분을 긋고 그 위에 걸친 잔해만 잠깐 숨긴다.
    /// 판정에서 빼지는 않는다 — 렌더러만 끄므로 케이블도 구멍도 그 잔해를 그대로 본다.
    /// 숨은 것이 아니라 <b>안 보일 뿐</b>이다.
    ///
    /// 물리 레이캐스트를 쓰지 않는 이유는 잔해에 콜라이더가 없기 때문이다(비물리 설계).
    /// 점–선분 거리는 잔해 80개에 내적 몇 번이라 매 프레임 돌려도 싸다.
    /// </summary>
    public sealed class TargetSightline : MonoBehaviour
    {
        [SerializeField, Tooltip("상호작용 대상을 알려주는 탐지기. 비우면 씬에서 찾는다")]
        Player.InteractionProbe probe;

        [SerializeField, Tooltip("시선에서 이만큼 안쪽에 걸치면 가린 것으로 본다(m). " +
                                 "잔해 자체 반경에 더해진다")]
        float clearance = 0.6f;

        [SerializeField, Tooltip("목표에 이보다 가까운 잔해는 숨기지 않는다(m). " +
                                 "건물 발치의 잔해까지 지우면 방금 부순 결과가 안 보인다")]
        float keepNearTarget = 2.5f;

        bool hidAnything;

        void Awake()
        {
            if (probe == null) probe = FindFirstObjectByType<Player.InteractionProbe>();
        }

        void LateUpdate()
        {
            Destructible target = probe != null ? probe.Candidate : null;

            if (target == null)
            {
                // 가린 것이 있었을 때만 훑는다. 평소에는 이 줄에서 끝난다.
                if (hidAnything) ShowAll();
                return;
            }

            Vector3 eye = transform.position;
            Vector3 aim = target.transform.position;
            if (target.Data != null) aim.y += target.Data.colliderSize.y * 0.5f;

            Vector3 seg = aim - eye;
            float segLenSqr = seg.sqrMagnitude;
            if (segLenSqr < 0.01f) { if (hidAnything) ShowAll(); return; }

            float keepSqr = keepNearTarget * keepNearTarget;
            hidAnything = false;

            var active = Debris.Active;
            for (int i = 0; i < active.Count; i++)
            {
                Debris d = active[i];
                if (d == null) continue;

                Vector3 p = d.transform.position;

                // 목표 발치의 잔해는 남긴다 — 부순 결과가 거기 있어야 한다
                if ((p - aim).sqrMagnitude < keepSqr) { d.SetVisible(true); continue; }

                // 선분 위 최근접점. t를 0~1로 자르면 카메라 뒤나 건물 너머는 자연히 빠진다.
                float t = Vector3.Dot(p - eye, seg) / segLenSqr;
                if (t <= 0f || t >= 1f) { d.SetVisible(true); continue; }

                float sqr = (p - (eye + seg * t)).sqrMagnitude;
                float need = d.SizeRadius + clearance;

                bool blocking = sqr < need * need;
                d.SetVisible(!blocking);
                if (blocking) hidAnything = true;
            }
        }

        void ShowAll()
        {
            var active = Debris.Active;
            for (int i = 0; i < active.Count; i++)
                if (active[i] != null) active[i].SetVisible(true);
            hidAnything = false;
        }
    }
}
