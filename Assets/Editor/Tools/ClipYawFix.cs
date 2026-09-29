using System.Text;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 옆을 보고 만들어진 클립을 <b>정면으로 돌려 놓는다</b> (CLAUDE.md 규칙 8).
    ///
    /// <b>왜 필요한가.</b> VARCO의 달리기 계열(run·jog·sprint)은 셋 다 +X를 보고 만들어져 있다.
    /// 실측: 몸 yaw 83~96도, 디딘 발의 진행 방향 86~90도. 걷기와 대기는 0도다.
    /// 그대로 쓰면 로봇이 <b>옆으로 달린다</b> [실측 2026-09-28].
    ///
    /// <b>임포터로는 안 된다.</b> 먼저 <c>motionNodeName</c> + <c>lockRootRotation</c> +
    /// <c>rotationOffset = -90</c>을 시도했는데 yaw가 96도 그대로였다. 리그가 Generic이고
    /// 아바타가 CopyFromOther라, 루트 모션 오프셋은 <b>뼈 커브를 건드리지 않는다</b>.
    /// 커브를 직접 고치는 수밖에 없다.
    ///
    /// <b>보정이 왜 한 줄인가.</b> 커브를 가진 가장 얕은 노드가 <c>Armature</c>이고,
    /// 그 부모(모델 루트)의 월드 회전이 <b>항등</b>이며 자기 위치가 원점이다.
    /// 그래서 월드에서 돌리고 싶은 각도를 로컬 회전에 <b>그대로 곱하면</b> 된다 —
    /// 부모가 기울어 있었다면 P⁻¹·Y·P 꼴의 켤레 변환이 필요했을 자리다.
    ///
    /// FBX 안의 클립은 고칠 수 없으므로 <c>Clips/</c> 아래에 <c>.anim</c>으로 새로 굽는다.
    /// <b>자르기를 먼저 하고 이걸 돌려야 한다</b> — ClipTrimmer는 FBX 임포터를 고치므로
    /// 순서가 뒤집히면 교정본이 안 잘린 클립에서 나온다.
    ///
    /// <b>이 도구가 검증하지 않는 것:</b> 돌려 놓은 뒤의 동작이 <b>보기 좋은지</b>.
    /// 각도와 진행 방향만 잰다.
    /// </summary>
    public static class ClipYawFix
    {
        const string Dir = "Assets/_Project/Art/Models/Robot";
        const string OutDir = Dir + "/Clips";
        const string TopNode = "Armature";

        static readonly string[] Targets = { "Run", "Jog", "Sprint" };

        public static string LastReport { get; private set; }

        [MenuItem("Demo/9. 클립 방향 교정 (옆을 보는 클립 돌리기)", priority = 52)]
        public static void Fix()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/R_Demolition.fbx");
            if (model == null) { Debug.LogError("[ClipYawFix] 기본 모델 없음"); return; }
            if (!AssetDatabase.IsValidFolder(OutDir))
                AssetDatabase.CreateFolder(Dir, "Clips");

            GameObject rig = Object.Instantiate(model);
            rig.hideFlags = HideFlags.HideAndDontSave;

            var sb = new StringBuilder("[ClipYawFix]\n");
            try
            {
                foreach (string name in Targets)
                {
                    AnimationClip src = Load(Dir + "/R_Demolition@" + name + ".fbx");
                    if (src == null) { sb.Append("  ").Append(name).Append(" — 클립 없음\n"); continue; }

                    float before = TravelYaw(rig, src);

                    // 의도는 분명히 90도 단위다. 실측값을 그대로 쓰면 달리기의 자연스러운
                    // 몸통 비틀림까지 보정에 섞여 들어가 매번 몇 도씩 달라진다.
                    float snap = Mathf.Round(before / 90f) * 90f;
                    if (Mathf.Abs(snap) < 1f)
                    {
                        sb.Append("  ").Append(name).Append(" — 이미 정면(")
                          .Append(before.ToString("F0")).Append("도), 건너뜀\n");
                        continue;
                    }

                    AnimationClip dst = Rotate(src, -snap);
                    string outPath = OutDir + "/R_" + name + ".anim";
                    AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
                    if (existing != null) { EditorUtility.CopySerialized(dst, existing); dst = existing; }
                    else AssetDatabase.CreateAsset(dst, outPath);
                    EditorUtility.SetDirty(dst);
                    AssetDatabase.SaveAssets();

                    float after = TravelYaw(rig, dst);
                    sb.Append("  ").Append(name.PadRight(7))
                      .Append(" 진행방향 ").Append(before.ToString("F0"))
                      .Append("도 -> ").Append(after.ToString("F0")).Append("도")
                      .Append("  (").Append(-snap).Append("도 적용)  -> ").Append(outPath).Append("\n");
                }
            }
            finally { Object.DestroyImmediate(rig); }

            sb.Append("  ※ 각도만 잰다. 돌려 놓은 동작이 보기 좋은지는 눈으로 봐야 한다.");
            LastReport = sb.ToString();
            Debug.Log(LastReport);
        }

        static AnimationClip Load(string fbx)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(fbx))
            {
                var c = o as AnimationClip;
                if (c != null && !c.name.StartsWith("__")) return c;
            }
            return null;
        }

        /// <summary>
        /// 클립을 Y축으로 돌린 <b>새 클립</b>을 만든다. FBX 안의 클립은 못 고치므로 복제한다.
        /// </summary>
        static AnimationClip Rotate(AnimationClip src, float degrees)
        {
            var dst = new AnimationClip { frameRate = src.frameRate, name = src.name };
            Quaternion fix = Quaternion.Euler(0f, degrees, 0f);

            foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(src))
            {
                if (b.path == TopNode &&
                    (b.propertyName.StartsWith("m_LocalRotation") ||
                     b.propertyName.StartsWith("m_LocalPosition")))
                    continue;                        // 아래에서 통째로 다시 만든다
                AnimationUtility.SetEditorCurve(dst, b, AnimationUtility.GetEditorCurve(src, b));
            }

            AnimationCurve rx = Get(src, "m_LocalRotation.x"), ry = Get(src, "m_LocalRotation.y");
            AnimationCurve rz = Get(src, "m_LocalRotation.z"), rw = Get(src, "m_LocalRotation.w");
            AnimationCurve px = Get(src, "m_LocalPosition.x"), py = Get(src, "m_LocalPosition.y");
            AnimationCurve pz = Get(src, "m_LocalPosition.z");
            if (rx == null || ry == null || rz == null || rw == null)
            {
                Debug.LogError("[ClipYawFix] " + TopNode + "에 회전 커브가 없다: " + src.name);
                return dst;
            }

            int frames = Mathf.Max(1, Mathf.RoundToInt(src.length * src.frameRate));
            var kqx = new Keyframe[frames + 1]; var kqy = new Keyframe[frames + 1];
            var kqz = new Keyframe[frames + 1]; var kqw = new Keyframe[frames + 1];
            var kpx = new Keyframe[frames + 1]; var kpy = new Keyframe[frames + 1];
            var kpz = new Keyframe[frames + 1];

            // 프레임마다 다시 샘플링한다. 원본이 이미 프레임 단위로 구워져 있어 손실이 없고,
            // 네 성분의 키 시각이 어긋나 있어도 안전하다.
            for (int f = 0; f <= frames; f++)
            {
                float t = f / src.frameRate;
                Quaternion q = fix * new Quaternion(rx.Evaluate(t), ry.Evaluate(t),
                                                    rz.Evaluate(t), rw.Evaluate(t));
                kqx[f] = new Keyframe(t, q.x); kqy[f] = new Keyframe(t, q.y);
                kqz[f] = new Keyframe(t, q.z); kqw[f] = new Keyframe(t, q.w);

                Vector3 p = fix * new Vector3(px != null ? px.Evaluate(t) : 0f,
                                              py != null ? py.Evaluate(t) : 0f,
                                              pz != null ? pz.Evaluate(t) : 0f);
                kpx[f] = new Keyframe(t, p.x); kpy[f] = new Keyframe(t, p.y);
                kpz[f] = new Keyframe(t, p.z);
            }

            Set(dst, "m_LocalRotation.x", kqx); Set(dst, "m_LocalRotation.y", kqy);
            Set(dst, "m_LocalRotation.z", kqz); Set(dst, "m_LocalRotation.w", kqw);
            Set(dst, "m_LocalPosition.x", kpx); Set(dst, "m_LocalPosition.y", kpy);
            Set(dst, "m_LocalPosition.z", kpz);

            // 루프 설정은 원본을 그대로 따라간다. 여기서 바꾸면 자르기 단계의 판단이 사라진다.
            AnimationUtility.SetAnimationClipSettings(dst, AnimationUtility.GetAnimationClipSettings(src));
            return dst;
        }

        static AnimationCurve Get(AnimationClip c, string prop)
        {
            var b = EditorCurveBinding.FloatCurve(TopNode, typeof(Transform), prop);
            return AnimationUtility.GetEditorCurve(c, b);
        }

        static void Set(AnimationClip c, string prop, Keyframe[] keys)
        {
            var b = EditorCurveBinding.FloatCurve(TopNode, typeof(Transform), prop);
            AnimationUtility.SetEditorCurve(c, b, new AnimationCurve(keys));
        }

        /// <summary>
        /// 디딘 발이 몸 기준으로 뒤로 가는 방향(도). 0이면 정면으로 나아간다.
        /// 몸통 yaw가 아니라 <b>발</b>을 보는 이유는, 달리기에서 상체가 좌우로 비틀려
        /// 몸통 각도는 프레임마다 20도씩 흔들리기 때문이다.
        /// </summary>
        static float TravelYaw(GameObject rig, AnimationClip clip)
        {
            Transform lf = null, rf = null;
            foreach (Transform t in rig.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "LeftFoot") lf = t;
                else if (t.name == "RightFoot") rf = t;
            }
            if (lf == null || rf == null) return 0f;

            int frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * clip.frameRate));
            Vector2 sum = Vector2.zero;
            Vector3 l0 = Vector3.zero, r0 = Vector3.zero;
            bool have = false;

            AnimationMode.StartAnimationMode();
            for (int f = 0; f <= frames; f++)
            {
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(rig, clip, f / clip.frameRate);
                AnimationMode.EndSampling();
                Vector3 l = rig.transform.InverseTransformPoint(lf.position);
                Vector3 r = rig.transform.InverseTransformPoint(rf.position);
                if (have)
                {
                    bool left = l.y <= r.y;
                    Vector3 a = left ? l0 : r0, b = left ? l : r;
                    sum += new Vector2(-(b.x - a.x), -(b.z - a.z));
                }
                l0 = l; r0 = r; have = true;
            }
            AnimationMode.StopAnimationMode();
            return Mathf.Atan2(sum.x, sum.y) * Mathf.Rad2Deg;
        }
    }
}
