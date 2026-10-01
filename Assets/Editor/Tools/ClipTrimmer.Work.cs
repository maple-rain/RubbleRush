using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 클립을 <b>훑고, 자르고, 보고하는</b> 쪽 [분할 2026-10-01].
    ///
    /// <see cref="ClipTrimmer"/>가 327줄이어서 갈랐다 (CLAUDE.md 규칙 3).
    /// ClipTrimmer.cs에는 메뉴와 진입점(Run)만 남고, 실제 일은 여기 있다 —
    /// Scan(FBX 훑기) · Analyse(이음매 오차 재기) · Distance · ApplyCuts · Format.
    ///
    /// partial로 둔 이유는 BalanceCsvIO.Import.cs에 적은 것과 같다 — 하나의 도구다.
    /// </summary>
    public static partial class ClipTrimmer
    {
        static void Scan(string fbx, GameObject rig, Transform[] bones, List<Cut> cuts)
        {
            var imp = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (imp == null) return;

            ModelImporterClipAnimation[] defs = imp.clipAnimations;
            if (defs == null || defs.Length == 0) defs = imp.defaultClipAnimations;
            if (defs == null) return;

            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(fbx))
            {
                var clip = o as AnimationClip;
                if (clip == null || clip.name.StartsWith("__")) continue;

                ModelImporterClipAnimation def = null;
                foreach (var d in defs) if (d.name == clip.name) def = d;
                if (def == null) continue;

                Cut c;
                if (Analyse(clip, rig, bones, def.loopTime, out c))
                {
                    c.Fbx = fbx;
                    c.Clip = clip.name;
                    // 임포터 프레임은 원본 테이크 기준이다. 지금 클립의 0번 프레임이
                    // 원본의 firstFrame이므로 그만큼 더해 되돌린다.
                    int origin = Mathf.RoundToInt(def.firstFrame);
                    c.First += origin;
                    c.Last += origin;
                    cuts.Add(c);
                }
            }
        }

        static bool Analyse(AnimationClip clip, GameObject rig, Transform[] bones,
                            bool wantLoop, out Cut cut)
        {
            cut = new Cut();
            int frames = Mathf.RoundToInt(clip.length * clip.frameRate);
            if (frames < MinPeriod * 2) return false;

            int b = bones.Length;
            var rot = new Quaternion[(frames + 1) * b];
            for (int f = 0; f <= frames; f++)
            {
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(rig, clip, f / clip.frameRate);
                AnimationMode.EndSampling();
                for (int i = 0; i < b; i++) rot[f * b + i] = bones[i].localRotation;
            }

            // 프레임별 움직임의 양. 정지 구간이 여기서 0으로 드러난다.
            var move = new float[frames + 1];
            for (int f = 1; f <= frames; f++)
                move[f] = Distance(rot, b, f - 1, f);

            // <b>두 번에 나눠 잰다.</b> 중앙값을 클립 전체에서 내면 안 된다 —
            // VARCO 클립은 뒤가 정지로 채워져 있고, Run은 249프레임 중 206개가 0.0도였다.
            // 절반 이상이 0이면 중앙값도 0이 되어 상대 기준이 통째로 무의미해진다 [2026-09-28].
            //
            // 그래서 1차로 <b>상위 백분위</b>를 잣대 삼아 죽은 구간을 걷어내고,
            // 2차로 살아 있는 구간에서만 중앙값을 다시 낸다.
            var sorted = new List<float>(frames);
            for (int f = 1; f <= frames; f++) sorted.Add(move[f]);
            sorted.Sort();
            float active = sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * 0.9f))];
            float dead = active * DeadRatio;

            int head = 0;
            while (head < frames && move[head + 1] <= dead) head++;
            int tail = frames;
            while (tail > head + MinPeriod && move[tail] <= dead) tail--;

            var live = new List<float>();
            for (int f = head + 1; f <= tail; f++) live.Add(move[f]);
            live.Sort();
            float median = live.Count > 0 ? live[live.Count / 2] : active;

            cut.DeadHead = head;
            cut.DeadTail = frames - tail;
            cut.Loop = wantLoop;
            cut.Bones = b;
            cut.Median = median;
            cut.SeamBefore = Distance(rot, b, frames, 0);

            if (!wantLoop)
            {
                cut.First = head;
                cut.Last = tail;
                cut.SeamAfter = cut.SeamBefore;
                return cut.DeadHead > 0 || cut.DeadTail > 0;
            }

            // 자세와 속도가 함께 맞는 (시작, 주기)를 찾는다.
            //
            // <b>짧은 주기 편향을 막아야 한다.</b> 자세 거리는 주기 길이에 비례하지 않아서,
            // 그냥 최소값을 고르면 움직임이 적은 구간의 <b>가장 짧은 창</b>이 항상 이긴다.
            // 실제로 10.25초 Idle에서 8프레임(0.33초)을 골라 왔다. 그래서 "충분히 맞는"
            // 후보들 중에서는 <b>가장 긴 것</b>을 고른다 — 동작의 변화를 더 많이 남긴다.
            //
            // 기준은 두 잣대의 <b>큰 쪽</b>이다.
            //
            // 절대 각도(뼈당 2도)만 쓰면 <b>빠른 동작</b>에서 오판한다 — 달리기는 프레임당
            // 뼈당 4~8도씩 움직이므로 이음매 8도는 한 프레임어치라 안 보이는데,
            // 걷기 기준으로 재면 "너무 크다"가 되어 멀쩡한 루프를 거부한다.
            //
            // 거꾸로 상대 기준(한 프레임 이동량)만 쓰면 <b>대기 동작</b>에서 무너진다 —
            // Idle은 프레임당 0.01도라 기준이 사실상 0이 되고, 그러면 아무 후보도 통과 못 해
            // 최소값으로 떨어져 8프레임짜리 토막을 고른다. 실제로 두 번 그랬다 [2026-09-28].
            float accept = Mathf.Max(median, 2f * b);
            int anyStart = head, anyPeriod = tail - head;
            float anySeam = float.MaxValue;
            int longStart = -1, longPeriod = -1;
            float longSeam = 0f;

            for (int p = MinPeriod; p <= tail - head - 1; p++)
            {
                for (int i = head; i + p + 1 <= tail; i++)
                {
                    float d = Distance(rot, b, i, i + p) + Distance(rot, b, i + 1, i + p + 1);
                    if (d < anySeam) { anySeam = d; anyStart = i; anyPeriod = p; }
                    if (d <= accept && p > longPeriod) { longSeam = d; longStart = i; longPeriod = p; }
                }
            }

            bool haveLong = longPeriod > 0;
            int pickStart = haveLong ? longStart : anyStart;
            int pickPeriod = haveLong ? longPeriod : anyPeriod;
            float pickSeam = haveLong ? longSeam : anySeam;

            // <b>지금보다 나빠지면 손대지 않는다.</b> 이미 한 사이클로 잘라 둔 클립은 진짜 주기가
            // 클립 길이와 같은데, 속도 항이 i+p+1 프레임을 요구해서 탐색이 거기까지 못 간다.
            // 그대로 두면 멀쩡한 클립을 더 나쁘게 자른다.
            if (pickSeam >= cut.SeamBefore)
            {
                cut.First = head;
                cut.Last = tail;
                cut.SeamAfter = cut.DeadHead > 0 || cut.DeadTail > 0
                    ? Distance(rot, b, tail, head)
                    : cut.SeamBefore;
                return cut.DeadHead > 0 || cut.DeadTail > 0;
            }

            cut.First = pickStart;
            cut.Last = pickStart + pickPeriod;
            cut.SeamAfter = pickSeam;
            return true;
        }

        /// <summary>두 프레임의 자세 차이(도의 합). 뼈 수가 같으므로 클립끼리 비교는 무의미하다.</summary>
        static float Distance(Quaternion[] rot, int bones, int f0, int f1)
        {
            float d = 0f;
            for (int i = 0; i < bones; i++)
                d += Quaternion.Angle(rot[f0 * bones + i], rot[f1 * bones + i]);
            return d;
        }

        static void ApplyCuts(List<Cut> cuts)
        {
            var byFbx = new Dictionary<string, List<Cut>>();
            foreach (Cut c in cuts)
            {
                if (!byFbx.ContainsKey(c.Fbx)) byFbx[c.Fbx] = new List<Cut>();
                byFbx[c.Fbx].Add(c);
            }

            foreach (var pair in byFbx)
            {
                var imp = AssetImporter.GetAtPath(pair.Key) as ModelImporter;
                if (imp == null) continue;

                ModelImporterClipAnimation[] defs = imp.clipAnimations;
                if (defs == null || defs.Length == 0) defs = imp.defaultClipAnimations;
                if (defs == null) continue;

                foreach (var d in defs)
                    foreach (Cut c in pair.Value)
                    {
                        if (d.name != c.Clip) continue;
                        d.firstFrame = c.First;
                        d.lastFrame = c.Last;
                        // loopPose는 남은 이음매를 유니티가 클립 전체에 분산해 흡수하게 한다.
                        // loopTime만 켜면 끝에서 시작으로 그대로 튄다 — Walk가 158도를 튀던 이유다.
                        //
                        // 단, 어긋남이 크면 그 각도를 동작 전체에 밀어 넣어 <b>자세를 왜곡한다.</b>
                        // 한 프레임 이동량의 두 배(느린 동작은 뼈당 4도)를 넘으면 애초에
                        // 한 사이클이 아니라는 뜻이라, 켜지 않고 남겨 사람이 보게 한다.
                        if (c.Loop && c.SeamAfter <= Mathf.Max(2f * c.Median, 4f * c.Bones))
                            d.loopPose = true;
                    }

                imp.clipAnimations = defs;
                imp.SaveAndReimport();
            }
            AssetDatabase.Refresh();
        }

        static string Format(List<Cut> cuts, bool applied)
        {
            var sb = new StringBuilder("[ClipTrimmer] ");
            sb.Append(applied ? "적용" : "분석만 (적용하려면 Demo/8b)").Append(" — ");
            sb.Append(cuts.Count).Append("개 클립\n");
            foreach (Cut c in cuts)
            {
                sb.Append("  ").Append(c.Clip.PadRight(10));
                sb.Append(" 구간 ").Append(c.First).Append("~").Append(c.Last);
                sb.Append(" (").Append(c.Last - c.First).Append("프레임)");
                sb.Append(c.Loop ? "  루프" : "  단발");
                sb.Append("  죽은앞=").Append(c.DeadHead);
                sb.Append(" 죽은뒤=").Append(c.DeadTail);
                sb.Append("  이음매 ").Append(c.SeamBefore.ToString("F1"));
                sb.Append(" -> ").Append(c.SeamAfter.ToString("F1")).Append("도");
                sb.Append(" (프레임 이동량의 ")
                  .Append((c.SeamAfter / Mathf.Max(0.01f, c.Median)).ToString("F2")).Append("배, 뼈당 ")
                  .Append((c.SeamAfter / Mathf.Max(1, c.Bones)).ToString("F1")).Append("도");
                if (c.Loop && c.SeamAfter > Mathf.Max(2f * c.Median, 4f * c.Bones))
                    sb.Append(" — 너무 커서 loopPose 안 켬");
                sb.Append(")\n");
            }
            sb.Append("  ※ 이 도구는 이음매 오차만 잰다. 고른 사이클이 보기 좋은지,");
            sb.Append(" 발이 여전히 미끄러지는지는 눈으로 봐야 한다.");
            return sb.ToString();
        }
    }
}
