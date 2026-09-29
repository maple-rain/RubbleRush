using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 클립을 <b>좌우로 뒤집어</b> 반대손 버전을 만든다 (CLAUDE.md 규칙 8).
    ///
    /// <b>왜 필요한가.</b> 양팔이 번갈아 나가는 타격을 만들려면 오른손 펀치가 있어야 하는데,
    /// VARCO 카탈로그에는 <c>boxing_punch</c> 하나뿐이고 그건 <b>왼손</b>으로 친다
    /// (실측: 왼손 z 0.45→1.17, 오른손 0.25→0.06) [지시 2026-09-28].
    ///
    /// <b>유니티 기본 미러링을 못 쓰는 이유.</b> <c>AnimationClip</c>의 Mirror 옵션은
    /// <b>Humanoid 리그 전용</b>이다. 이 로봇은 데포르메 비례가 Humanoid 정규화에서
    /// 뭉개지는 것을 피하려고 <b>Generic</b>으로 넣었다. 그래서 직접 뒤집는다.
    ///
    /// <b>로컬 회전을 바꿔치기하면 안 된다.</b> FBX 리그는 좌우 뼈의 축 방향(roll)이
    /// 대칭이 아닌 경우가 흔해서, <c>LeftArm</c>의 로컬 회전을 <c>RightArm</c>에 그대로
    /// 넣으면 팔이 엉뚱한 방향으로 꺾인다.
    ///
    /// <b>그래서 월드를 거친다.</b> 프레임마다
    /// <list type="number">
    /// <item>자세를 샘플링해 각 뼈의 <b>루트 기준 회전</b>을 읽고</item>
    /// <item>좌우 짝의 회전을 X축 기준으로 뒤집고 (<c>q' = (x, −y, −z, w)</c>)</item>
    /// <item>부모부터 차례로 <b>로컬 회전을 역산</b>한다</item>
    /// </list>
    /// 뼈 축이 어떻게 생겼든 결과가 정확히 거울상이 된다.
    ///
    /// <b>이 도구가 검증하지 않는 것:</b> 뒤집은 동작이 어색하지 않은지. 좌우 비대칭인
    /// 리그(한쪽 팔이 더 긴 등)에서는 회전만으로 완전한 거울상이 안 나온다 — 위치는 건드리지 않는다.
    /// </summary>
    public static class ClipMirror
    {
        const string Dir = "Assets/_Project/Art/Models/Robot";
        const string OutDir = Dir + "/Clips";

        public static string LastReport { get; private set; }

        [MenuItem("Demo/10. 클립 좌우 뒤집기 (반대손 만들기)", priority = 53)]
        public static void MirrorPunch()
        {
            LastReport = Run("R_Demolition@Punch.fbx", "Punch", "R_PunchRight.anim");
            Debug.Log(LastReport);
        }

        public static string Run(string fbx, string clipName, string outName)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/R_Demolition.fbx");
            if (model == null) return "[ClipMirror] 기본 모델 없음";
            if (!AssetDatabase.IsValidFolder(OutDir)) AssetDatabase.CreateFolder(Dir, "Clips");

            AnimationClip src = null;
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(Dir + "/" + fbx))
            {
                var c = o as AnimationClip;
                if (c != null && !c.name.StartsWith("__")) src = c;
            }
            if (src == null) return "[ClipMirror] 원본 클립 없음: " + fbx;

            GameObject rig = Object.Instantiate(model);
            rig.hideFlags = HideFlags.HideAndDontSave;

            var bones = new List<Transform>();
            var paths = new List<string>();
            Collect(rig.transform, "", bones, paths);

            // 좌우 짝. 이름에 Left/Right가 없으면 자기 자신이 짝이다 (척추·머리 등).
            var index = new Dictionary<string, int>();
            for (int i = 0; i < paths.Count; i++) index[paths[i]] = i;
            var partner = new int[paths.Count];
            int pairs = 0;
            for (int i = 0; i < paths.Count; i++)
            {
                string swapped = Swap(paths[i]);
                int j;
                if (swapped != paths[i] && index.TryGetValue(swapped, out j)) { partner[i] = j; pairs++; }
                else partner[i] = i;
            }

            // 부모가 먼저 오도록 깊이순으로 처리 순서를 잡는다. 로컬 회전을 역산할 때
            // 부모의 뒤집힌 회전이 이미 나와 있어야 한다.
            var order = new List<int>();
            for (int i = 0; i < paths.Count; i++) order.Add(i);
            order.Sort((a, b) => Depth(paths[a]).CompareTo(Depth(paths[b])));
            var parentOf = new int[paths.Count];
            for (int i = 0; i < paths.Count; i++)
            {
                int cut = paths[i].LastIndexOf('/');
                int p;
                parentOf[i] = cut >= 0 && index.TryGetValue(paths[i].Substring(0, cut), out p) ? p : -1;
            }

            int frames = Mathf.Max(1, Mathf.RoundToInt(src.length * src.frameRate));
            int n = paths.Count;
            var keyX = new Keyframe[n][]; var keyY = new Keyframe[n][];
            var keyZ = new Keyframe[n][]; var keyW = new Keyframe[n][];
            for (int i = 0; i < n; i++)
            {
                keyX[i] = new Keyframe[frames + 1]; keyY[i] = new Keyframe[frames + 1];
                keyZ[i] = new Keyframe[frames + 1]; keyW[i] = new Keyframe[frames + 1];
            }

            var world = new Quaternion[n];
            var flipped = new Quaternion[n];

            AnimationMode.StartAnimationMode();
            try
            {
                for (int f = 0; f <= frames; f++)
                {
                    float t = f / src.frameRate;
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(rig, src, t);
                    AnimationMode.EndSampling();

                    for (int i = 0; i < n; i++)
                        world[i] = Quaternion.Inverse(rig.transform.rotation) * bones[i].rotation;

                    for (int k = 0; k < order.Count; k++)
                    {
                        int i = order[k];
                        flipped[i] = Flip(world[partner[i]]);
                        int p = parentOf[i];
                        Quaternion local = p >= 0
                            ? Quaternion.Inverse(flipped[p]) * flipped[i]
                            : flipped[i];
                        keyX[i][f] = new Keyframe(t, local.x); keyY[i][f] = new Keyframe(t, local.y);
                        keyZ[i][f] = new Keyframe(t, local.z); keyW[i][f] = new Keyframe(t, local.w);
                    }
                }
            }
            finally { AnimationMode.StopAnimationMode(); }

            var dst = new AnimationClip { frameRate = src.frameRate, name = System.IO.Path.GetFileNameWithoutExtension(outName) };
            // 회전이 아닌 커브(위치·스케일)는 원본 그대로 둔다. 리그의 쉬는 자세 간격이라
            // 뒤집을 대상이 아니고, 건드리면 팔 길이가 달라진다.
            foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(src))
                if (!b.propertyName.StartsWith("m_LocalRotation"))
                    AnimationUtility.SetEditorCurve(dst, b, AnimationUtility.GetEditorCurve(src, b));

            for (int i = 0; i < n; i++)
            {
                Set(dst, paths[i], "m_LocalRotation.x", keyX[i]);
                Set(dst, paths[i], "m_LocalRotation.y", keyY[i]);
                Set(dst, paths[i], "m_LocalRotation.z", keyZ[i]);
                Set(dst, paths[i], "m_LocalRotation.w", keyW[i]);
            }
            AnimationUtility.SetAnimationClipSettings(dst, AnimationUtility.GetAnimationClipSettings(src));

            Object.DestroyImmediate(rig);

            string outPath = OutDir + "/" + outName;
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
            if (existing != null) { EditorUtility.CopySerialized(dst, existing); dst = existing; }
            else AssetDatabase.CreateAsset(dst, outPath);
            EditorUtility.SetDirty(dst);
            AssetDatabase.SaveAssets();

            var sb = new StringBuilder("[ClipMirror] ");
            sb.Append(clipName).Append(" -> ").Append(outPath);
            sb.Append(" | 뼈 ").Append(n).Append("개, 좌우 짝 ").Append(pairs).Append("개");
            sb.Append(" | ").Append(frames + 1).Append("프레임 ").Append(src.length.ToString("F3")).Append("초");
            return sb.ToString();
        }

        /// <summary>X축 기준 거울. 축 a를 중심으로 θ 도는 회전은 −a를 중심으로 −θ가 된다.</summary>
        static Quaternion Flip(Quaternion q) { return new Quaternion(q.x, -q.y, -q.z, q.w); }

        static string Swap(string path)
        {
            if (path.Contains("Left")) return path.Replace("Left", "Right");
            if (path.Contains("Right")) return path.Replace("Right", "Left");
            return path;
        }

        static int Depth(string path)
        {
            int d = 1;
            for (int i = 0; i < path.Length; i++) if (path[i] == '/') d++;
            return d;
        }

        static void Collect(Transform t, string prefix, List<Transform> bones, List<string> paths)
        {
            foreach (Transform c in t)
            {
                string p = string.IsNullOrEmpty(prefix) ? c.name : prefix + "/" + c.name;
                bones.Add(c); paths.Add(p);
                Collect(c, p, bones, paths);
            }
        }

        static void Set(AnimationClip c, string path, string prop, Keyframe[] keys)
        {
            AnimationUtility.SetEditorCurve(c,
                EditorCurveBinding.FloatCurve(path, typeof(Transform), prop),
                new AnimationCurve(keys));
        }
    }
}
