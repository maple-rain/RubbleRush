using System.Collections.Generic;
using UnityEngine;

namespace Demo.Destruction
{
    /// <summary>
    /// 필드의 상자가 <b>네 면에 서로 다른 그림</b>을 쓰게 한다 [지시 2026-10-12].
    ///
    /// <b>무엇이 틀려 있었나.</b> 건물 메시는 유니티 기본 <c>Cube</c>다 — 꼭짓점 24개,
    /// 서브메시 하나, 머티리얼 하나. 그래서 <see cref="Destructible.ApplySkin"/>이 입히는
    /// 한 장이 <b>네 벽 모두</b>에 붙었고, 그 한 장은 언제나 정면도였다.
    /// 필드에서는 어느 쪽으로 돌아가도 입구가 보였다는 뜻이다.
    ///
    /// 기획자가 세 번 적은 증상이 전부 여기서 나온다 —
    /// "타격 화면에 들어가면 건물 스프라이트가 임의로 돈다",
    /// "정면에서 E를 눌렀는데 측면이 나온다". 돌아간 것이 아니라,
    /// <b>필드가 거짓말을 하고 있었다.</b> 플레이어는 정면인 줄 알고 측면에 가서 눌렀고,
    /// 파괴 화면은 (맞게) 측면을 보여 줬다.
    ///
    /// <b>왜 서브메시가 아니라 아틀라스인가.</b> 면마다 머티리얼을 주면 면 수만큼
    /// 드로우콜이 붙는다 — 건물 39채면 39 -> 117이고, 잔해 0개짜리 씬이 이미 90을 쓴다
    /// (CLAUDE.md 예산 100). 네 장을 한 장에 모아 두고 <b>UV를 메시에 구우면</b>
    /// 머티리얼은 종류당 하나로 남는다. <see cref="RoofCap"/>이 지붕 결을 맞출 때 쓴 손과 같다.
    ///
    /// <b>메시는 종류가 아니라 원본별로 캐시한다.</b> 네 면의 UV 자리는 모든 종류가 같아서
    /// (칸 배치가 고정이다) 기본 <c>Cube</c> 하나를 고쳐 두면 일곱 종이 같이 쓴다.
    /// </summary>
    public static class FaceSkin
    {
        /// <summary>
        /// 칸 가장자리를 안쪽으로 얼마나 물릴 것인가 (아틀라스 UV 단위).
        ///
        /// 밉맵이 섞이는 것을 막는 값이다. 아틀라스는 한 장에 네 칸이라, 축소 단계에서
        /// 한 텍셀이 여러 칸에 걸치면 <b>옆 칸 색이 면 경계에 한 줄로 번진다.</b>
        /// 1024 아틀라스(칸당 512)에서 0.004는 4텍셀이고, 면의 0.8%를 잘라 먹는다 —
        /// 밉 2단계까지 막아 준다.
        /// </summary>
        const float EdgeInset = 0.004f;

        static readonly Dictionary<Mesh, Mesh> s_cache = new Dictionary<Mesh, Mesh>();

        /// <summary>
        /// 아틀라스에서 그 면이 차지하는 칸. <b>굽는 쪽(에디터 도구)과 읽는 쪽이 같은 함수를
        /// 봐야 한다</b> — 둘이 따로 적으면 한쪽만 고쳤을 때 조용히 어긋난다.
        ///
        /// 2x2다. 면 번호(<see cref="StructureFace"/>)의 아래 비트가 좌우, 윗 비트가 위아래다 —
        /// 정면 0은 왼쪽 아래, 우측면 1은 오른쪽 아래, 후면 2는 왼쪽 위, 좌측면 3은 오른쪽 위.
        /// UV와 <see cref="Texture2D.SetPixels(int,int,int,int,Color[])"/>가 둘 다
        /// 왼쪽 아래를 원점으로 세므로 그대로 맞물린다.
        /// </summary>
        public static Rect Cell(int face)
        {
            int f = ((face % StructureFace.Count) + StructureFace.Count) % StructureFace.Count;
            return new Rect((f & 1) * 0.5f, (f >> 1) * 0.5f, 0.5f, 0.5f);
        }

        /// <summary><paramref name="root"/> 아래 메시 전부를 네 면 UV로 바꾼다.</summary>
        public static void Apply(GameObject root)
        {
            if (root == null) return;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh swapped = Atlassed(mf.sharedMesh);
                if (swapped != null) mf.sharedMesh = swapped;
            }
        }

        /// <summary>
        /// 같은 모양에 <b>칸으로 접은 UV</b>를 가진 메시. 같은 원본이면 같은 것을 돌려준다.
        ///
        /// 면은 <b>법선의 가장 큰 축</b>으로 가른다. 기본 Cube처럼 축에 맞은 상자면 정확하고,
        /// 비스듬한 면이 있는 메시여도 가장 가까운 칸으로 떨어져 터지지 않는다.
        /// 윗면·아랫면은 정면 칸을 쓴다 — 윗면은 <see cref="RoofCap"/>이 덮고 아랫면은 땅 속이다.
        /// </summary>
        public static Mesh Atlassed(Mesh src)
        {
            if (src == null) return null;
            if (s_cache.TryGetValue(src, out Mesh found) && found != null) return found;
            if (!src.isReadable) return null;

            Vector3[] n = src.normals;
            Vector2[] uv = src.uv;
            if (n == null || uv == null || n.Length != uv.Length || uv.Length == 0) return null;

            var copy = Object.Instantiate(src);
            copy.name = src.name + " (4면)";
            copy.hideFlags = HideFlags.HideAndDontSave;

            var folded = new Vector2[uv.Length];
            for (int i = 0; i < uv.Length; i++)
            {
                Rect c = Cell(FaceOf(n[i]));
                folded[i] = new Vector2(
                    c.x + EdgeInset + uv[i].x * (c.width - EdgeInset * 2f),
                    c.y + EdgeInset + uv[i].y * (c.height - EdgeInset * 2f));
            }
            copy.uv = folded;

            s_cache[src] = copy;
            return copy;
        }

        static int FaceOf(Vector3 n)
        {
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            if (ay >= ax && ay >= az) return StructureFace.Front;   // 지붕이 덮는다
            if (az >= ax) return n.z >= 0f ? StructureFace.Front : StructureFace.Back;
            return n.x >= 0f ? StructureFace.Right : StructureFace.Left;
        }

        /// <summary>구역을 다시 지을 때 거둔다. <see cref="StageSkin.Clear"/>와 같은 자리에서 부른다.</summary>
        public static void Clear()
        {
            foreach (Mesh m in s_cache.Values)
                if (m != null) Object.Destroy(m);
            s_cache.Clear();
        }
    }
}
