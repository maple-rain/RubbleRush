using System.Collections.Generic;
using UnityEngine;

namespace Demo.Collection
{
    /// <summary>
    /// 잔해 한 조각. 풀에서 나오고 풀로 돌아간다.
    ///
    /// 활성 목록을 static으로 들고 있다 — 케이블이 폐합될 때와 구멍이 접촉을 볼 때 후보를 훑어야 하는데,
    /// 그때마다 FindObjectsOfType을 돌리면 CLAUDE.md 규칙 5 위반이고 80개 × 매 프레임이면 비싸다.
    /// 등록/해제는 OnEnable/OnDisable이라 풀링과 자연히 맞물린다.
    /// </summary>
    public sealed class Debris : MonoBehaviour
    {
        [SerializeField, Tooltip("판정 반경 대비 보이는 크기 배율. 한 변 = 반경 × 이 값.\n" +
                                 "2면 정육면체가 판정 구를 완전히 감싸지만 화면을 덮는다.\n" +
                                 "1.0이면 한 변이 반경과 같아 구멍 지름의 절반으로 보인다 — " +
                                 "'들어갈까'는 종류 사이의 크기 차이로 읽히고, 잔해가 시야를 막지 않는다")]
        float visualScale = 1.0f;

        static readonly List<Debris> s_active = new List<Debris>(128);

        /// <summary>지금 맵에 있는 잔해. 읽기 전용으로만 쓴다.</summary>
        public static IReadOnlyList<Debris> Active => s_active;

        /// <summary>잔해 1개의 단위 무게. StructureData.debrisSizeClass에서 온다.</summary>
        public int SizeClass { get; private set; }

        /// <summary>
        /// 물리적 크기(반경 m). 구멍 반경보다 작아야 들어간다.
        /// 무게와는 별개 축이다 — 가벼워도 부피가 크면 못 들어갈 수 있다.
        /// </summary>
        public float SizeRadius { get; private set; }

        /// <summary>견인 중인가. 견인 중인 것은 다시 포획되지 않는다.</summary>
        public bool IsTowed { get; set; }

        /// <summary>구멍으로 빠지는 중인가. 이 상태에서는 어떤 판정에도 걸리지 않는다.</summary>
        public bool IsFalling { get; private set; }

        /// <summary>
        /// 구멍에 빨려드는 중인가.
        ///
        /// 이 상태에서는 <see cref="CargoTow"/>가 자리를 잡아주지 않는다 —
        /// 그물이 붙잡으려는 힘과 구멍이 당기는 힘이 맞서면 잔해가 그 사이에서 떨린다.
        /// 구멍 쪽이 이긴다.
        /// </summary>
        public bool IsBeingSucked { get; set; }

        /// <summary>바닥 평면 좌표. 판정이 2D라 XZ만 쓴다.</summary>
        public Vector2 FlatPosition => new Vector2(transform.position.x, transform.position.z);

        /// <summary>
        /// 주머니 모형(<see cref="NetPocket"/>)이 쓰는 직전 위치 — 베를레의 속도가 여기 들어 있다
        /// [2026-10-08].
        ///
        /// <b>왜 NetPocket이 아니라 조각이 들고 있는가.</b> 구멍이 하나씩 삼키면 견인 목록
        /// 중간이 빠져 뒤가 당겨진다. 속도를 <b>인덱스로</b> 들고 있으면 그 순간 옆 조각의
        /// 속도를 물려받아, 삼키는 동안 — 즉 제일 잘 보이는 순간에 — 짐이 엉뚱하게 튄다.
        /// 조각이 자기 것을 들고 있으면 순서가 어떻게 바뀌든 안 섞인다.
        /// </summary>
        public Vector3 TowPrev { get; set; }

        Renderer rend;
        Vector3 baseScale;
        Vector3 fallFrom, fallTo;
        float fallT, fallDuration;
        System.Action<Debris> onFallDone;

        /// <summary>
        /// 풀에서 꺼낸 직후 DebrisSpawner가 호출한다.
        ///
        /// <b>크기를 여기서 정한다.</b> 예전에는 스포너가 Init 뒤에 스케일을 덮어썼는데,
        /// 그러면 <c>baseScale</c>이 <b>직전에 이 오브젝트가 쓰던 크기</b>로 남는다 —
        /// 풀에서 재사용될 때마다 구멍에 빠지는 순간 엉뚱한 크기로 튀었다.
        /// </summary>
        public void Init(int sizeClass, float sizeRadius)
        {
            SizeClass = sizeClass;
            SizeRadius = sizeRadius;
            IsTowed = false;
            IsFalling = false;
            IsBeingSucked = false;

            transform.localScale = Vector3.one * Mathf.Max(0.01f, sizeRadius * visualScale);
            baseScale = transform.localScale;
            SetVisible(true);
        }

        /// <summary>이 잔해가 그 반경의 구멍에 들어가는가.</summary>
        public bool FitsInHole(float holeRadius) => SizeRadius < holeRadius;

        /// <summary>
        /// 구멍으로 빠지는 연출을 시작한다. 끝나면 <paramref name="onDone"/>이 불린다.
        ///
        /// 물리를 쓰지 않는다 — Rigidbody를 달면 잔해 80개가 전부 시뮬레이션 대상이 되고,
        /// 구멍은 어차피 실제 구멍이 아니라 바닥에 그린 원이라 떨어질 곳도 없다.
        /// </summary>
        public void BeginFall(Vector3 holeCenter, float duration, System.Action<Debris> onDone)
        {
            if (IsFalling) return;

            IsFalling = true;
            IsTowed = false;
            IsBeingSucked = false;
            fallFrom = transform.position;
            fallTo = new Vector3(holeCenter.x, holeCenter.y - 2.5f, holeCenter.z);
            fallDuration = Mathf.Max(0.05f, duration);
            fallT = 0f;
            onFallDone = onDone;
        }

        void Update()
        {
            if (!IsFalling) return;

            fallT += Time.deltaTime / fallDuration;
            float k = Mathf.Clamp01(fallT);

            // 가로로는 빨리 빨려들고 세로로는 가속해서 떨어진다 — 빨려드는 느낌이 여기서 나온다.
            Vector3 p;
            p.x = Mathf.Lerp(fallFrom.x, fallTo.x, 1f - (1f - k) * (1f - k));
            p.z = Mathf.Lerp(fallFrom.z, fallTo.z, 1f - (1f - k) * (1f - k));
            p.y = Mathf.Lerp(fallFrom.y, fallTo.y, k * k);
            transform.position = p;

            transform.localScale = baseScale * Mathf.Lerp(1f, 0.25f, k);
            transform.Rotate(180f * Time.deltaTime, 240f * Time.deltaTime, 0f, Space.Self);

            if (k < 1f) return;

            IsFalling = false;
            transform.localScale = baseScale;
            var cb = onFallDone;
            onFallDone = null;
            if (cb != null) cb(this);
        }

        /// <summary>
        /// 보이기/숨기기. 카메라와 목표 건물 사이를 가로막을 때 잠깐 숨는다
        /// (<see cref="Core.TargetSightline"/>) [지시 2026-09-22].
        /// 렌더러만 끈다 — 오브젝트를 끄면 풀 등록이 풀리고 판정에서도 빠져버린다.
        /// </summary>
        public void SetVisible(bool on)
        {
            if (rend == null) rend = GetComponentInChildren<Renderer>();
            if (rend != null) rend.enabled = on;
        }

        void Awake() => rend = GetComponentInChildren<Renderer>();

        void OnEnable()
        {
            s_active.Add(this);
            SetVisible(true);
        }

        void OnDisable()
        {
            s_active.Remove(this);
            IsTowed = false;
            IsFalling = false;
            IsBeingSucked = false;
            onFallDone = null;
        }

        /// <summary>씬을 다시 지을 때 호출한다. 풀이 통째로 사라지면 목록도 비워야 한다.</summary>
        public static void ClearRegistry() => s_active.Clear();
    }
}
