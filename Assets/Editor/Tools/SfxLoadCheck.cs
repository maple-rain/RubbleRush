using System.Collections.Generic;
using System.Text;
using Demo.Data;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 소리가 <b>몇 번 씹히는지</b> 센다 [2026-10-08].
    ///
    /// <b>"씹히는 느낌"을 느낌으로 두면 못 고친다.</b> 원인이 둘인데 고치는 법이 정반대다 —
    /// 음성을 <b>뺏는</b> 것(울리던 소리를 끊음)이면 소리를 짧게 하거나 음성을 늘려야 하고,
    /// <c>minGap</c>에 <b>막히는</b> 것이면 간격을 줄여야 한다. 세어 보면 바로 갈린다.
    ///
    /// <b>왜 플레이 모드가 아닌가.</b> MCP로 플레이를 켜도 에디터에 포커스가 없으면 프레임이
    /// 안 돌아 <c>Time</c>이 멈춘다 — 타이밍이 핵심인 측정을 거기서 하면 안 된다.
    /// 대신 <see cref="Utils.SfxPool"/>의 뺏기 규칙을 그대로 돌리되 시간은 내가 쥔다.
    /// 클립 길이는 <b>실제 SoundBank</b>에서 읽으므로 음원을 갈면 결과도 따라 바뀐다.
    ///
    /// <b>안 재는 것</b>: 실제로 귀에 어떻게 들리는가. 겹친 소리의 음색은
    /// <c>Tools/sfx_feel.py</c>가 파형을 합성해서 잰다.
    /// </summary>
    public static class SfxLoadCheck
    {
        /// <summary>
        /// 연타 간격(초). <c>Tools/balance/punch.csv</c> 실측: 한 방이 0.458/3.08 = 0.149초이고
        /// <c>rush.fastGap 0.1</c> ~ <c>rush.slowGap 0.3</c> 사이에서 논다. 가운데를 잡는다.
        /// </summary>
        const float Gap = 0.20f;

        const float Seconds = 10f;

        /// <summary>한 번 두드릴 때 울리는 것. 게임의 호출 자리와 같다.</summary>
        static readonly Sfx[] PerTap = { Sfx.Punch, Sfx.Strike };

        /// <summary>가끔 오는 큰 박자. (초, 사건)</summary>
        static readonly (float every, Sfx id)[] Beats =
        {
            (2.5f, Sfx.FloorCollapse),
            (0.9f, Sfx.Swallow),        // 잔해가 구멍에 들어간다
            (1.4f, Sfx.Drop),
            (1.1f, Sfx.Snag),
        };

        [MenuItem("Demo/20. 소리 씹힘 점검", priority = 74)]
        public static void Run() => Debug.Log(Report());

        public static string Report()
        {
            var bank = Load();
            if (bank == null) return "[소리 씹힘] SoundBank를 못 찾았다.";

            int voices = Voices();
            var sb = new StringBuilder();
            sb.AppendLine("소리 씹힘 점검 — " + Seconds.ToString("F0") + "초 동안 " +
                          Gap.ToString("F2") + "초 간격으로 두드린다 (음성 " + voices + "개)");
            sb.AppendLine();

            List<Shot> shots = Timeline(bank);
            Simulate(shots, voices, out int steals, out int peak, out List<Shot> cut);

            sb.AppendLine("  울린 횟수      " + shots.Count);
            sb.AppendLine("  씹힌 횟수      " + steals + (steals > 0
                ? "   <- 울리던 소리를 끊고 자리를 뺏었다" : "   없음"));
            sb.AppendLine("  동시 최대      " + peak + " / " + voices
                          + (peak >= voices ? "   <- 꽉 찼다" : ""));
            sb.AppendLine();

            Occupancy(sb, bank, voices);

            if (cut.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  끊긴 소리 (앞에서 열 개만):");
                for (int i = 0; i < cut.Count && i < 10; i++)
                    sb.AppendLine("    " + cut[i].Id.ToString().PadRight(14)
                                  + cut[i].Len.ToString("F2") + "초짜리를 "
                                  + cut[i].Played.ToString("F2") + "초에서 끊었다 ("
                                  + (100f * cut[i].Played / Mathf.Max(0.01f, cut[i].Len)).ToString("F0") + "%)");
            }

            sb.AppendLine();
            sb.AppendLine("  안 잰 것: 귀에 어떻게 들리는가. 겹친 음색은 Tools/sfx_feel.py가 잰다.");
            return sb.ToString();
        }

        /// <summary>
        /// 사건별로 <b>음성을 몇 개나 물고 있는가</b>. 씹힘의 원인은 거의 늘 여기서 보인다 —
        /// 긴 소리 하나가 짧은 소리 여럿의 자리를 잡아먹는다.
        /// </summary>
        static void Occupancy(StringBuilder sb, SoundBank bank, int voices)
        {
            sb.AppendLine(string.Format("  {0,-14} {1,7} {2,9} {3,10}  {4}",
                                        "사건", "길이s", "초당 횟수", "점유 음성", "판정"));

            float total = 0f;
            foreach (Sfx id in PerTap) total += Line(sb, bank, id, 1f / Gap);
            foreach (var b in Beats) total += Line(sb, bank, b.id, 1f / b.every);

            sb.AppendLine("  " + "".PadRight(14) + "".PadLeft(7) + "".PadLeft(9)
                          + total.ToString("F1").PadLeft(10) + "  합계"
                          + (total > voices ? "   <- 음성 " + voices + "개를 넘는다. 넘친 만큼 씹힌다" : ""));
        }

        static float Line(StringBuilder sb, SoundBank bank, Sfx id, float perSecond)
        {
            float len = Length(bank, id);
            float hold = len * perSecond;
            sb.AppendLine(string.Format("  {0,-14} {1,7:F2} {2,9:F1} {3,10:F1}  {4}",
                id, len, perSecond, hold,
                hold >= 2f ? "길어서 겹친다" : hold >= 1f ? "한 발이 다음 발까지 간다" : ""));
            return hold;
        }

        struct Shot
        {
            public float At;
            public float Len;
            public Sfx Id;
            public float Played;
        }

        static List<Shot> Timeline(SoundBank bank)
        {
            var shots = new List<Shot>();

            for (float t = 0f; t < Seconds; t += Gap)
                foreach (Sfx id in PerTap)
                    shots.Add(new Shot { At = t, Len = Length(bank, id), Id = id });

            foreach (var b in Beats)
                for (float t = b.every; t < Seconds; t += b.every)
                    shots.Add(new Shot { At = t, Len = Length(bank, b.id), Id = b.id });

            // 마지막에 완파. 제일 긴 소리라 뒤에 오는 것을 전부 밀어낸다.
            shots.Add(new Shot { At = Seconds * 0.8f, Len = Length(bank, Sfx.Broken), Id = Sfx.Broken });

            shots.Sort((a, b) => a.At.CompareTo(b.At));
            return shots;
        }

        /// <summary>
        /// <see cref="Utils.SfxPool.Take"/>와 <b>같은 규칙</b>으로 돌린다 —
        /// 쉬는 것을 먼저 주고, 없으면 가장 오래전에 시작한 것을 뺏는다.
        /// </summary>
        static void Simulate(List<Shot> shots, int voices, out int steals, out int peak,
                             out List<Shot> cut)
        {
            var endsAt = new float[voices];
            var startAt = new float[voices];
            var what = new Sfx[voices];
            var len = new float[voices];
            for (int i = 0; i < voices; i++) { endsAt[i] = float.NegativeInfinity; startAt[i] = float.NegativeInfinity; }

            steals = 0;
            peak = 0;
            cut = new List<Shot>();

            foreach (Shot s in shots)
            {
                int busy = 0;
                for (int i = 0; i < voices; i++) if (endsAt[i] > s.At) busy++;
                if (busy > peak) peak = busy;

                int slot = -1;
                for (int i = 0; i < voices; i++)
                    if (endsAt[i] <= s.At) { slot = i; break; }

                if (slot < 0)
                {
                    int oldest = 0;
                    for (int i = 1; i < voices; i++)
                        if (startAt[i] < startAt[oldest]) oldest = i;
                    slot = oldest;
                    steals++;
                    cut.Add(new Shot { Id = what[slot], Len = len[slot],
                                       Played = s.At - startAt[slot] });
                }

                startAt[slot] = s.At;
                endsAt[slot] = s.At + s.Len;
                what[slot] = s.Id;
                len[slot] = s.Len;
            }

            if (peak + 1 > voices) peak = voices;
            else peak = Mathf.Min(voices, peak + 1);
        }

        static float Length(SoundBank bank, Sfx id)
        {
            SoundBank.Entry e = bank.Find(id);
            if (e == null || e.clips == null) return 0f;

            float longest = 0f;
            foreach (AudioClip c in e.clips) if (c != null) longest = Mathf.Max(longest, c.length);
            return longest;
        }

        static SoundBank Load()
        {
            string[] guids = AssetDatabase.FindAssets("t:SoundBank");
            return guids.Length == 0 ? null
                : AssetDatabase.LoadAssetAtPath<SoundBank>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        /// <summary>씬에 박힌 음성 수를 읽는다 — C# 기본값이 아니라 <b>씬의 값</b>이어야 한다.</summary>
        static int Voices()
        {
            var a = Object.FindFirstObjectByType<Utils.GameAudio>();
            if (a == null) return 12;
            var p = new SerializedObject(a).FindProperty("voices");
            return p != null ? Mathf.Max(1, p.intValue) : 12;
        }
    }
}
