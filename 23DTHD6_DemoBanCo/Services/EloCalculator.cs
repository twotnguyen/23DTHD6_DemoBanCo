namespace _23DTHD6_DemoBanCo.Services
{
    /// <summary>
    /// Tính điểm Elo theo công thức FIDE, dùng chung cho cả xếp hạng ẩn danh lẫn phòng thi đấu xếp hạng.
    ///
    ///   E_A = 1 / (1 + 10^((R_B - R_A) / 400))     tỉ lệ thắng kỳ vọng của A
    ///   R'_A = R_A + K * (S_A - E_A)               điểm mới của A
    ///   R'_B = R_B + K * (S_B - E_B),  S_B = 1 - S_A
    ///
    /// Trong đó S là điểm thực tế: thắng = 1, hoà = 0.5, thua = 0.
    /// K là hệ số biến động, không đổi trong một cặp ván (lấy theo số ván đã đấu của từng bên).
    /// </summary>
    public static class EloCalculator
    {
        public const int StartingElo = 1200;

        /// <summary>Bậc xếp hạng hiển thị cho người chơi.</summary>
        public enum RankTier
        {
            Novice,
            Junior,
            Intermediate,
            Advanced,
            Master,
            Grandmaster
        }

        /// <summary>
        /// Hệ số K: 32 khi mới chơi (tối đa 30 ván), từ ván thứ 31 trở đi giảm còn 16.
        /// </summary>
        public static int KFactor(int gamesPlayed)
        {
            return gamesPlayed <= 30 ? 32 : 16;
        }

        /// <summary>Tỉ lệ thắng kỳ vọng của bên A trước ván đấu, nằm trong khoảng (0, 1).</summary>
        public static double ExpectedScore(int ratingA, int ratingB)
        {
            return 1.0 / (1.0 + Math.Pow(10.0, (ratingB - ratingA) / 400.0));
        }

        /// <summary>
        /// Trả về điểm mới của cả hai bên sau một ván.
        /// scoreA là điểm thực tế của A: thắng 1.0, hoà 0.5, thua 0.0.
        /// gamesA / gamesB là số ván đã đấu trước đó, quyết định hệ số K của từng bên.
        /// </summary>
        public static (int newA, int newB) ApplyResult(int ratingA, int ratingB, double scoreA, int gamesA, int gamesB)
        {
            double scoreB = 1.0 - scoreA;

            double expectedA = ExpectedScore(ratingA, ratingB);
            double expectedB = ExpectedScore(ratingB, ratingA);

            double newA = ratingA + KFactor(gamesA) * (scoreA - expectedA);
            double newB = ratingB + KFactor(gamesB) * (scoreB - expectedB);

            // Công thức thuần FIDE, không giới hạn trần/sàn
            return ((int)Math.Round(newA), (int)Math.Round(newB));
        }

        /// <summary>Xếp bậc theo điểm.</summary>
        public static RankTier GetTier(int elo)
        {
            if (elo < 1200) return RankTier.Novice;
            if (elo < 1400) return RankTier.Junior;
            if (elo < 1600) return RankTier.Intermediate;
            if (elo < 1800) return RankTier.Advanced;
            if (elo < 2000) return RankTier.Master;
            return RankTier.Grandmaster;
        }

        /// <summary>Tên bậc tiếng Việt để hiển thị.</summary>
        public static string GetTierName(RankTier tier)
        {
            return tier switch
            {
                RankTier.Novice => "Kỳ thủ mới",
                RankTier.Junior => "Sơ cấp",
                RankTier.Intermediate => "Trung cấp",
                RankTier.Advanced => "Cao cấp",
                RankTier.Master => "Kiện tướng",
                RankTier.Grandmaster => "Đại sư",
                _ => "Kỳ thủ mới"
            };
        }
    }
}
