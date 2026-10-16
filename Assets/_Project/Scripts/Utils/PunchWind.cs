using System.Collections.Generic;
using UnityEngine;

namespace Demo.Utils
{
    /// <summary>
    /// 주먹이 지나간 자리에 <b>바람이 찢어진 자국</b>을 남긴다 [지시 2026-10-16].
    ///
    /// 기획자: "왜 심심한가 했더니 타격 이펙트를 안넣었구나, 타격할 때 팔 주변에
    /// 바람이 나간다던가 같은거 말이야. 보르노이만 적용했었네."
    ///
    /// 맞는 지적이다 — 재 보니 이 프로젝트에 <b>파티클이 한 개도 없다.</b>
    /// 파쇄 무늬(보로노이)와 화면 흔들림·히트스톱이 전부였다. 그래서 주먹이
    /// 나가는 순간에 <b>눈에 걸리는 것이 아무것도 없었다.</b>
    ///
    /// <b>왜 코드로 짓는가.</b> 프리팹과 씬 와이어링은 사람 몫이다 (CLAUDE.md 규칙 1).
    /// <see cref="BlobShadow"/>와 같은 수법으로 메시와 텍스처를 코드가 만든다 —
    /// 에디터에서 끌어다 놓을 것이 없으니 들고 다닐 수 있다.
    ///
    /// <b>풀링.</b> 획은 미리 만들어 두고 돌려 쓴다 (규칙 6). 연타하면 초당 다섯 번
    /// 나가는데 그때마다 Instantiate 하면 GC가 튄다.
    ///
    /// <b>이 효과가 하지 않는 것</b>: 맞은 쪽의 반응. 부딪힌 자리에서 튀는 파편이나
    /// 먼지는 따로다 — 여기는 <b>주먹 쪽</b>만 맡는다.
    /// </summary>
    public sealed class PunchWind : MonoBehaviour
    {
        // ── 수치 [제안·미승인 2026-10-16] ──────────────────────────────────
        // 전부 눈으로 보고 고를 것들이다. 기획자가 보고 숫자를 주면 그때 확정한다.

        /// <summary>한 번에 몇 획인가. 홀수면 가운데 한 획이 똑바로 나간다.</summary>
        const int Strokes = 3;

        /// <summary>미리 만들어 둘 획의 수. 연타 간격(0.2초)보다 수명이 짧으면 안 모자란다.</summary>
        const int Pool = 12;

        /// <summary>한 획이 사는 시간(초). 짧아야 '스친다'로 읽힌다.</summary>
        const float Life = 0.17f;

        /// <summary>부채꼴로 벌리는 각도(도). 0이면 전부 겹쳐 한 획으로 보인다.</summary>
        const float Spread = 22f;

        /// <summary>뻗는 거리(m). 주먹 앞으로 이만큼 늘어난다.</summary>
        const float Reach = 0.9f;

        // ── 상태 ─────────────────────────────────────────────────────────────

        struct Stroke
        {
            public Transform T;
            public MeshRenderer R;
            public float Left;          // 남은 수명
            public Vector3 From, Dir;
            public float Size;
        }

        readonly List<Stroke> strokes = new List<Stroke>(Pool);
        Transform leftHand, rightHand;
        Transform follow;               // 로봇. 손을 못 찾으면 여기서 쏜다
        Camera eye;
        float scale = 1f;

        MaterialPropertyBlock mpb;

        static Mesh s_quad;
        static Material s_mat;

        /// <summary>
        /// 로봇에 붙인다. 이미 있으면 그것을 돌려준다 — <see cref="BlobShadow.Ensure"/>와 같다.
        /// </summary>
        /// <param name="robot">애니메이터가 달린 로봇</param>
        /// <param name="sizeScale">획 크기 배율. 1이 기본</param>
        public static PunchWind Ensure(Transform robot, float sizeScale = 1f)
        {
            if (robot == null) return null;

            PunchWind w = robot.GetComponentInChildren<PunchWind>();
            if (w == null)
            {
                var go = new GameObject("PunchWind");
                go.transform.SetParent(robot, false);
                w = go.AddComponent<PunchWind>();
            }

            w.scale = Mathf.Max(0.05f, sizeScale);
            w.follow = robot;
            w.FindHands(robot);

            // <b>한 번은 말하고 지나간다</b> [2026-10-16]. 손 뼈를 이름으로 찾는데
            // 리그마다 이름이 달라서 못 찾을 수 있고, 그러면 바람이 가슴께에서 나와
            // "안 나온다"로 보인다. 조용히 틀리느니 로그 한 줄이 싸다.
            Debug.Log("[PunchWind] 붙었다 — 왼손 " + (w.leftHand != null ? w.leftHand.name : "못 찾음")
                    + " · 오른손 " + (w.rightHand != null ? w.rightHand.name : "못 찾음")
                    + " · 크기 x" + w.scale.ToString("0.##"), w);
            return w;
        }

        /// <summary>
        /// 지금 상태를 한 줄로. F7로 부른다 — "안 보인다"가 <b>안 나오는 것</b>인지
        /// <b>나오는데 안 보이는 것</b>인지 가른다.
        /// </summary>
        public string Report()
        {
            int live = 0;
            for (int i = 0; i < strokes.Count; i++) if (strokes[i].Left > 0f) live++;
            return "[PunchWind] 획 " + strokes.Count + "개 중 살아 있음 " + live
                 + " · 손 " + (leftHand != null ? "O" : "X") + (rightHand != null ? "O" : "X")
                 + " · 카메라 " + (eye != null ? eye.name : "없음")
                 + " · 머티리얼 " + (s_mat != null ? s_mat.shader.name : "없음");
        }

        /// <summary>
        /// 손 뼈를 이름 조각으로 찾는다 — <see cref="Demo.Player.FootPlant"/>가 발을 찾는 법과 같다.
        /// 리그마다 이름이 달라서 정확한 경로를 박아 둘 수 없다.
        /// 못 찾아도 죽지 않는다. 그때는 로봇 가슴께에서 쏜다.
        /// </summary>
        void FindHands(Transform robot)
        {
            foreach (Transform t in robot.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n.IndexOf("Hand", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (n.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0) leftHand = t;
                else if (n.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0) rightHand = t;
            }
        }

        void Awake()
        {
            eye = Camera.main;
            for (int i = 0; i < Pool; i++) strokes.Add(Make(i));
        }

        Stroke Make(int i)
        {
            var go = new GameObject("Stroke" + i);
            go.transform.SetParent(transform, false);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = Quad();

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Mat();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.enabled = false;

            return new Stroke { T = go.transform, R = mr, Left = 0f };
        }

        /// <summary>
        /// 한 방 나갔다. <paramref name="rightArm"/>이 어느 손에서 나가는지를 정한다.
        /// </summary>
        public void Burst(bool rightArm)
        {
            Transform hand = rightArm ? rightHand : leftHand;
            Vector3 at = hand != null ? hand.position
                       : (follow != null ? follow.position + Vector3.up * 1.2f : transform.position);

            // 나가는 쪽 — 로봇이 보는 방향이다. 파괴 화면에서는 벽을 보고 서 있다.
            Vector3 fwd = follow != null ? follow.forward : transform.forward;

            for (int s = 0; s < Strokes; s++)
            {
                int idx = Free();
                if (idx < 0) return;                 // 전부 쓰는 중 — 이번 것은 건너뛴다

                // 부채꼴로 벌린다. 홀수면 가운데가 0도다.
                float t = Strokes == 1 ? 0f : (s / (Strokes - 1f)) * 2f - 1f;
                Vector3 dir = Quaternion.AngleAxis(t * Spread * 0.5f, Vector3.up) * fwd;

                Stroke k = strokes[idx];
                k.Left = Life;
                k.From = at + dir * 0.08f;
                k.Dir = dir.normalized;
                k.Size = scale * Random.Range(0.85f, 1.15f);   // 똑같으면 도장 찍은 것처럼 보인다
                k.R.enabled = true;
                strokes[idx] = k;
            }
        }

        int Free()
        {
            for (int i = 0; i < strokes.Count; i++) if (strokes[i].Left <= 0f) return i;
            return -1;
        }

        void LateUpdate()
        {
            // <b>실제 시간으로 센다.</b> 타격에는 히트스톱이 걸려 게임 시간이 멈추는데,
            // 바람까지 같이 얼면 '멈췄다'가 아니라 '끊겼다'로 보인다.
            float dt = Time.unscaledDeltaTime;

            // <b>카메라를 못 찾으면 획이 옆모습이 되어 사라진다</b> [고침 2026-10-16].
            //
            // 빌보드 회전을 eye 가 있을 때만 걸었더니, Camera.main 이 null 인 동안
            // 쿼드가 처음 자세(월드 +Z) 그대로 서 있었다 — 쿼터뷰에서는 거의 옆모습이라
            // 두께 0으로 보인다. MainCamera 태그가 없는 씬도 있으므로 아무 카메라나 잡는다.
            if (eye == null)
            {
                eye = Camera.main;
                if (eye == null) eye = FindFirstObjectByType<Camera>();
            }

            for (int i = 0; i < strokes.Count; i++)
            {
                Stroke k = strokes[i];
                if (k.Left <= 0f) continue;

                k.Left -= dt;
                if (k.Left <= 0f)
                {
                    k.Left = 0f;
                    k.R.enabled = false;
                    strokes[i] = k;
                    continue;
                }

                float age = 1f - k.Left / Life;          // 0 = 막 나감, 1 = 사라짐

                // 처음엔 빠르게 뻗고 끝에서 느려진다 — 공기가 밀려났다가 멎는 그림이다.
                float out01 = 1f - (1f - age) * (1f - age);
                k.T.position = k.From + k.Dir * (Reach * k.Size * out01);

                // 길게 늘이면서 얇아진다. 늘어난 만큼 옅어져야 '지나갔다'로 읽힌다.
                float len = k.Size * Mathf.Lerp(0.35f, 1.1f, out01);
                float thin = k.Size * Mathf.Lerp(0.22f, 0.05f, age);
                k.T.localScale = new Vector3(len, thin, 1f);

                // 카메라를 보되 <b>길이축은 진행 방향에 맞춘다</b> — 안 그러면 획이
                // 아무 쪽이나 누워서 속도감이 사라진다.
                if (eye != null)
                {
                    Vector3 toEye = eye.transform.position - k.T.position;
                    Vector3 up = Vector3.Cross(k.Dir, toEye).normalized;
                    if (up.sqrMagnitude > 0.0001f)
                        k.T.rotation = Quaternion.LookRotation(toEye, up);
                }

                // <b>MaterialPropertyBlock 은 돌려 쓴다.</b> 매 프레임 새로 만들면
                // 획 12개 x 60프레임이라 GC가 튄다 (CLAUDE.md 규칙 6).
                float a = Mathf.Sin(age * Mathf.PI);      // 떠올랐다 사라진다
                if (mpb == null) mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", new Color(1f, 1f, 1f, a * 0.5f));
                k.R.SetPropertyBlock(mpb);

                strokes[i] = k;
            }
        }

        // ── 코드로 만드는 메시와 머티리얼 ────────────────────────────────────

        static Mesh Quad()
        {
            if (s_quad != null) return s_quad;

            s_quad = new Mesh { name = "WindStroke" };
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
        /// 가운데가 밝고 양 끝이 사라지는 획 하나를 구워 둔다.
        /// 텍스처를 에셋으로 두지 않는 이유는 <see cref="BlobShadow"/>와 같다 — 사람이
        /// 임포트 설정을 맞춰 줄 필요가 없어야 한다.
        /// </summary>
        static Material Mat()
        {
            if (s_mat != null) return s_mat;

            const int W = 64, H = 16;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "WindStroke" };
            tex.wrapMode = TextureWrapMode.Clamp;

            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = x / (W - 1f), v = y / (H - 1f);
                    // 길이축: 앞이 뾰족하고 뒤로 길게 끌린다
                    float along = Mathf.Sin(Mathf.Pow(u, 0.65f) * Mathf.PI);
                    // 두께축: 가운데가 밝다
                    float across = 1f - Mathf.Abs(v * 2f - 1f);
                    float a = along * across * across;
                    px[y * W + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            tex.Apply(false, true);

            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            s_mat = new Material(sh) { name = "PunchWind" };
            s_mat.SetTexture("_BaseMap", tex);
            s_mat.SetColor("_BaseColor", Color.white);
            // <b>_Surface·_Blend 만으로는 아무 일도 안 일어난다</b> [고침 2026-10-16].
            //
            // 그 둘은 URP <b>머티리얼 에디터가 읽는 표식</b>일 뿐이다. 실제 블렌딩은
            // _SrcBlend/_DstBlend/_ZWrite 가 정하는데, 코드로 만들면 에디터가 안 도므로
            // 표식만 세워 두면 머티리얼이 <b>불투명인 채로 남는다</b> — 알파가 통째로
            // 무시되어 흰 사각형이 뜨거나, 큐와 엇갈려 아예 안 보인다.
            // 기획자: "주먹을 내지를 때 VFX도 추가된 것 같지가 않고."
            //
            // 그래서 렌더 상태를 직접 적는다. 덧셈 블렌딩이다 (SrcAlpha + One).
            s_mat.SetFloat("_Surface", 1f);            // 표식 — 인스펙터에서 보기 위한 것
            s_mat.SetFloat("_Blend", 1f);
            s_mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            s_mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            s_mat.SetFloat("_AlphaClip", 0f);

            // <b>양면으로 그린다.</b> 빌보드가 한 프레임 늦거나 카메라를 못 찾으면
            // 뒷면이 보이는데, 컬링이 켜져 있으면 그때 통째로 사라진다.
            s_mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            s_mat.SetFloat("_ZWrite", 0f);
            s_mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            s_mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return s_mat;
        }
    }
}
