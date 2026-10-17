using System.Collections.Generic;
using UnityEngine;

namespace Demo.Utils
{
    /// <summary>
    /// 주먹이 <b>닿는 자리</b>에서 터지는 섬광 [신설 2026-10-16].
    ///
    /// 기획자: "타격 화면에서 타격시 + 연타시 타격감이 없음, 허공에 헛손질하는것으로
    /// 밖에 보이지가 않음."
    ///
    /// <b>문자 그대로 맞는 말이었다.</b> 그동안 만든 것은 전부 <b>팔 쪽</b>이었다 —
    /// 주먹 바람도 잔상도 팔에 붙어 있고, 벽에는 게이지가 찰 때만 금이 조금씩 간다.
    /// <b>주먹이 닿는 지점에는 아무 일도 일어나지 않았다.</b> 소리를 아무리 고쳐도
    /// 메울 수 없는 구멍이 거기 있었다.
    ///
    /// <b>왜 코드로 굽는가.</b> 프리팹과 씬 와이어링은 사람 몫이다 (CLAUDE.md 규칙 1).
    /// <see cref="BlobShadow"/> · <see cref="PunchWind"/>와 같은 수법으로 메시와
    /// 텍스처를 코드가 만든다. 풀링은 규칙 6이다.
    ///
    /// <b>초당 스무 번 터진다.</b> 그래서 하나하나는 아주 짧고(0.12초) 작아야 한다 —
    /// 길면 겹쳐서 화면이 하얗게 타 버린다.
    /// </summary>
    public sealed class HitFlash : MonoBehaviour
    {
        // ── 수치 [전부 제안·미승인 2026-10-16] ───────────────────────────────

        /// <summary>미리 만들어 둘 섬광의 수. 초당 20타 x 수명 0.12초 = 2.4개면 되지만 여유를 둔다.</summary>
        const int Pool = 10;

        /// <summary>한 장이 사는 시간(초). 짧아야 연타에 겹쳐 타지 않는다.</summary>
        const float Life = 0.16f;

        /// <summary>터질 때의 지름(m)과 끝 지름 [키움 2026-10-17].
        /// 0.35 -> 1.00 / 1.15 -> 3.00. 재 보니 6m 거리에서 41x41 픽셀이라
        /// 1920x1080 에서 먼지만 했다. 기획자: "있다면 더 크고 강하게".</summary>
        const float FromSize = 1.00f;
        const float ToSize = 3.00f;

        /// <summary>가장 밝을 때의 알파.</summary>
        const float Alpha = 1.0f;

        struct Flash
        {
            public Transform T;
            public MeshRenderer R;
            public float Left;
            public float Size;
            public float Spin;
        }

        readonly List<Flash> flashes = new List<Flash>(Pool);
        Camera eye;
        MaterialPropertyBlock mpb;

        static Mesh s_quad;
        static Material s_mat;
        static HitFlash s_current;

        /// <summary>
        /// 어디서든 한 줄로 쓴다. 없으면 만든다 — 씬에 올려 둘 것이 없다.
        /// </summary>
        public static void Pop(Vector3 at, float scale = 1f)
        {
            if (s_current == null)
            {
                var go = new GameObject("HitFlash");
                s_current = go.AddComponent<HitFlash>();
            }
            s_current.Fire(at, scale);
        }

        void Awake()
        {
            if (s_current == null) s_current = this;
            eye = Camera.main;
            for (int i = 0; i < Pool; i++) flashes.Add(Make(i));
        }

        void OnDestroy() { if (s_current == this) s_current = null; }

        /// <summary>
        /// 꺼질 때 남은 섬광을 지운다. 안 지우면 LateUpdate 가 멈춘 채 켜져 있던
        /// 것이 그대로 굳는다 — <see cref="PunchWind"/>에서 그 사고를 겪었다.
        /// </summary>
        void OnDisable()
        {
            for (int i = 0; i < flashes.Count; i++)
            {
                Flash f = flashes[i];
                f.Left = 0f;
                if (f.R != null) f.R.enabled = false;
                flashes[i] = f;
            }
        }

        Flash Make(int i)
        {
            var go = new GameObject("Flash" + i);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = Quad();

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Mat();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.enabled = false;
            return new Flash { T = go.transform, R = mr, Left = 0f };
        }

        void Fire(Vector3 at, float scale)
        {
            int idx = -1;
            for (int i = 0; i < flashes.Count; i++) if (flashes[i].Left <= 0f) { idx = i; break; }
            if (idx < 0) return;                    // 전부 쓰는 중이면 이번 것은 건너뛴다

            if (eye == null)
            {
                eye = Camera.main;
                if (eye == null) eye = FindFirstObjectByType<Camera>();
            }

            Flash f = flashes[idx];
            f.Left = Life;
            f.Size = Mathf.Max(0.1f, scale);
            // <b>매번 다른 각도로 돌린다.</b> 같은 모양이 초당 스무 번 겹치면
            // 도장을 찍는 것처럼 보인다. 각도만 바꿔도 터질 때마다 달라 보인다.
            f.Spin = Random.Range(0f, 360f);

            f.T.position = at;
            Face(f.T, f.Spin);
            f.T.localScale = Vector3.one * (FromSize * f.Size);

            if (mpb == null) mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", new Color(1f, 1f, 1f, Alpha));
            f.R.SetPropertyBlock(mpb);
            f.R.enabled = true;

            flashes[idx] = f;
        }

        void Face(Transform t, float spin)
        {
            if (eye == null) return;
            Vector3 toEye = eye.transform.position - t.position;
            if (toEye.sqrMagnitude < 1e-6f) return;
            t.rotation = Quaternion.LookRotation(toEye) * Quaternion.Euler(0f, 0f, spin);
        }

        void LateUpdate()
        {
            // 히트스톱이 걸린 동안에도 흘러야 한다 — 같이 얼면 '끊겼다'로 보인다.
            float dt = Time.unscaledDeltaTime;
            if (mpb == null) mpb = new MaterialPropertyBlock();

            for (int i = 0; i < flashes.Count; i++)
            {
                Flash f = flashes[i];
                if (f.Left <= 0f)
                {
                    if (f.R.enabled) f.R.enabled = false;   // 놓친 프레임이 있어도 반드시 끈다
                    continue;
                }

                f.Left -= dt;
                if (f.Left <= 0f)
                {
                    f.Left = 0f;
                    f.R.enabled = false;
                    flashes[i] = f;
                    continue;
                }

                float age = 1f - f.Left / Life;             // 0 = 막 터짐
                float grow = Mathf.Sqrt(age);               // 처음에 확 퍼지고 뒤는 느리다
                f.T.localScale = Vector3.one * Mathf.Lerp(FromSize, ToSize, grow) * f.Size;
                Face(f.T, f.Spin);

                // 퍼지는 만큼 옅어진다. 커지면서 진하면 화면이 타 버린다.
                mpb.SetColor("_BaseColor", new Color(1f, 1f, 1f, (1f - age) * Alpha));
                f.R.SetPropertyBlock(mpb);

                flashes[i] = f;
            }
        }

        // ── 코드로 굽는 메시와 머티리얼 ──────────────────────────────────────

        static Mesh Quad()
        {
            if (s_quad != null) return s_quad;
            s_quad = new Mesh { name = "HitFlashQuad" };
            s_quad.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f), new Vector3(0.5f,  0.5f, 0f),
            };
            s_quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            s_quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            s_quad.RecalculateBounds();
            return s_quad;
        }

        /// <summary>
        /// 네 갈래로 뻗는 섬광을 구워 둔다 — 가운데가 밝고 십자로 퍼지는 모양이다.
        /// 동그란 원보다 '때렸다'가 읽힌다.
        ///
        /// <b>RGBA32 를 쓴다.</b> Alpha8 은 셰이더에서 RGB 가 무엇으로 읽힐지
        /// 보장이 없다 — BlobShadow 가 그것 때문에 하얗게 나왔다.
        /// </summary>
        static Material Mat()
        {
            if (s_mat != null) return s_mat;

            const int N = 96;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "HitFlash" };
            tex.wrapMode = TextureWrapMode.Clamp;

            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f;
                    float dy = (y + 0.5f) / N * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    float core = Mathf.Clamp01(1f - d * 2.2f);          // 가운데 덩어리
                    core *= core;

                    // 십자 갈래 — 축에 가까울수록 멀리 뻗는다
                    float ang = Mathf.Atan2(dy, dx);
                    float spike = Mathf.Abs(Mathf.Cos(ang * 2f));
                    spike = Mathf.Pow(spike, 8f) * Mathf.Clamp01(1f - d);

                    px[y * N + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(core + spike * 0.8f));
                }
            tex.SetPixels(px);
            tex.Apply(false, true);

            s_mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "HitFlash" };
            s_mat.SetTexture("_BaseMap", tex);
            s_mat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0f));   // 블록이 없으면 안 보인다

            // 표식만으로는 투명해지지 않는다 — 실제 블렌드 상태를 직접 적는다.
            s_mat.SetFloat("_Surface", 1f);
            s_mat.SetFloat("_Blend", 1f);
            s_mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            // <b>덧셈이 아니라 알파 블렌딩</b> [고침 2026-10-17].
            //
            // 섬광이니 덧셈이 자연스럽다고 생각했는데, 덧셈은 <b>밝은 배경에서
            // 안 보인다</b> — 파괴 화면은 대낮의 콘크리트 벽이라 흰색에서 포화된다.
            // PunchWind 에서 똑같이 당하고 고쳐 놓고 여기서 그대로 반복했다.
            s_mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            s_mat.SetFloat("_ZWrite", 0f);
            s_mat.SetFloat("_AlphaClip", 0f);
            s_mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            s_mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            s_mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return s_mat;
        }
    }
}
