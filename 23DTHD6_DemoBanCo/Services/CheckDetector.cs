namespace _23DTHD6_DemoBanCo.Services
{
    /// <summary>
    /// Phát hiện chiếu tướng và thế thua. Mọi kiểm tra đều đi qua đây để
    /// luật chiếu tướng chỉ có một nguồn sự thật.
    /// </summary>
    public static class CheckDetector
    {
        /// <summary>Tướng của phe này có đang bị chiếu không.</summary>
        public static bool IsInCheck(ChessBoard board, Side side)
        {
            var king = board.FindKing(side);

            // Không còn tướng thì coi như đã thua: mọi nước đi đều bất hợp lệ.
            if (king == null)
                return true;

            return IsSquareAttacked(board, king.Row, king.Col, Opposite(side));
        }

        /// <summary>Vị trí tướng đang bị chiếu, hoặc null nếu không bị chiếu / không còn tướng.</summary>
        public static ChessPiece? GetCheckedKing(ChessBoard board, Side side)
        {
            var king = board.FindKing(side);

            if (king == null)
                return null;

            return IsSquareAttacked(board, king.Row, king.Col, Opposite(side)) ? king : null;
        }

        public static Side Opposite(Side side) => side == Side.Red ? Side.Black : Side.Red;

        /// <summary>Ô (row,col) có bị quân phe attacker tấn công không.</summary>
        public static bool IsSquareAttacked(ChessBoard board, int row, int col, Side attacker)
        {
            foreach (var piece in board.Pieces())
            {
                if (piece.Color != attacker)
                    continue;

                if (AttacksSquare(board, piece, row, col))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Quân này có tấn công được ô đích không (bỏ qua ràng buộc tướng mình).
        /// Không cần xét tới việc quân đích có phải tướng hay không.
        /// </summary>
        private static bool AttacksSquare(ChessBoard board, ChessPiece piece, int row, int col)
        {
            switch (piece.Type)
            {
                case PieceType.Chariot:
                    return IsOnSameLine(piece.Row, piece.Col, row, col)
                        && ChessRules.CountBetween(board, piece.Row, piece.Col, row, col) == 0;

                case PieceType.Cannon:
                    // Pháo chỉ tấn công được khi có đúng 1 màn chắn giữa nó và ô đích.
                    return IsOnSameLine(piece.Row, piece.Col, row, col)
                        && ChessRules.CountBetween(board, piece.Row, piece.Col, row, col) == 1;

                case PieceType.Horse:
                    return HorseAttacks(board, piece, row, col);

                case PieceType.Elephant:
                    return ElephantAttacks(piece, row, col);

                case PieceType.Advisor:
                    return AdvisorAttacks(piece, row, col);

                case PieceType.King:
                    return KingAttacks(piece, row, col);

                case PieceType.Soldier:
                    return SoldierAttacks(piece, row, col);

                default:
                    return false;
            }
        }

        private static bool IsOnSameLine(int r1, int c1, int r2, int c2)
        {
            return r1 == r2 || c1 == c2;
        }

        /// <summary>
        /// Mã tấn công ô đích khi đi hình chữ L VÀ ô chân không bị chặn.
        /// Chặn chân là kiểu hình "chân trước" của cờ tướng, khác với kiểu hình ngựa của cờ vua.
        /// </summary>
        private static bool HorseAttacks(ChessBoard board, ChessPiece horse, int row, int col)
        {
            (int dr, int dc, int legR, int legC)[] steps =
            {
                (-2, -1, -1,  0), (-2,  1, -1,  0), ( 2, -1,  1,  0), ( 2,  1,  1,  0),
                (-1, -2,  0, -1), ( 1, -2,  0, -1), (-1,  2,  0,  1), ( 1,  2,  0,  1)
            };

            foreach (var (dr, dc, legR, legC) in steps)
            {
                if (horse.Row + dr != row || horse.Col + dc != col)
                    continue;

                int legRow = horse.Row + legR;
                int legCol = horse.Col + legC;

                if (ChessRules.IsInsideBoard(legRow, legCol) && board[legRow, legCol] != null)
                    return false;

                return true;
            }

            return false;
        }

        /// <summary>Tượng đánh chéo 2 ô; điểm đích phải nằm cùng bờ sông với tượng.</summary>
        private static bool ElephantAttacks(ChessPiece elephant, int row, int col)
        {
            (int dr, int dc)[] steps = { (-2, -2), (-2, 2), (2, -2), (2, 2) };

            foreach (var (dr, dc) in steps)
            {
                if (elephant.Row + dr != row || elephant.Col + dc != col)
                    continue;

                // Tượng không đánh được sang bờ sông của đối phương.
                bool acrossRiver = elephant.Color == Side.Black
                    ? row >= 5
                    : row <= 4;

                return !acrossRiver;
            }

            return false;
        }

        /// <summary>Sĩ đánh chéo 1 ô, luôn nằm trong cung.</summary>
        private static bool AdvisorAttacks(ChessPiece advisor, int row, int col)
        {
            (int dr, int dc)[] steps = { (-1, -1), (-1, 1), (1, -1), (1, 1) };

            foreach (var (dr, dc) in steps)
            {
                if (advisor.Row + dr == row && advisor.Col + dc == col)
                    return ChessRules.InPalace(advisor.Color, row, col);
            }

            return false;
        }

        /// <summary>Tướng đánh thẳng 1 ô, luôn nằm trong cung.</summary>
        private static bool KingAttacks(ChessPiece king, int row, int col)
        {
            (int dr, int dc)[] steps = { (-1, 0), (1, 0), (0, -1), (0, 1) };

            foreach (var (dr, dc) in steps)
            {
                if (king.Row + dr == row && king.Col + dc == col)
                    return true;
            }

            return false;
        }

        /// <summary>Tốt chỉ đi thẳng về phía trước, thêm ngang khi đã qua sông. Không lùi.</summary>
        private static bool SoldierAttacks(ChessPiece soldier, int row, int col)
        {
            int forward = soldier.Color == Side.Red ? -1 : 1;

            if (row == soldier.Row + forward && col == soldier.Col)
                return true;

            // Đã qua sông thì đánh được sang ngang
            bool crossed = soldier.Color == Side.Red ? soldier.Row <= 4 : soldier.Row >= 5;
            if (crossed && row == soldier.Row && Math.Abs(col - soldier.Col) == 1)
                return true;

            return false;
        }

        /// <summary>
        /// Hai tướng đứng cùng cột và không có quân nào chắn giữa.
        /// Đây là luật "phi hành quân" của cờ tướng.
        /// </summary>
        public static bool KingsFaceEachOther(ChessBoard board)
        {
            var redKing = board.FindKing(Side.Red);
            var blackKing = board.FindKing(Side.Black);

            if (redKing == null || blackKing == null)
                return false;

            if (redKing.Col != blackKing.Col)
                return false;

            if (redKing.Row == blackKing.Row)
                return true;

            return ChessRules.CountBetween(board, redKing.Row, redKing.Col, blackKing.Row, blackKing.Col) == 0;
        }

        /// <summary>
        /// Kiểm tra nước đi đầy đủ: đúng luật quân, không để tướng mình bị chiếu,
        /// không để hai tướng gặp nhau. Đây là cổng duy nhất mọi nước đi phải qua.
        /// </summary>
        public static bool IsLegalMoveFull(ChessBoard board, Move move)
        {
            if (!ChessRules.IsPseudoLegalMove(board, move))
                return false;

            // Không được bắt tướng đối phương trực tiếp bằng nước đi thường
            var captured = board[move.ToRow, move.ToCol];
            if (captured != null && captured.Type == PieceType.King)
                return false;

            var undo = ChessRules.ApplyMove(board, move);

            // ăn quân chặn chiếu tướng cũng phải được tính sau khi đi
            bool illegal = IsInCheck(board, move.Piece.Color) || KingsFaceEachOther(board);

            ChessRules.UndoMove(board, undo);

            return !illegal;
        }

        /// <summary>Bên này đã bị chiếu hết: đang bị chiếu và không còn nước đi nào cứu được.</summary>
        public static bool IsCheckmate(ChessBoard board, Side side)
        {
            if (!IsInCheck(board, side))
                return false;

            return ChessRules.GetLegalMoves(board, side).Count == 0;
        }

        /// <summary>Bên này bị vây khốn: không bị chiếu nhưng hết nước đi. Theo luật xử thua.</summary>
        public static bool IsStalemate(ChessBoard board, Side side)
        {
            if (IsInCheck(board, side))
                return false;

            return ChessRules.GetLegalMoves(board, side).Count == 0;
        }
    }
}
