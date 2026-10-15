using System.Collections.Generic;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 빌드가 끝나면 <b>무엇이 얼마나 차지했는지</b> 적는다 [신설 2026-10-15].
    ///
    /// <b>왜 필요한가.</b> CLAUDE.md 예산은 AAB 200MB · WebGL 100MB인데, 실측하니
    /// Assets 345MB 중 <b>서드파티가 148MB</b>다 (VARCOSound 84M · ThirdParty 24M ·
    /// Plugins 17M · Casual 11M · VARCO_URP 9.8M). 유니티는 참조된 것만 넣으므로
    /// 보통은 괜찮지만, 어쩌다 하나가 씬에 물리면 수십 MB가 조용히 따라 들어온다.
    /// <b>넘고 나서 아는 것과 넘는 순간 아는 것은 다르다.</b>
    ///
    /// 유니티의 <see cref="BuildReport"/>가 이미 종류별 합계를 들고 있다. 여기서는
    /// 그것을 예산과 나란히 놓기만 한다 — 새로 재지 않는다.
    /// </summary>
    public static class BuildSize
    {
        /// <summary>CLAUDE.md 성능 예산 [확정].</summary>
        const long AndroidBudget = 200L * 1024 * 1024;
        const long WebGLBudget = 100L * 1024 * 1024;

        public static string Format(BuildReport report, bool android)
        {
            if (report == null) return "[빌드] 리포트가 없다";

            BuildSummary s = report.summary;
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("[빌드] " + s.result + " — " + s.platform);
            sb.AppendLine("  걸린 시간  " + s.totalTime.ToString(@"hh\:mm\:ss"));
            sb.AppendLine("  오류 " + s.totalErrors + " · 경고 " + s.totalWarnings);

            if (s.result != BuildResult.Succeeded)
            {
                sb.Append(Failures(report));
                return sb.ToString();
            }

            long total = (long)s.totalSize;
            long budget = android ? AndroidBudget : WebGLBudget;
            float pct = budget > 0 ? total * 100f / budget : 0f;

            sb.AppendLine("  산출물    " + s.outputPath);
            sb.AppendLine(string.Format("  크기      {0}  /  예산 {1}   ({2:0.#}%){3}",
                          Mb(total), Mb(budget), pct, pct > 100f ? "   ** 예산 초과 **" : ""));

            sb.Append(Breakdown(report, total));
            return sb.ToString();
        }

        /// <summary>
        /// 종류별로 무엇이 컸는가. <b>큰 것부터</b> 적는다 — 줄일 곳을 찾는 것이 목적이지
        /// 목록을 다 보여 주는 것이 목적이 아니다.
        /// </summary>
        static string Breakdown(BuildReport report, long total)
        {
            // Unity 6에서 packedAssets는 묶음마다 들어 있다. 종류별로 합친다.
            var byType = new Dictionary<string, long>();

            foreach (PackedAssets pack in report.packedAssets)
                foreach (PackedAssetInfo info in pack.contents)
                {
                    string type = info.type != null ? info.type.Name : "기타";
                    byType.TryGetValue(type, out long had);
                    byType[type] = had + (long)info.packedSize;
                }

            if (byType.Count == 0) return "  (종류별 내역이 비어 있다)";

            var rows = new List<KeyValuePair<string, long>>(byType);
            rows.Sort((a, b) => b.Value.CompareTo(a.Value));

            var sb = new System.Text.StringBuilder("  내역 (큰 것부터)\n");
            long shown = 0;
            for (int i = 0; i < rows.Count && i < 8; i++)
            {
                shown += rows[i].Value;
                sb.AppendLine(string.Format("    {0,-22} {1,10}   {2,5:0.#}%",
                              rows[i].Key, Mb(rows[i].Value),
                              total > 0 ? rows[i].Value * 100f / total : 0f));
            }
            if (rows.Count > 8)
                sb.AppendLine(string.Format("    {0,-22} {1,10}   (나머지 {2}종)",
                              "…", Mb(SumFrom(rows, 8)), rows.Count - 8));

            return sb.ToString();
        }

        static long SumFrom(List<KeyValuePair<string, long>> rows, int from)
        {
            long n = 0;
            for (int i = from; i < rows.Count; i++) n += rows[i].Value;
            return n;
        }

        /// <summary>왜 실패했는가. 오류 줄만 뽑는다.</summary>
        static string Failures(BuildReport report)
        {
            var sb = new System.Text.StringBuilder("  실패한 이유\n");
            int shown = 0;

            foreach (BuildStep step in report.steps)
                foreach (BuildStepMessage m in step.messages)
                {
                    if (m.type != LogType.Error && m.type != LogType.Exception) continue;
                    if (shown++ >= 10) { sb.AppendLine("    … (더 있다)"); return sb.ToString(); }
                    sb.AppendLine("    " + m.content);
                }

            if (shown == 0) sb.AppendLine("    (리포트에 오류 줄이 없다 — 콘솔 전문을 볼 것)");
            return sb.ToString();
        }

        static string Mb(long bytes) => (bytes / 1024f / 1024f).ToString("0.0") + "MB";
    }
}
