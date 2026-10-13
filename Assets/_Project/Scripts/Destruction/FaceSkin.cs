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

        /// <summary>
        /// 토대(잔해 단계) 아틀라스의 칸. <b>두 쪽짜리다</b> [지시 2026-10-12] —
        /// 왼쪽은 네 <b>옆면</b>이 쓰는 무너진 벽 입면, 오른쪽은 <b>윗면</b>이 쓰는 자갈이다.
        ///
        /// <b>왜 윗면을 따로 두는가.</b> 잔해 그림은 건물의 <b>정면도</b>라 위쪽 28~45%가
        /// 밋밋한 하늘이다. 민짜 Cube에 그대로 깔면 그 하늘이 <b>윗면에 통째로</b> 얹히는데,
        /// 카메라가 50도로 내려다보므로 윗면이 어느 벽보다 크게 보인다 —
        /// 기획자가 "회색 육면체"라고 지적한 것이 그 하늘이다.
        /// 입면은 벽에 서야 하고, 윗면에는 부서진 콘크리트가 와야 한다.
        /// </summary>
        public static Rect RubbleCell(int face, bool top)
        {
            if (top) return new Rect(0f, 0.5f, 0.5f, 0.5f);                 // 윗면 — 부서진 콘크리트
            if (face == StructureFace.Front) return new Rect(0f, 0f, 0.5f, 0.5f);   // 문 있는 쪽
            return new Rect(0.5f, 0f, 0.5f, 0.5f);                          // 문 없는 쪽
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

            // <b>UV를 접지 않고 다시 그린다</b> [고침 2026-10-12].
            //
            // 처음에는 원래 UV를 칸으로 옮기기만 했다. 그런데 유니티 기본 Cube는
            // <b>-Z 면만 UV가 180도 돌아 있다</b> — 실측으로 u+가 (-1,0,0)이 아니라 (1,0,0)이고
            // v+는 아래를 가리킨다. 그대로 옮기면 필드의 후면 벽에 그림이 거꾸로·좌우반전으로
            // 붙고, 조각 판은 똑바로 붙는다. <b>같은 그림인데 다른 그림으로 보인다</b> —
            // 후면은 39채 전부에서 늘 보이는 면이라 이것이 계속 걸렸다 [지적 2026-10-12].
            //
            // 그래서 꼭짓점 자리에서 직접 끌어낸다. 규약은 <b>조각 판과 같은 것</b>을 쓴다
            // (WallFracture.Uv) — 둘이 같은 식을 봐야 다시는 어긋나지 않는다.
            Vector3[] pos = src.vertices;
            Bounds box = src.bounds;

            var folded = new Vector2[uv.Length];
            for (int i = 0; i < uv.Length; i++)
            {
                int f = FaceOf(n[i]);
                Vector3 p = pos[i] - box.center;
                Vector2 flat = Flat(p, box.extents, n[i], f);
                Rect c = Cell(f);
                folded[i] = new Vector2(
                    c.x + EdgeInset + flat.x * (c.width - EdgeInset * 2f),
                    c.y + EdgeInset + flat.y * (c.height - EdgeInset * 2f));
            }
            copy.uv = folded;

            s_cache[src] = copy;
            return copy;
        }

        /// <summary>
        /// 그 면 위에서의 자리 0~1. <b>조각 판과 같은 규약</b>이다 —
        /// 바깥에서 봤을 때 u는 오른쪽, v는 위로 커진다.
        /// 윗면·아랫면은 세로가 없으므로 x·z로 깐다 (지붕이 덮어 안 보인다).
        /// </summary>
        static Vector2 Flat(Vector3 p, Vector3 extents, Vector3 normal, int face)
        {
            if (Mathf.Abs(normal.y) > Mathf.Abs(normal.x) && Mathf.Abs(normal.y) > Mathf.Abs(normal.z))
                return new Vector2(Half(p.x, extents.x), Half(p.z, extents.z));

            Vector3 right = RightOf(face);
            float halfWide = Mathf.Abs(Vector3.Dot(extents, right));
            float u = halfWide > 1e-4f ? Vector3.Dot(p, right) / (halfWide * 2f) + 0.5f : 0.5f;
            return new Vector2(u, Half(p.y, extents.y));
        }

        static float Half(float a, float e) => e > 1e-4f ? a / (e * 2f) + 0.5f : 0.5f;

        /// <summary>
        /// 그 면을 바깥에서 봤을 때 <b>오른쪽</b>이 건물 로컬에서 어느 쪽인가.
        /// <c>FractureProxy</c>가 판을 세울 때 쓰는 식과 글자 그대로 같다.
        /// </summary>
        static Vector3 RightOf(int face)
        {
            Vector3 outward;
            switch (((face % StructureFace.Count) + StructureFace.Count) % StructureFace.Count)
            {
                case StructureFace.Front: outward = Vector3.forward; break;
                case StructureFace.Right: outward = Vector3.right;   break;
                case StructureFace.Back:  outward = Vector3.back;    break;
                default:                  outward = Vector3.left;    break;
            }
            return Quaternion.LookRotation(-outward, Vector3.up) * Vector3.right;
        }

        /// <summary>토대용. 옆면은 입면, 윗면·아랫면은 자갈 쪽 칸을 본다.</summary>
        public static void ApplyRubble(GameObject root)
        {
            if (root == null) return;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh swapped = RubbleMapped(mf.sharedMesh);
                if (swapped != null) mf.sharedMesh = swapped;
            }
        }

        static readonly Dictionary<Mesh, Mesh> s_rubble = new Dictionary<Mesh, Mesh>();

        static Mesh RubbleMapped(Mesh src)
        {
            if (src == null) return null;
            if (s_rubble.TryGetValue(src, out Mesh found) && found != null) return found;
            if (!src.isReadable) return null;

            Vector3[] n = src.normals;
            Vector3[] pos = src.vertices;
            if (n == null || pos == null || n.Length != pos.Length || n.Length == 0) return null;

            var copy = Object.Instantiate(src);
            copy.name = src.name + " (토대)";
            copy.hideFlags = HideFlags.HideAndDontSave;

            Bounds box = src.bounds;
            var uv = new Vector2[pos.Length];
            for (int i = 0; i < pos.Length; i++)
            {
                bool top = Mathf.Abs(n[i].y) > Mathf.Abs(n[i].x) && Mathf.Abs(n[i].y) > Mathf.Abs(n[i].z);
                Vector3 p = pos[i] - box.center;
                Vector2 flat = Flat(p, box.extents, n[i], FaceOf(n[i]));
                Rect c = RubbleCell(FaceOf(n[i]), top);
                uv[i] = new Vector2(
                    c.x + EdgeInset + flat.x * (c.width - EdgeInset * 2f),
                    c.y + EdgeInset + flat.y * (c.height - EdgeInset * 2f));
            }
            copy.uv = uv;

            s_rubble[src] = copy;
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
            foreach (Mesh m in s_rubble.Values)
                if (m != null) Object.Destroy(m);
            s_rubble.Clear();
        }
    }
}
