using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Demo.EditorTools
{
    /// <summary>
    /// 밸런스 표를 읽고 쓰는 최소한의 CSV 처리.
    ///
    /// 라이브러리를 들이지 않는 이유는 여기서 필요한 것이 세 가지뿐이기 때문이다 —
    /// <b>엑셀에서 한글이 안 깨질 것</b>(UTF-8 BOM), 값에 쉼표가 들어가도 버틸 것(따옴표),
    /// 그리고 주석 줄을 건너뛸 것(<c>#</c>).
    ///
    /// BOM이 핵심이다. 없이 쓰면 엑셀이 한글 CSV를 시스템 코드페이지로 읽어 전부 깨진다 —
    /// 기획자가 표를 열자마자 못 쓰게 된다.
    ///
    /// 숫자는 항상 <see cref="CultureInfo.InvariantCulture"/>로 읽고 쓴다.
    /// 한국어 윈도우에서도 소수점은 점이어야 파일이 기계 사이에서 옮겨 다닌다.
    /// </summary>
    public static class Csv
    {
        /// <summary>엑셀이 한글을 제대로 읽게 하는 유일한 조건. 빼면 전부 깨진다.</summary>
        public static readonly UTF8Encoding ExcelSafe = new UTF8Encoding(true);

        /// <summary>한 줄을 만든다. 쉼표·따옴표·줄바꿈이 들어간 값은 따옴표로 감싼다.</summary>
        public static string Row(params string[] cells)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) sb.Append(',');
                string c = cells[i] ?? string.Empty;
                if (c.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
                    sb.Append('"').Append(c.Replace("\"", "\"\"")).Append('"');
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        public static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        public static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
        public static string B(bool v) => v ? "TRUE" : "FALSE";

        /// <summary>
        /// 본문 줄을 파싱한다. 주석(<c>#</c>)과 빈 줄, 그리고 <b>머리글 줄</b>은 빠진다.
        /// 반환값의 각 원소는 한 줄의 셀 목록이다.
        /// </summary>
        public static List<string[]> Parse(string text)
        {
            var rows = new List<string[]>();
            if (string.IsNullOrEmpty(text)) return rows;

            bool headerSeen = false;
            foreach (string raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (!headerSeen) { headerSeen = true; continue; }   // 첫 유효 줄이 머리글
                rows.Add(SplitLine(line));
            }
            return rows;
        }

        static string[] SplitLine(string line)
        {
            var cells = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c != '"') { sb.Append(c); continue; }
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; continue; }
                    quoted = false;
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { cells.Add(sb.ToString().Trim()); sb.Length = 0; }
                else sb.Append(c);
            }
            cells.Add(sb.ToString().Trim());
            return cells.ToArray();
        }

        // ── 셀 읽기 — 비어 있거나 망가졌으면 기본값을 그대로 둔다 ──────────────────
        // 기획자가 열 하나를 비워 두는 것은 "이 값은 건드리지 않겠다"는 뜻이지
        // "0으로 만들라"는 뜻이 아니다.

        public static string Str(string[] row, int i, string fallback) =>
            i < row.Length && row[i].Length > 0 ? row[i] : fallback;

        public static float Num(string[] row, int i, float fallback) =>
            i < row.Length && float.TryParse(row[i], NumberStyles.Float,
                                             CultureInfo.InvariantCulture, out float v)
                ? v : fallback;

        public static int Int(string[] row, int i, int fallback) =>
            i < row.Length && int.TryParse(row[i], NumberStyles.Integer,
                                           CultureInfo.InvariantCulture, out int v)
                ? v : fallback;

        public static bool Bool(string[] row, int i, bool fallback)
        {
            if (i >= row.Length || row[i].Length == 0) return fallback;
            string s = row[i].Trim().ToUpperInvariant();
            if (s == "TRUE" || s == "1" || s == "Y" || s == "O") return true;
            if (s == "FALSE" || s == "0" || s == "N" || s == "X") return false;
            return fallback;
        }
    }
}
