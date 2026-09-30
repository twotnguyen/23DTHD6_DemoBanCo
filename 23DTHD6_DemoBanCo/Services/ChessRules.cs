namespace _23DTHD6_DemoBanCo.Services
{
    /// <summary>Phe quân. Đỏ đi trước, đen ở phía trên bàn cờ.</summary>
    public enum Side
    {
        Red = 0,
        Black = 1
    }

    /// <summary>Các loại quân cờ tướng.</summary>
    public enum PieceType
    {
        King,      // Tướng (tướng đỏ và tướng đen đều là King, phân biệt bằng Color)
        Advisor,   // Sĩ
        Elephant,  // Tượng
        Horse,     // Mã
        Chariot,   // Xe
        Cannon,    // Pháo
        Soldier    // Tốt
    }

    /// <summary>Một quân cờ trên bàn.</summary>
    public class ChessPiece
    {
        public PieceType Type { get; }

        public Side Color { get; }

        /// <summary>Vị trí hiện tại trên bàn. Cập nhật khi quân di chuyển.</summary>
        public int Row { get; set; }

        public int Col { get; set; }

        /// <summary>
        /// Định danh ổn định của quân: giữ nguyên suốt ván đấu để lưu lịch sử nước đi.
        /// Sinh từ loại quân, phe và ô xuất phát.
        /// </summary>
        public string Id { get; }

        public ChessPiece(PieceType type, Side color, int row, int col)
            : this(type, color, row, col, BuildId(type, color, row, col))
        {
        }

        public ChessPiece(PieceType type, Side color, int row, int col, string id)
        {
            Type = type;
            Color = color;
            Row = row;
            Col = col;
            Id = id;
        }

        private static string BuildId(PieceType type, Side color, int row, int col)
            => $"{type.ToString().ToLowerInvariant()}_{color.ToString().ToLowerInvariant()}_{row}_{col}";

        public ChessPiece Clone() => new ChessPiece(Type, Color, Row, Col, Id);
    }

    /// <summary>Một nước đi của engine. Quân thật được tra ra từ bàn cờ, không tạo bản sao.</summary>
    public class Move
    {
        public ChessPiece Piece { get; }

        public int FromRow { get; }

        public int FromCol { get; }

        public int ToRow { get; }

        public int ToCol { get; }

        public Move(ChessPiece piece, int fromRow, int fromCol, int toRow, int toCol)
        {
            Piece = piece;
            FromRow = fromRow;
            FromCol = fromCol;
            ToRow = toRow;
            ToCol = toCol;
        }

        public string PieceId => Piece.Id;

        /// <summary>Nước đi này có ăn quân ở ô đích không (xét trên bàn cờ hiện tại).</summary>
        public bool IsCapture(ChessBoard board) => board[ToRow, ToCol] != null;

        public override string ToString() =>
            $"{Piece.Type}({Piece.Color}) {FromRow},{FromCol} -> {ToRow},{ToCol}";
    }

    /// <summary>Thông tin quân bị ăn và vị trí cũ, dùng để hoàn tác nước đi.</summary>
    public class MoveUndo
    {
        public ChessPiece? CapturedPiece { get; init; }

        /// <summary>Ô quân vừa đi tới.</summary>
        public int ToRow { get; init; }

        public int ToCol { get; init; }

        /// <summary>Ô xuất phát, để trả quân về chỗ cũ.</summary>
        public int FromRow { get; init; }

        public int FromCol { get; init; }
    }

    /// <summary>
    /// Bàn cờ cờ tướng 10 hàng x 9 cột. Dùng mảng phẳng 90 ô cho phép clone rẻ.
    /// Quy ước toạ độ: hàng 0 là hàng lưng của phe ĐEN, hàng 9 là hàng lưng của phe ĐỎ.
    /// Cột 0 nằm bên trái khi nhìn từ phía phe đen.
    /// </summary>
    public class ChessBoard
    {
        public const int RowCount = 10;
        public const int ColCount = 9;

        private readonly ChessPiece?[] _cells = new ChessPiece?[RowCount * ColCount];

        public int Rows => RowCount;

        public int Cols => ColCount;

        public ChessPiece? this[int row, int col]
        {
            get
            {
                if (!IsOnBoard(row, col))
                    throw new ArgumentOutOfRangeException(
                        $"{nameof(row)},{nameof(col)}", $"Ô ({row},{col}) nằm ngoài bàn cờ.");

                return _cells[row * ColCount + col];
            }
            set
            {
                if (!IsOnBoard(row, col))
                    throw new ArgumentOutOfRangeException(
                        $"{nameof(row)},{nameof(col)}", $"Ô ({row},{col}) nằm ngoài bàn cờ.");

                _cells[row * ColCount + col] = value;
            }
        }

        private static bool IsOnBoard(int row, int col)
            => row >= 0 && row < RowCount && col >= 0 && col < ColCount;

        public ChessBoard Clone()
        {
            var copy = new ChessBoard();
            for (int i = 0; i < _cells.Length; i++)
                copy._cells[i] = _cells[i]?.Clone();

            return copy;
        }

        public IEnumerable<ChessPiece> Pieces()
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i] != null)
                    yield return _cells[i]!;
            }
        }

        public ChessPiece? FindKing(Side color)
        {
            foreach (var p in Pieces())
            {
                if (p.Type == PieceType.King && p.Color == color)
                    return p;
            }

            return null;
        }

        public bool HasSide(Side color)
        {
            foreach (var p in Pieces())
            {
                if (p.Color == color)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Luật di chuyển của từng quân cờ tướng.
    /// Đọc/ghi FEN và ký hiệu nước đi nằm ở GameEvaluator để chỉ có một cách quy đổi.
    /// Không chạm database, không phụ thuộc ASP.NET.
    /// </summary>
    public static class ChessRules
    {
        public const int Rows = ChessBoard.RowCount;   // 10
        public const int Cols = ChessBoard.ColCount;   // 9

        /// <summary>Sông nằm giữa hàng 4 và hàng 5, đánh số từ 0.</summary>
        private const int RiverTop = 4;

        private const int RiverBottom = 5;

        public static bool IsInsideBoard(int row, int col)
        {
            return row >= 0 && row < Rows && col >= 0 && col < Cols;
        }

        /// <summary>Quân này đã qua sông chưa (tốt đỏ ở hàng &lt;= 4, tốt đen ở hàng &gt;= 5).</summary>
        public static bool HasCrossedRiver(ChessPiece piece)
        {
            return piece.Color == Side.Red ? piece.Row <= RiverTop : piece.Row >= RiverBottom;
        }

        /// <summary>
        /// Cung của tướng: cột 3..5, đen ở hàng 0..2, đỏ ở hàng 7..9.
        /// Tướng và sĩ không bao giờ rời cung.
        /// </summary>
        public static bool InPalace(Side color, int row, int col)
        {
            if (col < 3 || col > 5)
                return false;

            return color == Side.Black ? row >= 0 && row <= 2 : row >= 7 && row <= 9;
        }

        /// <summary>Tạo bàn cờ với 32 quân đúng vị trí chuẩn cờ tướng.</summary>
        public static ChessBoard CreateInitialBoard()
        {
            var board = new ChessBoard();

            // ---------- Hàng 0: quân đen ----------
            PlaceRow(board, 0, new[] { PieceType.Chariot, PieceType.Horse, PieceType.Elephant,
                                      PieceType.Advisor, PieceType.King, PieceType.Advisor,
                                      PieceType.Elephant, PieceType.Horse, PieceType.Chariot }, Side.Black);

            // ---------- Hàng 2: pháo đen ----------
            board[2, 1] = new ChessPiece(PieceType.Cannon, Side.Black, 2, 1);
            board[2, 7] = new ChessPiece(PieceType.Cannon, Side.Black, 2, 7);

            // ---------- Hàng 3: tốt đen ----------
            for (int col = 0; col < Cols; col += 2)
                board[3, col] = new ChessPiece(PieceType.Soldier, Side.Black, 3, col);

            // ---------- Hàng 6: tốt đỏ ----------
            for (int col = 0; col < Cols; col += 2)
                board[6, col] = new ChessPiece(PieceType.Soldier, Side.Red, 6, col);

            // ---------- Hàng 7: pháo đỏ ----------
            board[7, 1] = new ChessPiece(PieceType.Cannon, Side.Red, 7, 1);
            board[7, 7] = new ChessPiece(PieceType.Cannon, Side.Red, 7, 7);

            // ---------- Hàng 9: quân đỏ ----------
            PlaceRow(board, 9, new[] { PieceType.Chariot, PieceType.Horse, PieceType.Elephant,
                                      PieceType.Advisor, PieceType.King, PieceType.Advisor,
                                      PieceType.Elephant, PieceType.Horse, PieceType.Chariot }, Side.Red);

            return board;
        }

        private static void PlaceRow(ChessBoard board, int row, PieceType[] types, Side color)
        {
            for (int col = 0; col < Cols && col < types.Length; col++)
                board[row, col] = new ChessPiece(types[col], color, row, col);
        }

        // =========================================
        // Luật di chuyển
        // =========================================

        /// <summary>
        /// Đếm số quân chắn giữa hai ô trên cùng đường thẳng (cùng hàng hoặc cùng cột).
        /// Trả về -1 nếu hai ô không thẳng hàng. CheckDetector dùng hàm này để xác định
        /// quân có tấn công được ô đích hay không.
        /// </summary>
        public static int CountBetween(ChessBoard board, int row1, int col1, int row2, int col2)
        {
            if (row1 != row2 && col1 != col2)
                return -1;

            int dr = Math.Sign(row2 - row1);
            int dc = Math.Sign(col2 - col1);

            int count = 0;
            int r = row1 + dr;
            int c = col1 + dc;

            while (r != row2 || c != col2)
            {
                if (board[r, c] != null)
                    count++;

                r += dr;
                c += dc;
            }

            return count;
        }

        /// <summary>Danh sách nước đi đúng luật quân, CHƯA kiểm tra chiếu tướng.</summary>
        public static List<Move> GetPseudoLegalMoves(ChessBoard board, Side side)
        {
            var moves = new List<Move>();

            foreach (var piece in board.Pieces())
            {
                if (piece.Color != side)
                    continue;

                switch (piece.Type)
                {
                    case PieceType.Chariot:
                        moves.AddRange(ChariotMoves(board, piece));
                        break;
                    case PieceType.Cannon:
                        // Pháo đi như xe khi không ăn, và chỉ ăn được sau đúng 1 màn chắn
                        moves.AddRange(CannonQuietMoves(board, piece));
                        moves.AddRange(CannonCaptureMoves(board, piece));
                        break;
                    case PieceType.Horse:
                        moves.AddRange(HorseMoves(board, piece));
                        break;
                    case PieceType.Elephant:
                        moves.AddRange(ElephantMoves(board, piece));
                        break;
                    case PieceType.Advisor:
                        moves.AddRange(AdvisorMoves(board, piece));
                        break;
                    case PieceType.King:
                        moves.AddRange(KingMoves(board, piece));
                        break;
                    case PieceType.Soldier:
                        moves.AddRange(SoldierMoves(board, piece));
                        break;
                }
            }

            return moves;
        }

        /// <summary>Nước đi hợp lệ theo luật quân, chưa xét chiếu tướng.</summary>
        public static bool IsPseudoLegalMove(ChessBoard board, Move move)
        {
            if (!IsInsideBoard(move.FromRow, move.FromCol) ||
                !IsInsideBoard(move.ToRow, move.ToCol))
                return false;

            var piece = board[move.FromRow, move.FromCol];
            if (piece == null)
                return false;

            // Không được đi vào ô của chính mình, cũng không đứng yên
            if (move.FromRow == move.ToRow && move.FromCol == move.ToCol)
                return false;

            var target = board[move.ToRow, move.ToCol];
            if (target != null && target.Color == piece.Color)
                return false;

            foreach (var m in GetPseudoLegalMoves(board, piece.Color))
            {
                if (m.FromRow == move.FromRow && m.FromCol == move.FromCol
                    && m.ToRow == move.ToRow && m.ToCol == move.ToCol)
                    return true;
            }

            return false;
        }

        private static bool CanLand(ChessBoard board, ChessPiece piece, int row, int col)
        {
            var target = board[row, col];
            return target == null || target.Color != piece.Color;
        }

        /// <summary>Xe đi thẳng bất kỳ số ô, không qua quân, ăn quân đối phương đầu tiên gặp.</summary>
        private static List<Move> ChariotMoves(ChessBoard board, ChessPiece chariot)
        {
            var moves = new List<Move>();

            foreach (var (dr, dc) in Directions)
            {
                int r = chariot.Row + dr;
                int c = chariot.Col + dc;

                while (IsInsideBoard(r, c))
                {
                    var occupant = board[r, c];

                    if (occupant == null)
                    {
                        moves.Add(new Move(chariot, chariot.Row, chariot.Col, r, c));
                    }
                    else
                    {
                        if (occupant.Color != chariot.Color)
                            moves.Add(new Move(chariot, chariot.Row, chariot.Col, r, c));

                        break;
                    }

                    r += dr;
                    c += dc;
                }
            }

            return moves;
        }

        /// <summary>
        /// Pháo đi như xe khi KHÔNG ăn: trượt qua ô trống và DỪNG trước quân đầu tiên,
        /// vì pháo không ăn trực tiếp quân nó chạm phải.
        /// </summary>
        private static List<Move> CannonQuietMoves(ChessBoard board, ChessPiece cannon)
        {
            var moves = new List<Move>();

            foreach (var (dr, dc) in Directions)
            {
                int r = cannon.Row + dr;
                int c = cannon.Col + dc;

                while (IsInsideBoard(r, c))
                {
                    // Gặp quân đầu tiên thì hết đường đi thường, kể cả quân đối phương
                    if (board[r, c] != null)
                        break;

                    moves.Add(new Move(cannon, cannon.Row, cannon.Col, r, c));

                    r += dr;
                    c += dc;
                }
            }

            return moves;
        }

        /// <summary>
        /// Pháo ĂN: trên đường đi phải có đúng MỘT màn chắn, quân đầu tiên gặp sau
        /// màn chắn đó mới là quân bị ăn. Có hai màn chắn thì không ăn được.
        /// </summary>
        private static List<Move> CannonCaptureMoves(ChessBoard board, ChessPiece cannon)
        {
            var moves = new List<Move>();

            foreach (var (dr, dc) in Directions)
            {
                int r = cannon.Row + dr;
                int c = cannon.Col + dc;

                // Bước 1: quét tới màn chắn đầu tiên
                while (IsInsideBoard(r, c) && board[r, c] == null)
                {
                    r += dr;
                    c += dc;
                }

                if (!IsInsideBoard(r, c))
                    continue;

                // Bước 2: bỏ qua màn chắn, quân kế tiếp là quân bị ăn
                r += dr;
                c += dc;

                while (IsInsideBoard(r, c) && board[r, c] == null)
                {
                    r += dr;
                    c += dc;
                }

                if (!IsInsideBoard(r, c))
                    continue;

                var target = board[r, c];
                if (target!.Color != cannon.Color)
                    moves.Add(new Move(cannon, cannon.Row, cannon.Col, r, c));
            }

            return moves;
        }

        /// <summary>
        /// Mã đi hình chữ L (1 thẳng + 1 chéo). Chân bị chặn thì mất hẳn hướng đó:
        /// mà trống nhưng ô đích có quân thì vẫn không đi được.
        /// </summary>
        private static List<Move> HorseMoves(ChessBoard board, ChessPiece horse)
        {
            var moves = new List<Move>();

            foreach (var (dr, dc, legR, legC) in HorseSteps)
            {
                int legRow = horse.Row + legR;
                int legCol = horse.Col + legC;

                if (IsInsideBoard(legRow, legCol) && board[legRow, legCol] != null)
                    continue;

                int toRow = horse.Row + dr;
                int toCol = horse.Col + dc;

                if (!IsInsideBoard(toRow, toCol))
                    continue;

                if (!CanLand(board, horse, toRow, toCol))
                    continue;

                moves.Add(new Move(horse, horse.Row, horse.Col, toRow, toCol));
            }

            return moves;
        }

        /// <summary>
        /// Tượng đi chéo 2 ô. Mắt tượng (ô chính giữa) có quân thì không nhảy được,
        /// và tượng không bao giờ qua sông: tượng đen kẹt ở hàng 0..4, tượng đỏ ở hàng 5..9.
        /// </summary>
        private static List<Move> ElephantMoves(ChessBoard board, ChessPiece elephant)
        {
            var moves = new List<Move>();

            foreach (var (dr, dc) in ElephantSteps)
            {
                int toRow = elephant.Row + dr;
                int toCol = elephant.Col + dc;

                if (!IsInsideBoard(toRow, toCol))
                    continue;

                bool acrossRiver = elephant.Color == Side.Black
                    ? toRow >= RiverBottom
                    : toRow <= RiverTop;

                if (acrossRiver)
                    continue;

                // Mắt tượng phải trống
                int eyeRow = elephant.Row + dr / 2;
                int eyeCol = elephant.Col + dc / 2;

                if (board[eyeRow, eyeCol] != null)
                    continue;

                if (!CanLand(board, elephant, toRow, toCol))
                    continue;

                moves.Add(new Move(elephant, elephant.Row, elephant.Col, toRow, toCol));
            }

            return moves;
        }

        /// <summary>Sĩ đi chéo 1 ô, không bao giờ ra khỏi cung (cột 3..5, cùng phe với tướng).</summary>
        private static List<Move> AdvisorMoves(ChessBoard board, ChessPiece advisor)
        {
            var moves = new List<Move>();

            foreach (var (dr, dc) in AdvisorSteps)
            {
                int toRow = advisor.Row + dr;
                int toCol = advisor.Col + dc;

                if (!IsInsideBoard(toRow, toCol))
                    continue;

                if (!InPalace(advisor.Color, toRow, toCol))
                    continue;

                if (!CanLand(board, advisor, toRow, toCol))
                    continue;

                moves.Add(new Move(advisor, advisor.Row, advisor.Col, toRow, toCol));
            }

            return moves;
        }

        /// <summary>Tướng đi thẳng 1 ô ngang/dọc, không được rời cung.</summary>
        private static List<Move> KingMoves(ChessBoard board, ChessPiece king)
        {
            var moves = new List<Move>();

            foreach (var (dr, dc) in KingSteps)
            {
                int toRow = king.Row + dr;
                int toCol = king.Col + dc;

                if (!IsInsideBoard(toRow, toCol))
                    continue;

                if (!InPalace(king.Color, toRow, toCol))
                    continue;

                if (!CanLand(board, king, toRow, toCol))
                    continue;

                moves.Add(new Move(king, king.Row, king.Col, toRow, toCol));
            }

            return moves;
        }

        /// <summary>
        /// Tốt đi thẳng 1 ô về phía trước (đỏ đi lên, đen đi xuống), KHÔNG được lùi.
        /// Khi đã qua sông mới được đi ngang 1 ô.
        /// </summary>
        private static List<Move> SoldierMoves(ChessBoard board, ChessPiece soldier)
        {
            var moves = new List<Move>();

            int forward = soldier.Color == Side.Red ? -1 : 1;

            int fRow = soldier.Row + forward;
            if (IsInsideBoard(fRow, soldier.Col) && CanLand(board, soldier, fRow, soldier.Col))
                moves.Add(new Move(soldier, soldier.Row, soldier.Col, fRow, soldier.Col));

            if (HasCrossedRiver(soldier))
            {
                int leftCol = soldier.Col - 1;
                if (IsInsideBoard(soldier.Row, leftCol) && CanLand(board, soldier, soldier.Row, leftCol))
                    moves.Add(new Move(soldier, soldier.Row, soldier.Col, soldier.Row, leftCol));

                int rightCol = soldier.Col + 1;
                if (IsInsideBoard(soldier.Row, rightCol) && CanLand(board, soldier, soldier.Row, rightCol))
                    moves.Add(new Move(soldier, soldier.Row, soldier.Col, soldier.Row, rightCol));
            }

            return moves;
        }

        private static readonly (int dr, int dc)[] Directions = { (-1, 0), (1, 0), (0, -1), (0, 1) };

        /// <summary>(cách đi, ô chân bị chặn tương ứng) cho mã.</summary>
        private static readonly (int dr, int dc, int legR, int legC)[] HorseSteps =
        {
            (-2, -1, -1,  0),   // lên 2, trái 1 — chân chặn ở ô phía trên
            (-2,  1, -1,  0),   // lên 2, phải 1
            ( 2, -1,  1,  0),   // xuống 2, trái 1
            ( 2,  1,  1,  0),   // xuống 2, phải 1
            (-1, -2,  0, -1),   // trái 2, lên 1 — chân chặn ở ô bên trái
            ( 1, -2,  0, -1),   // trái 2, xuống 1
            (-1,  2,  0,  1),   // phải 2, lên 1
            ( 1,  2,  0,  1)    // phải 2, xuống 1
        };

        private static readonly (int dr, int dc)[] ElephantSteps = { (-2, -2), (-2, 2), (2, -2), (2, 2) };

        private static readonly (int dr, int dc)[] AdvisorSteps = { (-1, -1), (-1, 1), (1, -1), (1, 1) };

        private static readonly (int dr, int dc)[] KingSteps = { (-1, 0), (1, 0), (0, -1), (0, 1) };

        // =========================================
        // Cổng hợp lệ và áp dụng nước đi
        // =========================================

        /// <summary>Nước đi hợp lệ đầy đủ: đúng luật quân, không để tướng mình bị chiếu, không để hai tướng gặp nhau.</summary>
        public static List<Move> GetLegalMoves(ChessBoard board, Side side)
        {
            var legal = new List<Move>();

            foreach (var move in GetPseudoLegalMoves(board, side))
            {
                if (CheckDetector.IsLegalMoveFull(board, move))
                    legal.Add(move);
            }

            return legal;
        }

        public static bool IsLegalMove(ChessBoard board, Move move)
        {
            return CheckDetector.IsLegalMoveFull(board, move);
        }

        /// <summary>Áp dụng nước đi lên bàn cờ, trả về thông tin để hoàn tác.</summary>
        public static MoveUndo ApplyMove(ChessBoard board, Move move)
        {
            var piece = board[move.FromRow, move.FromCol];
            var captured = board[move.ToRow, move.ToCol];

            board[move.FromRow, move.FromCol] = null;
            board[move.ToRow, move.ToCol] = piece;

            if (piece != null)
            {
                piece.Row = move.ToRow;
                piece.Col = move.ToCol;
            }

            return new MoveUndo
            {
                CapturedPiece = captured,
                ToRow = move.ToRow,
                ToCol = move.ToCol,
                FromRow = move.FromRow,
                FromCol = move.FromCol
            };
        }

        /// <summary>Hoàn tác nước đi đã áp dụng: trả quân về ô xuất phát, quân bị ăn về ô đích.</summary>
        public static void UndoMove(ChessBoard board, MoveUndo undo)
        {
            var piece = board[undo.ToRow, undo.ToCol];

            board[undo.ToRow, undo.ToCol] = undo.CapturedPiece;
            board[undo.FromRow, undo.FromCol] = piece;

            if (undo.CapturedPiece != null)
            {
                undo.CapturedPiece.Row = undo.ToRow;
                undo.CapturedPiece.Col = undo.ToCol;
            }

            if (piece != null)
            {
                piece.Row = undo.FromRow;
                piece.Col = undo.FromCol;
            }
        }

    }
}
