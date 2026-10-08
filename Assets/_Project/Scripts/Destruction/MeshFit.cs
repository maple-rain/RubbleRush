using UnityEngine;

namespace Demo.Destruction
{
    /// <summary>
    /// 달아 놓은 메시를 <b>정해진 치수에 맞춰</b> 늘리고 바닥을 땅에 붙인다 [고침 2026-10-08].
    ///
    /// <b>왜 필요한가.</b> <see cref="Destructible"/>은 콜라이더를
    /// <see cref="Data.StructureData.colliderSize"/>로 만들면서 <b>보이는 메시는 프리팹 그대로</b>
    /// 달았다. 그래서 둘이 말없이 어긋났다 — 실측(2026-10-08):
    ///
    /// <list type="bullet">
    /// <item><c>t2_light</c>: 설정 3.4 x 3.4 x 3.0 인데 화면에는 <b>1.0 x 3.0 x 5.5</b>.
    ///       이름을 바꾸기 전 <c>wall</c>의 치수가 프리팹에 박혀 있었다.</item>
    /// <item><c>t6_brick</c>: 설정 8.0 x 7.2 x 8.0 인데 화면에는 6.0 x 4.5 x 6.0 (<c>t4</c>의 메시).</item>
    /// <item><c>t7_glass</c>: 설정 4.6 x 5.0 x 4.6 인데 화면에는 2.4 x 1.6 x 1.8 (<c>plank</c>의 메시).</item>
    /// </list>
    ///
    /// 결과는 조용한 거짓말이었다. 기획자가 CSV에서 크기를 고쳐도 <b>화면은 그대로</b>이고,
    /// 때리는 판정(콜라이더)과 보이는 상자가 달라 "분명 맞았는데 안 맞은" 느낌이 난다.
    /// 프리팹을 종류마다 다시 뽑는 대신 <b>들어올 때 한 번 늘린다</b> —
    /// 지금 메시가 전부 민짜 상자라 늘려도 티가 안 나고, 숫자의 출처가 하나로 모인다
    /// (CLAUDE.md 규칙 7).
    ///
    /// <b>Awake에서 한 번만 돈다.</b> <see cref="Destructible.Init"/>이 메시를 달 때만 부른다.
    /// </summary>
    public static class MeshFit
    {
        /// <summary>
        /// <paramref name="root"/> 아래 메시 전체를 <paramref name="size"/>에 맞춘다.
        /// 바닥은 <paramref name="root"/>의 부모 원점(y=0)에 닿고, 가로/깊이는 가운데로 선다 —
        /// 콜라이더가 <c>center.y = size.y / 2</c>로 서는 것과 같은 기준이다.
        /// </summary>
        public static void Fit(GameObject root, Vector3 size)
        {
            if (root == null || size.x <= 0f || size.y <= 0f || size.z <= 0f) return;

            Transform t = root.transform;
            t.localScale = Vector3.one;
            t.localPosition = Vector3.zero;

            if (!LocalBounds(root, out Bounds box)) return;
            if (box.size.x < 1e-4f || box.size.y < 1e-4f || box.size.z < 1e-4f) return;

            var k = new Vector3(size.x / box.size.x, size.y / box.size.y, size.z / box.size.z);
            t.localScale = k;

            // 늘린 뒤의 자리. 바닥(min.y)을 0으로, 가로·깊이의 가운데를 0으로 민다.
            t.localPosition = new Vector3(-box.center.x * k.x,
                                          -(box.center.y - box.extents.y) * k.y,
                                          -box.center.z * k.z);
        }

        /// <summary>
        /// <paramref name="root"/>의 로컬 공간에서 잰 메시 전체의 경계.
        ///
        /// <b><see cref="Renderer.bounds"/>를 쓰지 않는다.</b> 그것은 월드 축에 정렬된 상자라,
        /// 구역 배치에 섞여 있는 yaw 90도짜리에서는 가로와 깊이가 뒤바뀐 값이 나온다.
        /// 메시의 원래 경계를 가져와 모서리 여덟 개를 직접 옮긴다.
        /// </summary>
        static bool LocalBounds(GameObject root, out Bounds box)
        {
            box = new Bounds();
            bool any = false;
            Matrix4x4 toRoot = root.transform.worldToLocalMatrix;

            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;

                Bounds mb = mf.sharedMesh.bounds;
                Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;

                for (int i = 0; i < 8; i++)
                {
                    var sign = new Vector3((i & 1) == 0 ? -1f : 1f,
                                           (i & 2) == 0 ? -1f : 1f,
                                           (i & 4) == 0 ? -1f : 1f);
                    Vector3 p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, sign));
                    if (!any) { box = new Bounds(p, Vector3.zero); any = true; }
                    else box.Encapsulate(p);
                }
            }
            return any;
        }
    }
}
