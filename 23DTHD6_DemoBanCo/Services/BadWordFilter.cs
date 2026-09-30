using System.Globalization;
using System.Text;

namespace _23DTHD6_DemoBanCo.Services
{
    /// <summary>
    /// Che tiếng thô trong chat và tên hiển thị.
    ///
    /// CÁCH SO KHỚP:
    ///  1. Bỏ dấu tiếng Việt cả ở câu và ở từ cấm, nên "ĐM" khớp "đm", "ĐÉO" khớp "đéo".
    ///     Ký tự "đ"/"Đ" không phải dấu phụ nên được quy về "d" để khớp với cách viết tắt không dấu.
    ///  2. Ký tự đại diện '*', '.', '_' khớp 0 hoặc 1 ký tự bất kỳ:
    ///     "d*m" khớp "dm", "dam", "đm"; "th*ng cha" khớp "thang cha" và "thng cha".
    ///  3. Mẫu chỉ khớp khi ranh giới trùng với ranh giới từ: ký tự trước và sau mẫu không được là chữ cái.
    ///     Nhờ vậy "lonely" không bị chặn vì chứa "lon", "cach" không bị chặn vì chứa "cac".
    ///  4. Danh sách SafeWords giữ lại vài từ hợp lệ bị lẫn sau khi bỏ dấu ("các" -> "cac").
    ///     SafeWords được so trên chuỗi GỐC còn dấu, nên "các" qua mà "cặc" vẫn bị chặn.
    ///
    /// ĐÁNH ĐỔI ĐÃ BIẾT: sau khi bỏ dấu, "ngu" (tiếng lậm) trùng hệt "ngủ" (ngủ), nên mẫu "ngu"
    /// đã bị gỡ khỏi danh sách. Tương tự, người dùng gõ "cac" không dấu cũng không bị chặn.
    /// Chấp nhận được với bộ lọc chat demo; bảo mật thật thì phải so trên chuỗi giữ dấu hoặc dùng NLP.
    /// </summary>
    public static class BadWordFilter
    {
        private const string Replacement = "***";

        private static readonly char[] WildcardChars = { '*', '.', '_' };

        /// <summary>Từ cấm gốc, hiển thị được (giữ dấu). Khớp thực tế dùng <see cref="Patterns"/>.</summary>
        private static readonly string[] RawWords =
        {
            // --- Tiếng Việt ---
            "dm",       // đm
            "d*m",      // đm, dcm, dm
            "deo",      // đéo
            "cac",      // cặc
            "dit",      // địt
            "lon",      // lồn
            "vcl",      // vcl
            "vl",       // vl
            "vkl",      // vkl
            "cailon",
            "clgt",
            "clm",
            "kdt",      // kđt
            "cmm",
            "con cho",  // con chó
            "cho ma",   // chó má
            "suc sinh", // súc sinh
            "thangcha", // thằngcha
            "th*ng cha",
            "me may",   // mẹ mày
            "tao may",  // tao mày
            "chet di",  // chết đi
            "oc cho",   // óc chó
            "dai vo",   // dãi v, đại v

            // --- Tiếng Anh ---
            "fuck", "fucking", "fucked", "shit", "bullshit", "bitch",
            "asshole", "bastard", "dick", "cunt", "whore", "slut",
            "faggot", "nigger", "retard", "moron", "idiot", "jackass",
            "dumbass", "prick", "wanker", "pussy"
        };

        /// <summary>
        /// Từ hợp lệ bị trùng sau khi bỏ dấu. So khớp trên chuỗi GỐC còn dấu và hạ chữ thường,
        /// chỉ liệt kê dạng có dấu đúng viết. Dạng không dấu đã bị gỡ có chủ ý xem MỤC ĐÁNH ĐỔI ở trên.
        /// </summary>
        private static readonly string[] SafeWords =
        {
            "các",
            "dám",
            "đám",
            "dụm"
        };

        private static readonly string[] Patterns = BuildPatterns();

        private static readonly HashSet<string> SafeSet =
            new(SafeWords.Select(w => w.ToLowerInvariant()), StringComparer.Ordinal);

        /// <summary>Danh sách từ cấm gốc, dùng để hiển thị nếu cần (ví dụ trang quản trị).</summary>
        public static IReadOnlyList<string> BadWords => RawWords;

        /// <summary>
        /// Thay mọi từ cấm bằng "***". Giữ nguyên phần còn lại của câu, kể cả chữ hoa/thường.
        /// </summary>
        public static string Filter(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? "";

            string normalized = Normalize(text, out int[] map);
            if (normalized.Length == 0)
                return text;

            var marked = new bool[text.Length];
            bool any = false;

            foreach (string pattern in Patterns)
                MarkMatches(text, map, normalized, pattern, marked, ref any);

            if (!any)
                return text;

            var sb = new StringBuilder(text.Length);
            bool inBlock = false;

            for (int i = 0; i < text.Length; i++)
            {
                if (marked[i])
                {
                    // Gộp cả cụm từ bị chặn thành một "***" cho gọn
                    if (!inBlock)
                    {
                        sb.Append(Replacement);
                        inBlock = true;
                    }
                }
                else
                {
                    inBlock = false;
                    sb.Append(text[i]);
                }
            }

            return sb.ToString();
        }

        /// <summary>Câu có chứa từ cấm hay không.</summary>
        public static bool ContainsBadWord(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            string normalized = Normalize(text, out int[] map);
            if (normalized.Length == 0)
                return false;

            foreach (string pattern in Patterns)
            {
                for (int start = 0; start < normalized.Length; start++)
                {
                    if (!MatchAt(normalized, pattern, start, out int end))
                        continue;
                    if (!IsWholeWord(normalized, start, end))
                        continue;
                    if (IsSafe(text, map, start, end))
                        continue;

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Từ cấm đã bỏ dấu, sẵn sàng so khớp.
        /// Mẫu rỗng hoặc bắt đầu bằng ký tự đại diện bị loại bỏ, vì sẽ khớp lung tung.
        /// </summary>
        private static string[] BuildPatterns()
        {
            var list = new List<string>(RawWords.Length);

            foreach (string word in RawWords)
            {
                string pattern = RemoveDiacritics(word);
                if (pattern.Length == 0 || WildcardChars.Contains(pattern[0]))
                    continue;

                list.Add(pattern);
            }

            return list.ToArray();
        }

        /// <summary>
        /// Bỏ dấu tiếng Việt và hạ chữ thường, đồng thời quy "đ"/"Đ" về "d".
        /// map[k] là chỉ số trong chuỗi GỐC của ký tự đã bỏ dấu thứ k, dùng để ánh xạ ngược khi thay thế.
        /// Vì bỏ dấu làm chuỗi ngắn lại nên map là điều bắt buộc, không thể dùng trực tiếp chỉ số của
        /// chuỗi đã chuẩn hoá.
        /// </summary>
        private static string Normalize(string text, out int[] map)
        {
            var chars = new List<char>(text.Length);
            var indices = new List<int>(text.Length);
            var buffer = new StringBuilder(4);

            for (int i = 0; i < text.Length; i++)
            {
                buffer.Clear();
                buffer.Append(text[i].ToString().Normalize(NormalizationForm.FormD));

                foreach (char c in buffer.ToString())
                {
                    // Bỏ dấu phụ (dấu thanh, dấu mũ, dấu nặng...)
                    if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                        continue;

                    // Gán vào biến cục bộ: c là biến lặp của foreach nên không gán trực tiếp được
                    char ch = c == 'Đ' ? 'D' : c == 'đ' ? 'd' : c;

                    chars.Add(char.ToLowerInvariant(ch));
                    indices.Add(i);
                }
            }

            map = indices.ToArray();
            return new string(chars.ToArray());
        }

        /// <summary>Bỏ dấu và hạ chữ thường, dùng cho mẫu.</summary>
        private static string RemoveDiacritics(string text)
        {
            return Normalize(text, out _);
        }

        /// <summary>Đánh dấu các ký tự gốc thuộc mọi vị trí khớp mẫu.</summary>
        private static void MarkMatches(string text, int[] map, string normalized, string pattern, bool[] marked, ref bool any)
        {
            if (pattern.Length == 0)
                return;

            for (int start = 0; start < normalized.Length; start++)
            {
                if (!MatchAt(normalized, pattern, start, out int end))
                    continue;

                if (!IsWholeWord(normalized, start, end))
                    continue;

                if (IsSafe(text, map, start, end))
                    continue;

                int from = map[start];
                int to = map[end - 1];

                for (int i = from; i <= to; i++)
                {
                    if (!marked[i])
                    {
                        marked[i] = true;
                        any = true;
                    }
                }
            }
        }

        /// <summary>Ranh giới từ: ký tự liền trước và sau mẫu không được là chữ cái.</summary>
        private static bool IsWholeWord(string normalized, int start, int end)
        {
            if (start > 0 && char.IsLetter(normalized[start - 1]))
                return false;

            if (end < normalized.Length && char.IsLetter(normalized[end]))
                return false;

            return true;
        }

        /// <summary>
        /// Cụm từ vừa khớp có phải từ hợp lệ bị lẫn sau khi bỏ dấu không.
        /// Lấy đoạn từ chuỗi GỐC (còn dấu) rồi hạ chữ thường để so với SafeWords.
        /// Nhờ vậy phân biệt được "các" (hợp lệ) với "cặc" (cấm) dù cùng bỏ dấu ra "cac".
        /// </summary>
        private static bool IsSafe(string text, int[] map, int start, int end)
        {
            string span = text[map[start]..(map[end - 1] + 1)];
            return SafeSet.Contains(span.ToLowerInvariant());
        }

        /// <summary>So khớp mẫu tại vị trí start. end là vị trí ngay sau mẫu trong chuỗi đã chuẩn hoá.</summary>
        private static bool MatchAt(string normalized, string pattern, int start, out int end)
        {
            return Match(normalized, pattern, 0, start, out end);
        }

        private static bool Match(string normalized, string pattern, int ti, int si, out int end)
        {
            if (ti == pattern.Length)
            {
                end = si;
                return true;
            }

            char p = pattern[ti];

            if (WildcardChars.Contains(p))
            {
                // Ký tự đại diện khớp 0 ký tự
                if (Match(normalized, pattern, ti + 1, si, out end))
                    return true;

                // hoặc khớp đúng 1 ký tự
                if (si < normalized.Length && Match(normalized, pattern, ti + 1, si + 1, out end))
                    return true;

                end = si;
                return false;
            }

            if (si < normalized.Length && normalized[si] == p)
                return Match(normalized, pattern, ti + 1, si + 1, out end);

            end = si;
            return false;
        }
    }
}
