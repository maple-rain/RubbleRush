using UnityEngine;

namespace Demo.Utils
{
    /// <summary>
    /// 발밑에 깔리는 <b>둥근 그림자 한 장</b> [지시 2026-10-15: "실시간 그림자 끄자"].
    ///
    /// <b>왜 필요한가.</b> 실시간 그림자를 끄면 모바일 예산은 지켜지지만
    /// (CLAUDE.md: "실시간 그림자 사용 안 함 — 블롭 섀도우로 대체")
    /// <b>움직이는 것이 바닥에서 떠 보인다.</b> 서 있는 건물은 바닥과 맞닿아 있어 괜찮지만,
    /// 로봇은 돌아다니므로 발이 땅에 붙어 있다는 단서가 없어진다.
    ///
    /// <b>빛이 아니라 그림이다.</b> 광원도 투영기(Projector)도 쓰지 않는다 — 둘 다
    /// 드로우콜과 셰이더 변형을 늘린다. 바닥에 깔린 <b>사각형 한 장</b>이고,
    /// 그 위에 가운데가 짙은 원을 그려 둔다.
    ///
    /// <b>스스로 붙는다.</b> <see cref="Ensure"/>가 자식으로 만들어 두므로 씬 와이어링이
    /// 필요 없다 (CLAUDE.md 규칙 1) — <see cref="SfxPool.Ensure"/>와 같은 꼴이다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlobShadow : MonoBehaviour
    {
        [SerializeField, Range(0.2f, 6f), Tooltip("그림자 지름(m). 발 너비보다 조금 넓게")]
        float size = 2.2f;

        [SerializeField, Range(0f, 1f), Tooltip("제일 짙은 가운데의 어두움")]
        float strength = 0.45f;

        [SerializeField, Tooltip("바닥에서 띄우는 높이(m). 0이면 지면과 다퉈 깜빡인다")]
        float lift = 0.02f;

        [SerializeField, Tooltip("이 층들만 바닥으로 본다. 비우면 전부")]
        LayerMask ground = ~0;

        [SerializeField, Range(0.5f, 20f), Tooltip("이보다 멀면 바닥이 없다고 보고 숨긴다(m)")]
        float maxDrop = 6f;

        Transform follow;
        MeshRenderer skin;
        static Texture2D s_disc;
        static Material s_mat;

        /// <summary>
        /// 이 대상 밑에 그림자를 깔아 둔다. 이미 있으면 그것을 돌려준다.
        /// </summary>
        public static BlobShadow Ensure(Transform target, float size = 2.2f)
        {
            if (target == null) return null;

            var found = target.GetComponentInChildren<BlobShadow>();
            if (found != null) return found;

            var go = new GameObject("BlobShadow");
            go.transform.SetParent(target, false);

            var b = go.AddComponent<BlobShadow>();
            b.size = size;
            b.follow = target;
            b.Build();
            return b;
        }

        void Awake()
        {
            if (follow == null) follow = transform.parent != null ? transform.parent : transform;
            if (skin == null) Build();
        }

        void Build()
        {
            // 바닥에 눕힌 사각형 하나. Quad는 기본이 수직이라 눕혀 둔다.
            // <b>?? 를 쓰면 안 된다</b> [고침 2026-10-15]. 유니티의 Object는 '가짜 null'이라
            // GetComponent가 없을 때 돌려주는 값이 C#의 null과 다르게 동작한다 —
            // ?? 가 왼쪽을 '있다'로 보고 넘어가서, 붙이지도 않은 MeshFilter를 건드리다
            // MissingComponentException이 났다. 그 예외가 Awake를 끊어
            // <b>그 아래 줄들이 통째로 안 돌았다.</b>
            var mf = gameObject.GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = Quad();

            skin = gameObject.GetComponent<MeshRenderer>();
            if (skin == null) skin = gameObject.AddComponent<MeshRenderer>();
            skin.sharedMaterial = Mat();
            skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            skin.receiveShadows = false;
            skin.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            transform.localScale = new Vector3(size, size, 1f);
        }

        void LateUpdate()
        {
            if (skin == null || follow == null) return;

            // 발밑의 바닥을 찾는다. 경사나 잔해 위에서도 거기에 붙어야 한다.
            Vector3 from = follow.position + Vector3.up * 0.5f;
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, maxDrop + 0.5f, ground,
                                QueryTriggerInteraction.Ignore))
            {
                skin.enabled = true;
                transform.position = hit.point + Vector3.up * lift;
                // 바닥 기울기를 따라 눕는다 — 경사에서 떠 보이지 않게
                // 사각형은 +Z를 보고 있으므로, 그 축을 바닥의 법선에 맞추면 눕는다.
                transform.rotation = Quaternion.LookRotation(hit.normal);

                // 멀어질수록 옅게. 공중에 떴을 때 바닥에 또렷한 원이 남으면 더 어색하다.
                float drop = Mathf.Clamp01(hit.distance / maxDrop);
                SetAlpha(strength * (1f - drop * drop));
            }
            else skin.enabled = false;
        }

        void SetAlpha(float a)
        {
            if (skin == null) return;
            var mpb = new MaterialPropertyBlock();
            skin.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", new Color(0f, 0f, 0f, Mathf.Clamp01(a)));
            skin.SetPropertyBlock(mpb);
        }

        /// <summary>눕힌 사각형. 전부가 같은 것을 쓰므로 한 번만 만든다.</summary>
        static Mesh s_quad;
        static Mesh Quad()
        {
            if (s_quad != null) return s_quad;
            s_quad = new Mesh { name = "BlobQuad" };
            s_quad.vertices = new[] { new Vector3(-0.5f,-0.5f,0f), new Vector3(0.5f,-0.5f,0f),
                                      new Vector3(-0.5f, 0.5f,0f), new Vector3(0.5f, 0.5f,0f) };
            s_quad.uv = new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(0,1), new Vector2(1,1) };
            s_quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            s_quad.RecalculateNormals();
            s_quad.RecalculateBounds();
            return s_quad;
        }

        /// <summary>
        /// 가운데가 짙고 가장자리로 갈수록 사라지는 원. <b>그림 파일을 두지 않는다</b> —
        /// 64x64 한 장이라 코드로 굽는 것이 임포트 설정을 맞추는 것보다 싸다.
        /// </summary>
        static Material Mat()
        {
            if (s_mat != null) return s_mat;

            const int N = 64;
            s_disc = new Texture2D(N, N, TextureFormat.Alpha8, false) { name = "BlobDisc" };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f;
                    float dy = (y + 0.5f) / N * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    // 제곱으로 떨어뜨린다. 직선으로 하면 테두리가 선처럼 보인다
                    float a = Mathf.Clamp01(1f - d);
                    px[y * N + x] = new Color(0f, 0f, 0f, a * a);
                }
            s_disc.SetPixels(px);
            s_disc.wrapMode = TextureWrapMode.Clamp;
            s_disc.Apply();

            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            s_mat = new Material(sh) { name = "BlobShadow" };
            s_mat.SetTexture("_BaseMap", s_disc);
            s_mat.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0.45f));
            s_mat.SetFloat("_Surface", 1f);                        // 투명
            s_mat.SetFloat("_Blend", 0f);                          // 알파 블렌드
            s_mat.SetFloat("_ZWrite", 0f);
            s_mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            s_mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return s_mat;
        }
    }
}
