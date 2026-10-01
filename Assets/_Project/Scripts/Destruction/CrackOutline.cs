using System.Collections.Generic;
using UnityEngine;

namespace Demo.Destruction
{
    /// <summary>
    /// 금이 간 조각의 <b>테두리에만</b> 선을 남긴다 [지시 2026-09-23].
    ///
    /// 처음에는 모든 조각을 늘 조금씩 벌려서 이음매를 보이게 했는데, 그러면 때리기도 전에
    /// 벽이 이미 조각나 있다. 선은 <b>부순 자리에서만</b> 나와야 한다.
    ///
    /// 선을 조각 판과 같은 메시에 넣지 않는 이유는 z-파이팅이다. 앞면과 선이 같은 깊이에
    /// 있으면 어느 쪽이 이길지 프레임마다 달라져 선이 깜빡인다. 그래서 <b>제 정점</b>을
    /// 따로 들고 카메라 쪽으로 조금 밀어 둔다 — 조각 하나에 모서리 수만큼이라
    /// 앞면(1+모서리)과 옆면(4×모서리)에 비하면 6분의 1 수준이다.
    ///
    /// 선은 <see cref="MeshTopology.Lines"/>라 삼각형이 없다. 굵기는 GPU가 1픽셀로 고정하며,
    /// 이 게임은 한 층을 화면에 꽉 채우므로 그 굵기가 균열선으로 알맞다.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class CrackOutline : MonoBehaviour
    {
        Mesh mesh;
        Vector3[] verts;
        int[] start;                  // 조각마다 정점이 시작되는 자리
        readonly List<int> lines = new List<int>();
        float lift;

        /// <summary>
        /// 조각 판의 자식으로 붙여 만든다. 부모와 같은 자리에 서므로 로컬 좌표가 그대로 맞는다.
        /// </summary>
        /// <param name="lift">앞면보다 얼마나 앞(−Z)에 둘 것인가(m). z-파이팅을 피할 만큼만</param>
        public static CrackOutline Attach(Transform parent, FracturePattern pattern,
                                          Color color, float lift)
        {
            var go = new GameObject("CrackOutline");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();

            var o = go.AddComponent<CrackOutline>();
            o.Build(pattern, color, lift);
            return o;
        }

        void Build(FracturePattern pattern, Color color, float lift)
        {
            this.lift = lift;

            int n = pattern.SeedCount;
            start = new int[n + 1];

            int total = 0;
            for (int s = 0; s < n; s++)
            {
                start[s] = total;
                total += pattern.IsEmpty(s) ? 0 : pattern.Cell(s).Points.Length;
            }
            start[n] = total;

            verts = new Vector3[total];

            mesh = new Mesh { name = "CrackOutline" };
            mesh.indexFormat = total > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.MarkDynamic();
            mesh.vertices = verts;

            GetComponent<MeshFilter>().sharedMesh = mesh;

            var mr = GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sharedMaterial = LineMaterial(color);
        }

        /// <summary>
        /// 조각 판의 정점을 그대로 베껴 오고, 금이 간 조각의 모서리만 선으로 잇는다.
        /// </summary>
        /// <param name="shard">조각 판의 정점 배열</param>
        /// <param name="shardStart">조각 판의 정점 시작 표</param>
        public void Refresh(FracturePattern pattern, Vector3[] shard, int[] shardStart)
        {
            if (mesh == null) return;

            int n = pattern.SeedCount;
            lines.Clear();

            for (int s = 0; s < n; s++)
            {
                if (pattern.IsEmpty(s)) continue;

                int e = pattern.Cell(s).Points.Length;
                int o = start[s];

                // 사라진 조각은 선도 없다. 정점을 한 점으로 접어 두면 선이 안 그려진다.
                //
                // <b>떨어지는 조각에는 선을 안 그린다</b> [지시 8번, 2026-09-30].
                // 이 선은 z-파이팅을 피하려고 카메라 쪽으로 띄워 놨다 — 그래서 <b>깊이 검사를
                // 이기고</b> 앞에 뭐가 있든 그 위에 찍힌다. 공중에 뜬 덩어리에 그리면
                // 화면에 남는 것이 3D 조각이 아니라 <b>검은 선 그림 한 장</b>이 된다.
                // 특히 마지막 층은 뒤에 아무것도 없어서 선만 허공에 떠 있었다.
                //
                // 이 파일 첫머리가 적어 둔 대로, 선은 <b>금이 간 자리</b>를 가리키는 것이다.
                // 떨어져 나온 덩어리는 금이 아니다.
                bool show = !pattern.Gone(s) && pattern.Cracking(s);
                if (!show)
                {
                    for (int i = 0; i < e; i++) verts[o + i] = Vector3.zero;
                    continue;
                }

                // 조각 판의 테두리 정점은 [무게중심 1개] 다음에 모서리 수만큼 이어진다.
                int ring = shardStart[s] + 1;
                for (int i = 0; i < e; i++)
                {
                    Vector3 p = shard[ring + i];
                    p.z -= lift;                      // 앞면보다 살짝 앞
                    verts[o + i] = p;
                }

                for (int i = 0; i < e; i++)
                {
                    lines.Add(o + i);
                    lines.Add(o + (i + 1) % e);
                }
            }

            mesh.vertices = verts;
            mesh.SetIndices(lines, MeshTopology.Lines, 0, false);
            mesh.RecalculateBounds();
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }

        /// <summary>빛을 안 받는 한 색. 균열선은 그늘져서 흐려지면 안 된다.</summary>
        static Material LineMaterial(Color color)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var m = new Material(sh) { name = "CrackLine" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            return m;
        }
    }
}
