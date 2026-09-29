using UnityEngine;

namespace Demo.Destruction
{
    /// <summary>
    /// 맨 뒤 겹까지 뚫렸을 때 보이는 <b>건물 속</b> [지시 2026-09-23].
    ///
    /// 속을 진짜로 만들지는 않는다. 1인칭으로 정면에서만 보므로 <b>어두운 판 한 장</b>이면
    /// 충분하다. 정점 넷, 드로우콜 하나다.
    ///
    /// 이게 없으면 구멍 너머에 아무것도 없어 <b>하늘과 땅이 그대로 보인다</b> —
    /// 건물이 아니라 세워 둔 판때기로 보이는 결정적인 이유였다.
    ///
    /// 벽의 <b>두께</b>는 여기서 만들지 않는다. 예전에는 조각 판의 메시를 뒤에 한 장 더
    /// 그려 단면 띠를 흉내 냈는데, 그건 같은 메시라 <b>앞이 뚫리면 뒤도 같이 뚫렸다</b> —
    /// 두께가 그려져 있을 뿐 재료가 아니었다. 지금은 <see cref="DemolitionSession"/>이
    /// 자기 진행도를 가진 조각 판을 깊이 방향으로 여러 겹 세운다. 그게 진짜 두께다.
    ///
    /// 조각 판의 부모에 붙는다. 판이 이미 면에 맞춰 서 있으므로 로컬 +Z가 곧 건물 안쪽이다.
    /// </summary>
    public sealed class FractureShell : MonoBehaviour
    {
        /// <param name="width">이 면의 가로(m)</param>
        /// <param name="height">건물 높이(m)</param>
        /// <param name="depth">건물 속 벽까지의 깊이(m)</param>
        public void Build(float width, float height, float depth, Color interiorColor)
        {
            builtHeight = Mathf.Max(0.01f, height);
            AddLayer("Interior", Slab(width, height), depth, interiorColor);
            slab = transform.Find("Interior");
        }

        /// <summary>
        /// 이 판이 따라갈 조각 판 [지시 2026-09-23].
        ///
        /// 속을 막는 판은 건물 높이 전체를 덮는다. 그런데 1층이 무너지면 위층이 내려앉아
        /// 벽은 한 층 낮아지는데 이 판만 그대로 서 있어서, <b>위층이 있던 자리가
        /// 앞에 아무것도 없는 검은 벽</b>으로 남는다 — "다 부쉈는데 안 부서진 부분이 있다",
        /// "위층이 안 내려온다"가 전부 여기서 나왔다. 실제로 위층은 내려와 있었다.
        /// </summary>
        public void Follow(WallFracture source) => follow = source;

        WallFracture follow;
        Transform slab;
        float builtHeight;

        /// <summary>
        /// 메시를 다시 만들지 않고 <b>세로로 눌러서</b> 맞춘다. 판이 y=0에서 위로 자라도록
        /// 만들어져 있어 바닥이 고정된 채 높이만 줄어든다 — 트랜스폼 한 줄이면 끝난다.
        /// </summary>
        void LateUpdate()
        {
            if (follow == null || slab == null) return;
            float k = Mathf.Clamp01(follow.LiveHeight / builtHeight);
            var sc = slab.localScale;
            if (!Mathf.Approximately(sc.y, k)) slab.localScale = new Vector3(sc.x, k, sc.z);
        }

        /// <summary>건물 속을 막는 판. 사각형 하나면 된다 — 1인칭으로 정면에서만 본다.</summary>
        static Mesh Slab(float width, float height)
        {
            float hw = width * 0.5f;
            var m = new Mesh { name = "FractureInterior" };
            m.vertices = new[]
            {
                new Vector3(-hw, 0f, 0f),     new Vector3(hw, 0f, 0f),
                new Vector3(hw, height, 0f),  new Vector3(-hw, height, 0f),
            };
            // 조각 판과 같은 쪽(로컬 −Z)을 본다. 감기와 노멀을 판과 맞춰야 같이 보인다.
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.RecalculateBounds();
            return m;
        }

        void AddLayer(string name, Mesh mesh, float depth, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, depth);   // +Z가 건물 안쪽

            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sharedMaterial = Tint(name, color);
        }

        Material Tint(string name, Color color)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(sh) { name = name + " (instance)" };
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, color);
            if (m.HasProperty(ColorId))     m.SetColor(ColorId, color);
            return m;
        }

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId     = Shader.PropertyToID("_Color");

        /// <summary>
        /// 만든 것을 전부 거둔다. 머티리얼과 <b>직접 만든 메시만</b> 지운다 —
        /// 조각 판의 메시는 빌려 쓴 것이라 여기서 지우면 판이 통째로 사라진다.
        /// </summary>
        void OnDestroy()
        {
            foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && mf.sharedMesh.name == "FractureInterior")
                    Destroy(mf.sharedMesh);

            foreach (var mr in GetComponentsInChildren<MeshRenderer>(true))
                if (mr.sharedMaterial != null && mr.sharedMaterial.name.EndsWith("(instance)"))
                    Destroy(mr.sharedMaterial);
        }
    }
}
