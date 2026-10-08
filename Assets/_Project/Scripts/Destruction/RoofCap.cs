using UnityEngine;

namespace Demo.Destruction
{
    /// <summary>
    /// 건물 꼭대기에 지붕 한 장을 덮는다 [2026-10-08].
    ///
    /// <b>왜 지붕이 제일 급한가.</b> 카메라가 50도로 내려다본다. 한 변 s인 상자에서
    /// 윗면의 화면 투영 면적은 <c>s²·sin50° = 0.766s²</c>이고 벽면은 <c>s²·cos50° = 0.643s²</c>다 —
    /// <b>지붕이 어느 벽보다 크게 보인다.</b> 그 가장 큰 면에 건물 <i>정면도</i>가 붙어 있었으므로
    /// 작은 건물일수록 "바닥에 깔린 그림판"처럼 보였다.
    ///
    /// <b>왜 UV를 메시에 굽는가.</b> 건물이 2.4m에서 12m까지 다섯 배 차이가 난다.
    /// 머티리얼의 타일링으로 결 크기를 맞추면 <b>크기마다 머티리얼이 하나씩 생겨</b>
    /// 드로우콜이 건물 수만큼 붙는다 (잔해 0개짜리 씬이 이미 82~84를 쓴다).
    /// UV를 메시에 구우면 머티리얼은 하나로 공유되고 — SRP 배처는 메시가 달라도 묶는다 —
    /// 결 크기는 건물마다 맞는다. 메시는 꼭짓점 네 개라 공짜다.
    ///
    /// <b>잔해 단계에는 안 덮는다.</b> 무너진 뒤에 멀쩡한 지붕이 남아 있으면 안 된다.
    /// </summary>
    public static class RoofCap
    {
        /// <summary>
        /// 지붕 무늬가 몇 미터마다 반복되는가. 지면과 같은 결로 맞춘 값이다 —
        /// <c>Demo/19 배경 입히기</c>가 카메라에서 끌어낸 8.7m를 따른다.
        /// 밸런스 값이 아니라 보이는 결의 크기라 여기 둔다.
        /// </summary>
        const float GrainMeters = 8.7f;

        /// <summary>지붕이 벽 윗면과 z-파이팅하지 않게 띄우는 높이.</summary>
        const float Lift = 0.02f;

        /// <summary>
        /// <paramref name="parent"/>의 자식으로 지붕 한 장을 만든다.
        /// <paramref name="size"/>는 그 단계의 겉보기 치수(가로·높이·깊이)다.
        /// </summary>
        public static GameObject Make(Transform parent, Vector3 size, Material mat)
        {
            if (parent == null || mat == null || size.x <= 0f || size.z <= 0f) return null;

            var go = new GameObject("Roof");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, size.y + Lift, 0f);

            go.AddComponent<MeshFilter>().sharedMesh = Quad(size);

            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;  // 벽이 이미 드리운다
            r.receiveShadows = false;

            return go;
        }

        /// <summary>
        /// 위를 보는 사각형 하나. UV가 <b>미터로</b> 깔려 있어 건물 크기와 상관없이 결이 같다.
        /// 꼭짓점 순서는 위에서 내려다볼 때 시계 반대 방향이어야 앞면이 하늘을 본다.
        /// </summary>
        static Mesh Quad(Vector3 size)
        {
            float hx = size.x * 0.5f, hz = size.z * 0.5f;
            float ux = size.x / GrainMeters, uz = size.z / GrainMeters;

            var m = new Mesh { name = "Roof" };
            m.vertices = new[]
            {
                new Vector3(-hx, 0f, -hz), new Vector3(-hx, 0f, hz),
                new Vector3(hx, 0f, hz),   new Vector3(hx, 0f, -hz),
            };
            m.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(0f, uz),
                new Vector2(ux, uz), new Vector2(ux, 0f),
            };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            return m;
        }
    }
}
