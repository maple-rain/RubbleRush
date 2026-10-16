using System.Collections.Generic;
using UnityEngine;

namespace Demo.Utils
{
    /// <summary>
    /// 주먹을 내지를 때 <b>팔뚝을 따라 잔상이 남는다</b> [지시 2026-10-16].
    ///
    /// 기획자: "팔 끝에 주먹에서부터 팔꿈치까지 이펙트가 있었으면 함. (…)
    /// 잔상이 남을정도로 연타를 했다는 감각을 심어줄까."
    ///
    /// <b>처음 만든 것은 틀렸다.</b> 주먹에서 앞으로 뻗어나가는 부채꼴 획이었는데,
    /// 그것은 '바람이 날아간다'이지 '팔이 지나갔다'가 아니다. 기획자가 원한 것은
    /// 뒤에 남는 자국이다.
    ///
    /// <b>어떻게 잔상이 되는가.</b> 주먹을 내지르는 동안 짧은 간격으로 <b>팔뚝의
    /// 지금 자세를 그대로 찍는다.</b> 찍힌 것은 <b>그 자리에 멈춰</b> 사라진다.
    /// 그래서 한 대를 치면 팔이 지나온 길이 몇 장 남고, <b>연타하면 지워지기 전에
    /// 다음 것이 겹쳐</b> 잔상이 쌓인다. 느리게 치면 한 장씩 떴다 사라지므로
    /// 연타할 때만 번져 보인다 — "피곤해지지 않게" 하는 것이 그 뜻이다.
    ///
    /// <b>뼈는 이름으로 찾지 않는다.</b> 휴머노이드 아바타면
    /// <see cref="Animator.GetBoneTransform"/>이 리그 이름과 무관하게 정확히 집는다.
    /// 이름 검색은 그게 안 될 때의 보험이다.
    ///
    /// 프리팹과 씬 와이어링은 사람 몫이라(CLAUDE.md 규칙 1) 메시와 텍스처를 코드가 굽는다.
    /// 획은 미리 만들어 돌려 쓴다 (규칙 6).
    /// </summary>
    public sealed class PunchWind : MonoBehaviour
    {
        // ── 수치 [제안·미승인 2026-10-16] — 눈으로 보고 고를 값들 ────────────

        /// <summary>미리 만들어 둘 획의 수. 연타가 겹치려면 한 방분보다 넉넉해야 한다.</summary>
        const int Pool = 24;

        /// <summary>한 방에 몇 장을 찍는가. 많으면 팔 궤적이 촘촘해진다.</summary>
        const int Shots = 5;

        /// <summary>찍는 간격(초). Shots x 이 값이 곧 주먹이 나가는 시간이다.</summary>
        const float ShotGap = 0.022f;

        /// <summary>한 장이 사라지는 데 걸리는 시간(초). 길수록 잔상이 오래 남는다.</summary>
        const float Life = 0.28f;

        /// <summary>획의 두께(m). 팔뚝 굵기쯤.</summary>
        const float Thickness = 0.42f;

        /// <summary>가장 진할 때의 알파. <b>0.45에서 올렸다</b> — 두 번 "안 보인다"가
        /// 나왔으니 과하게 두고 줄이는 편이 낫다 [제안·미승인 2026-10-16].</summary>
        const float Alpha = 0.85f;

        /// <summary>주먹 너머로 더 뻗는 비율. 0이면 팔뚝 길이 그대로다.</summary>
        const float Overreach = 0.70f;

        // ── 상태 ─────────────────────────────────────────────────────────────

        struct Stroke
        {
            public Transform T;
            public MeshRenderer R;
            public float Left;
        }

        readonly List<Stroke> strokes = new List<Stroke>(Pool);

        Transform leftHand, leftElbow, rightHand, rightElbow;
        Transform follow;
        Camera eye;
        float scale = 1f;

        float emitLeft, nextShot;
        bool emitRight;

        MaterialPropertyBlock mpb;
        static Mesh s_quad;
        static Material s_mat;

        /// <summary>로봇에 붙인다. 이미 있으면 그것을 돌려준다.</summary>
        public static PunchWind Ensure(Animator anim, float sizeScale = 1f)
        {
            if (anim == null) return null;

            PunchWind w = anim.GetComponentInChildren<PunchWind>();
            if (w == null)
            {
                var go = new GameObject("PunchWind");
                go.transform.SetParent(anim.transform, false);
                w = go.AddComponent<PunchWind>();
            }

            w.scale = Mathf.Max(0.05f, sizeScale);
            w.follow = anim.transform;
            w.FindArms(anim);

            Debug.Log("[PunchWind] 붙었다 — 왼팔 " + Pair(w.leftHand, w.leftElbow)
                    + " · 오른팔 " + Pair(w.rightHand, w.rightElbow)
                    + " · 휴머노이드 " + anim.isHuman, w);
            return w;
        }

        static string Pair(Transform h, Transform e) =>
            (h != null ? h.name : "손없음") + "<-" + (e != null ? e.name : "팔꿈치없음");

        /// <summary>
        /// 손과 팔꿈치를 찾는다. <b>휴머노이드면 뼈 이름을 안 본다</b> —
        /// <see cref="Animator.GetBoneTransform"/>이 리그와 무관하게 정확하다.
        /// 아니면 이름 조각으로 더듬는다 (<see cref="Demo.Player.FootPlant"/>와 같은 수법).
        /// </summary>
        void FindArms(Animator anim)
        {
            if (anim.isHuman)
            {
                leftHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
                leftElbow = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                rightHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
                rightElbow = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
                if (leftHand != null && rightHand != null) return;
            }

            foreach (Transform t in anim.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                bool left = n.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0;
                bool right = n.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (!left && !right) continue;

                if (n.IndexOf("ForeArm", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("LowerArm", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (left && leftElbow == null) leftElbow = t;
                    else if (right && rightElbow == null) rightElbow = t;
                }
                else if (n.IndexOf("Hand", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (left && leftHand == null) leftHand = t;
                    else if (right && rightHand == null) rightHand = t;
                }
            }
        }

        void Awake()
        {
            eye = Camera.main;
            for (int i = 0; i < Pool; i++) strokes.Add(Make(i));
        }

        /// <summary>
        /// 꺼질 때 <b>남은 획을 전부 지운다</b> [고침 2026-10-16].
        ///
        /// 안 지우면 LateUpdate가 멈춘 채 켜져 있던 획이 그대로 굳는다 —
        /// 기획자가 본 "캐릭터의 발 밑에 생긴 무언가"가 그것이다. 갓 만든 획은
        /// 아직 자리를 안 받아 부모 원점, 즉 <b>로봇 발밑</b>에 있다.
        /// </summary>
        void OnDisable()
        {
            for (int i = 0; i < strokes.Count; i++)
            {
                Stroke k = strokes[i];
                k.Left = 0f;
                if (k.R != null) k.R.enabled = false;
                strokes[i] = k;
            }
            emitLeft = 0f;
        }

        Stroke Make(int i)
        {
            var go = new GameObject("Stroke" + i);
            go.transform.SetParent(transform, false);

            go.AddComponent<MeshFilter>().sharedMesh = Quad();

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Mat();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.enabled = false;

            return new Stroke { T = go.transform, R = mr, Left = 0f };
        }

        /// <summary>한 방 나갔다. 여기서부터 <see cref="Shots"/>장을 찍기 시작한다.</summary>
        public void Burst(bool rightArm)
        {
            emitRight = rightArm;
            emitLeft = Shots * ShotGap;
            nextShot = 0f;
            Snap();                       // 첫 장은 지금 당장 — 한 프레임도 늦으면 어색하다
        }

        /// <summary>팔뚝의 지금 자세를 한 장 찍는다. 찍힌 것은 그 자리에 남는다.</summary>
        void Snap()
        {
            Transform hand = emitRight ? rightHand : leftHand;
            Transform elbow = emitRight ? rightElbow : leftElbow;
            if (hand == null) return;

            Vector3 a = hand.position;
            // 팔꿈치를 못 찾으면 팔뚝 길이를 가늠해 대신 쓴다 — 아무것도 안 그리는 것보다 낫다.
            Vector3 b = elbow != null ? elbow.position
                      : a - (follow != null ? follow.forward : Vector3.forward) * 0.35f;

            int idx = Free();
            if (idx < 0) return;          // 전부 쓰는 중이면 이번 장은 거른다

            Stroke k = strokes[idx];
            Place(k.T, a, b);
            k.Left = Life;
            k.R.enabled = true;
            strokes[idx] = k;
        }

        /// <summary>
        /// 손과 팔꿈치 사이에 획을 눕힌다. <b>길이축이 팔뚝을 따라간다</b> —
        /// 이게 "주먹에서 팔꿈치까지"의 뜻이다.
        /// </summary>
        void Place(Transform t, Vector3 hand, Vector3 elbow)
        {
            Vector3 along = hand - elbow;
            float len = along.magnitude;
            if (len < 0.001f) { along = Vector3.forward; len = 0.3f; }

            // <b>주먹 너머로 더 뻗는다</b> [고침 2026-10-16]. 팔뚝(0.46m)만 그리면
            // 7m 거리에서 82px 라 눈에 안 걸린다. 주먹 앞으로 이만큼 더 끌어
            // 휘두른 자취로 읽히게 한다.
            Vector3 dirN = along / len;
            hand += dirN * (len * Overreach);
            along = hand - elbow;
            len = along.magnitude;

            t.position = (hand + elbow) * 0.5f;
            t.localScale = new Vector3(len, Thickness * scale, 1f);

            // 카메라를 보되 <b>길이축은 팔뚝에 맞춘다.</b> 안 그러면 획이 아무 쪽이나
            // 누워서 '팔을 따라간다'가 사라진다.
            Vector3 dir = along / len;
            Vector3 toEye = (eye != null ? eye.transform.position : t.position + Vector3.back) - t.position;
            Vector3 up = Vector3.Cross(dir, toEye);
            t.rotation = up.sqrMagnitude > 1e-6f
                       ? Quaternion.LookRotation(toEye, up)
                       : Quaternion.LookRotation(toEye);
        }

        int Free()
        {
            for (int i = 0; i < strokes.Count; i++) if (strokes[i].Left <= 0f) return i;
            return -1;
        }

        void LateUpdate()
        {
            // <b>실제 시간으로 센다.</b> 타격에는 히트스톱이 걸려 게임 시간이 멈추는데,
            // 잔상까지 같이 얼면 '멈췄다'가 아니라 '끊겼다'로 보인다.
            float dt = Time.unscaledDeltaTime;

            if (eye == null)
            {
                eye = Camera.main;
                if (eye == null) eye = FindFirstObjectByType<Camera>();
            }

            // 내지르는 동안 계속 찍는다 — 이것이 팔의 궤적이 된다.
            if (emitLeft > 0f)
            {
                emitLeft -= dt;
                nextShot -= dt;
                if (nextShot <= 0f) { nextShot = ShotGap; Snap(); }
            }

            if (mpb == null) mpb = new MaterialPropertyBlock();

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

                // <b>움직이지 않는다.</b> 잔상은 팔이 있던 자리에 남는 것이지
                // 따라다니는 것이 아니다. 옅어지기만 한다.
                // <b>제곱이 아니라 루트로 눕힌다</b> [고침 2026-10-16].
                //
                // 제곱이면 수명의 절반에서 이미 알파가 0.21 로 떨어진다 — 또렷한
                // 구간이 0.05초(60fps 에서 세 프레임)뿐이라 82x89 픽셀짜리가
                // 번쩍이고 만다. 기획자가 네 번 "안 보인다"고 한 이유가 이것이었다.
                // 기하도 블렌딩도 자리도 맞았는데 내가 안 보이게 만들어 뒀다.
                //
                // 루트면 절반에서 0.60 이라 끝까지 읽힌다.
                float a = Mathf.Sqrt(k.Left / Life);
                mpb.SetColor("_BaseColor", new Color(1f, 1f, 1f, a * Alpha));
                k.R.SetPropertyBlock(mpb);

                strokes[i] = k;
            }
        }

        // ── 진단 ─────────────────────────────────────────────────────────────

        /// <summary>F7이 부른다. "안 나오는 것"과 "나오는데 안 보이는 것"을 가른다.</summary>
        public string Report()
        {
            int live = 0;
            for (int i = 0; i < strokes.Count; i++) if (strokes[i].Left > 0f) live++;
            return "[PunchWind] 획 " + strokes.Count + "개 중 " + live + "장 떠 있음"
                 + " · 왼팔 " + Pair(leftHand, leftElbow)
                 + " · 오른팔 " + Pair(rightHand, rightElbow)
                 + " · 카메라 " + (eye != null ? eye.name : "없음");
        }

        // ── 코드로 굽는 메시와 머티리얼 ──────────────────────────────────────

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
                    // 주먹 쪽(u=1)이 진하고 팔꿈치 쪽으로 옅어진다 — 끌린 자국으로 읽힌다
                    float along = Mathf.Pow(u, 1.6f);
                    float across = 1f - Mathf.Abs(v * 2f - 1f);
                    px[y * W + x] = new Color(1f, 1f, 1f, along * across * across);
                }
            tex.SetPixels(px);
            tex.Apply(false, true);

            s_mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "PunchWind" };
            s_mat.SetTexture("_BaseMap", tex);
            s_mat.SetColor("_BaseColor", Color.white);

            // _Surface·_Blend 만으로는 아무 일도 안 일어난다 — 그 둘은 머티리얼 에디터가
            // 읽는 표식일 뿐이고, 실제 블렌딩은 아래 셋이 정한다 [고침 2026-10-16].
            s_mat.SetFloat("_Surface", 1f);
            s_mat.SetFloat("_Blend", 1f);
            s_mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            // <b>덧셈이 아니라 알파 블렌딩</b> [고침 2026-10-16].
            //
            // 처음엔 덧셈(One)으로 뒀다. 덧셈은 <b>빛을 더하는</b> 방식이라
            // 어두운 배경에서만 산다 — 파괴 화면은 대낮에 밝은 콘크리트 벽이라
            // 더해 봤자 흰색에서 포화될 뿐 거의 안 보인다.
            // 기획자가 두 번 "이펙트 안 나옴"이라고 한 마지막 이유가 이것이다.
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
