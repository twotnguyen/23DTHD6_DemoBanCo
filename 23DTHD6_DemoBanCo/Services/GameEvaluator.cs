using _23DTHD6_DemoBanCo.Models;

namespace _23DTHD6_DemoBanCo.Services
{
    /// <summary>
    /// Đọc/ghi thế cờ (FEN), ký hiệu nước đi và phân định kết quả ván.
    /// Đây là cổng duy nhất mà phần còn lại của ứng dụng dùng để nói chuyện về
    /// luật cờ, để chỗ tính kết quả ván chỉ có một nơi.
    /// Không quyết định lý do nằm ngoài bàn cờ (đầu hàng, hết giờ, mất kết nối):
    /// những lý do đó do MatchService chốt vì chỉ nó biết bối cảnh ván đấu.
    /// </summary>
    public static class GameEvaluator
    {
        /// <summary>Số lần lặp thế cờ bị tính là hoà.</summary>
        public const int RepetitionLimit = 3;

        /// <summary>Thế cờ khởi tạo, đúng chuẩn FEN Xiangqi 6 trường.</summary>
        public const string InitialFen =
            "rnbakabnr/9/1c5c1/p1p1p1p1p/9/9/P1P1P1P1P/1C5C1/9/RNBAKABNR w - - 0 1";

        // =========================================
        // FEN
        // =========================================

        /// <summary>
        /// Dựng bàn cờ từ chuỗi FEN Xiangqi.
        /// Chấp nhận cả bản đầy đủ 6 trường ("... w - - 0 1") lẫn bản chỉ có thế cờ
        /// ("rnbakabnr/9/.../RNBAKABNR"): phần sau dấu cách đầu tiên bị bỏ qua.
        /// Sai định dạng thì ném FormatException.
        /// </summary>
        public static ChessBoard FromFen(string fen)
        {
            if (string.IsNullOrWhiteSpace(fen))
                throw new ArgumentException("Chuỗi FEN rỗng.", nameof(fen));

            string position = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).First();
            string[] rankFields = position.Split('/', StringSplitOptions.None);

            if (rankFields.Length != ChessRules.Rows)
                throw new FormatException($"FEN phải có {ChessRules.Rows} hàng, nhận được {rankFields.Length}.");

            var board = new ChessBoard();

            for (int row = 0; row < ChessRules.Rows; row++)
            {
                int col = 0;

                foreach (char c in rankFields[row])
                {
                    // Số 1..9 là số ô trống liên tiếp
                    if (c >= '1' && c <= '9')
                    {
                        col += c - '0';
                        continue;
                    }

                    if (!TryPieceFromFenChar(c, out var type, out var color))
                        throw new FormatException($"Ký tự FEN không hợp lệ: '{c}'.");

                    if (!ChessRules.IsInsideBoard(row, col))
                        throw new FormatException($"Hàng {row} của FEN dài quá {ChessRules.Cols} ô.");

                    board[row, col] = new ChessPiece(type, color, row, col);
                    col++;
                }

                if (col != ChessRules.Cols)
                    throw new FormatException($"Hàng {row} của FEN phải đúng {ChessRules.Cols} ô, nhận được {col}.");
            }

            return board;
        }

        /// <summary>
        /// Xuất phần thế cờ của FEN: 10 hàng cách nhau bằng "/", số ô trống được gộp
        /// (ví dụ "rnbakabnr/9/1c5c1/..."). Đây cũng là định dạng client dùng để vẽ bàn,
        /// nên thứ tự hàng giữ nguyên: hàng đầu tiên là phía quân đen.
        /// </summary>
        public static string ToFen(ChessBoard board)
        {
            var rows = new string[ChessRules.Rows];

            for (int row = 0; row < ChessRules.Rows; row++)
            {
                var builder = new System.Text.StringBuilder();
                int empty = 0;

                for (int col = 0; col < ChessRules.Cols; col++)
                {
                    var piece = board[row, col];

                    if (piece == null)
                    {
                        empty++;
                        continue;
                    }

                    if (empty > 0)
                    {
                        builder.Append(empty);
                        empty = 0;
                    }

                    builder.Append(ToFenChar(piece));
                }

                if (empty > 0)
                    builder.Append(empty);

                rows[row] = builder.ToString();
            }

            return string.Join("/", rows);
        }

        /// <summary>
        /// Bản FEN đầy đủ 6 trường, dùng để lưu vĩnh viễn và làm khoá so sánh lặp thế.
        /// Ba trường sau lượt đi luôn là "w - - 0 1" vì bàn cờ không lưu đồng hồ hay số nước đi,
        /// nhờ vậy hai thế cờ giống nhau luôn cho ra cùng một khoá.
        /// </summary>
        public static string ToFen(ChessBoard board, Side sideToMove)
        {
            string side = sideToMove == Side.Red ? "w" : "b";
            return $"{ToFen(board)} {side} - - 0 1";
        }

        /// <summary>
        /// Quy đổi quân sang ký hiệu FEN. Theo chuẩn Xiangqi: K tướng, A sĩ,
        /// B tượng, N mã, R xe, C pháo, P tốt. Chữ hoa là phe ĐỎ, chữ thường là phe ĐEN.
        /// </summary>
        private static char ToFenChar(ChessPiece piece)
        {
            char letter = piece.Type switch
            {
                PieceType.King => 'K',
                PieceType.Advisor => 'A',
                PieceType.Elephant => 'B',
                PieceType.Horse => 'N',
                PieceType.Chariot => 'R',
                PieceType.Cannon => 'C',
                PieceType.Soldier => 'P',
                _ => '?'
            };

            return piece.Color == Side.Red ? letter : char.ToLowerInvariant(letter);
        }

        private static bool TryPieceFromFenChar(char c, out PieceType type, out Side color)
        {
            color = char.IsUpper(c) ? Side.Red : Side.Black;

            switch (char.ToUpperInvariant(c))
            {
                case 'K': type = PieceType.King; return true;
                case 'A': type = PieceType.Advisor; return true;
                case 'B': type = PieceType.Elephant; return true;
                case 'N': type = PieceType.Horse; return true;
                case 'R': type = PieceType.Chariot; return true;
                case 'C': type = PieceType.Cannon; return true;
                case 'P': type = PieceType.Soldier; return true;
                default: type = default; return false;
            }
        }

        // =========================================
        // Ký hiệu nước đi
        // =========================================

        /// <summary>
        /// Ký hiệu nước đi kiểu đại số quốc tế của cờ tướng (ICCS): chữ loại quân +
        /// cột xuất phát, ví dụ "R1a3" (xe cột a dọc lên hàng 3), "H2e2" (mã từ
        /// cột c2 sang e2), "C5c7" (pháo đi ngang). Nếu quân không đổi cột thì
        /// không có dấu gạch nối. Hàm chỉ ĐỌC bàn cờ, không áp dụng nước đi:
        /// gọi trước ApplyMove, vì ký hiệu mô tả nước đi trên thế cờ cũ.
        /// </summary>
        public static string ToSan(ChessBoard board, Move move)
        {
            char pieceLetter = move.Piece.Type switch
            {
                PieceType.King => 'K',
                PieceType.Advisor => 'A',
                PieceType.Elephant => 'B',
                PieceType.Horse => 'H',   // H = horse trong cách viết tiếng Anh
                PieceType.Chariot => 'R',
                PieceType.Cannon => 'C',
                PieceType.Soldier => 'P',
                _ => '?'
            };

            char fromFile = (char)('a' + move.FromCol);

            // Cùng cột: ghi luôn số thứ tự hàng đích (hàng lưng đỏ là 1, hàng lưng đen là 9)
            if (move.FromCol == move.ToCol)
                return $"{pieceLetter}{fromFile}{ChessRules.Rows - move.ToRow}";

            return $"{pieceLetter}{fromFile}{(char)('a' + move.ToCol)}";
        }

        // =========================================
        // Kết quả ván
        // =========================================

        /// <summary>
        /// Kết quả tính từ bàn cờ thuần, xét cho bên sắp đi.
        /// Hết nước đi và đang bị chiếu -> Checkmate; hết nước đi mà không bị chiếu ->
        /// Stalemate (luật cờ tướng xử thua); còn lại -> None.
        /// </summary>
        public static MatchEndReason EvaluateGameEnd(ChessBoard board, Side sideToMove)
        {
            if (ChessRules.GetLegalMoves(board, sideToMove).Count > 0)
                return MatchEndReason.None;

            return CheckDetector.IsInCheck(board, sideToMove)
                ? MatchEndReason.Checkmate
                : MatchEndReason.Stalemate;
        }

        /// <summary>
        /// Thế cờ đã lặp đủ số lần quy định chưa.
        /// Danh sách truyền vào phải ĐÃ bao gồm cả thế cờ hiện tại ở lần xuất hiện cuối.
        /// </summary>
        public static bool IsRepetitionDraw(IEnumerable<string> fenHistory)
        {
            string? latest = null;

            foreach (var fen in fenHistory)
                latest = fen;

            if (latest == null)
                return false;

            int count = 0;

            foreach (var fen in fenHistory)
            {
                if (string.Equals(fen, latest, StringComparison.Ordinal))
                    count++;
            }

            return count >= RepetitionLimit;
        }

        /// <summary>
        /// Phe thắng khi bên đang đi không còn nước đi, hoặc null nếu ván chưa xong vì bàn cờ.
        /// Cả chiếu hết và vây khốn đều là bên đang đi thua, chỉ khác ở lý do kết thúc.
        /// </summary>
        public static Side? GetWinner(ChessBoard board, Side sideToMove)
        {
            if (ChessRules.GetLegalMoves(board, sideToMove).Count > 0)
                return null;

            return CheckDetector.Opposite(sideToMove);
        }
    }
}
