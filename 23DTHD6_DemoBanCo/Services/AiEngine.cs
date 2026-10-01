using System.Diagnostics;
using _23DTHD6_DemoBanCo.Models;

namespace _23DTHD6_DemoBanCo.Services
{
    /// <summary>
    /// Kết quả tìm kiếm nước đi của máy, dùng cho widget thống kê khi demo.
    /// </summary>
    public class AiMoveResult
    {
        public Move? BestMove { get; set; }

        public int NodesEvaluated { get; set; }

        public int DepthReached { get; set; }

        public long ElapsedMs { get; set; }

        public string PrincipalVariation { get; set; } = "";

        public bool TimedOut { get; set; }
    }

    /// <summary>
    /// Máy cờ minimax + alpha-beta, viết thuần bằng C#.
    /// Không gọi API bên ngoài, không dùng thư viện cờ.
    /// </summary>
    public static class AiEngine
    {
        private const int MaxDepth = 8;

        // Điểm quân theo đặc tả
        private const int ChariotValue = 90;
        private const int CannonValue = 45;
        private const int HorseValue = 40;
        private const int SoldierValue = 10;
        private const int ElephantValue = 20;
        private const int AdvisorValue = 20;
        private const int KingValue = 10000;

        /// <summary>Ngân sách thời gian (ms) cho từng cấp độ.</summary>
        public static int TimeBudgetMs(AiDifficulty difficulty) => difficulty switch
        {
            AiDifficulty.Easy => 300,
            AiDifficulty.Medium => 1000,
            AiDifficulty.Hard => 3000,
            _ => 300
        };

        /// <summary>Độ sâu tìm kiếm cho từng cấp độ.</summary>
        public static int DepthFor(AiDifficulty difficulty) => difficulty switch
        {
            AiDifficulty.Easy => 2,
            AiDifficulty.Medium => 4,
            AiDifficulty.Hard => 6,
            _ => 2
        };

        public static AiMoveResult FindBestMove(ChessBoard board, Side sideToMove, AiDifficulty difficulty)
        {
            var result = new AiMoveResult();
            var stopwatch = Stopwatch.StartNew();
            long budget = TimeBudgetMs(difficulty);
            int maxDepth = DepthFor(difficulty);

            var moves = ChessRules.GetLegalMoves(board, sideToMove);
            if (moves.Count == 0)
            {
                stopwatch.Stop();
                result.ElapsedMs = stopwatch.ElapsedMilliseconds;
                result.BestMove = null;
                return result;
            }

            // Sắp xếp nước đi: thử bắt quân trước để cắt alpha-beta sớm hơn
            var ordered = OrderMovesByCapture(board, moves, sideToMove);

            Move? bestMove = null;
            int bestScore = int.MinValue;
            var pv = new List<Move>();

            for (int depth = 1; depth <= maxDepth; depth++)
            {
                pv.Clear();
                int score = Negamax(board, sideToMove, depth, int.MinValue, int.MaxValue, result, stopwatch, budget, pv);

                if (stopwatch.ElapsedMilliseconds > budget)
                {
                    // Hết giờ ở giữa chừng: giữ lại kết quả tốt nhất của lượt trước
                    result.TimedOut = true;
                    break;
                }

                bestScore = score;
                result.DepthReached = depth;

                if (pv.Count > 0)
                    bestMove = pv[0];
            }

            stopwatch.Stop();
            result.ElapsedMs = stopwatch.ElapsedMilliseconds;
            result.BestMove = bestMove ?? ordered.FirstOrDefault();
            result.PrincipalVariation = string.Join(" ",
                pv.Select(m => MoveToSan(board, m))
                  .Where(s => !string.IsNullOrEmpty(s)));

            return result;
        }

        /// <summary>
        /// Negamax: điểm luôn được tính từ góc nhìn bên đang đi, đảo dấu khi đổi bên.
        /// </summary>
        private static int Negamax(
            ChessBoard board,
            Side sideToMove,
            int depth,
            int alpha,
            int beta,
            AiMoveResult result,
            Stopwatch stopwatch,
            long budgetMs,
            List<Move> pv)
        {
            result.NodesEvaluated++;

            // Chặn thời gian phải kiểm ở MỌI node, không phải cứ 64 node như trước.
            // Lý do: giữa hai lần kiểm, engine có thể dồn toàn bộ nhánh con của một
            // node sâu vào, nên ván ở cấp Khó vượt ngân sách 3000ms tới ~4000ms —
            // tức là người chơi chờ lâu gấp rưỡi so với cam kết của đặc tả 6.1.
            // Khi hết giờ, trả luôn điểm tĩnh ở thế cờ hiện tại và đánh dấu TimedOut
            // để lớp trên giữ kết quả tốt nhất của lượt trước.
            if (stopwatch.ElapsedMilliseconds > budgetMs)
            {
                result.TimedOut = true;
                return EvaluateBoard(board, sideToMove);
            }

            if (depth == 0)
                return EvaluateBoard(board, sideToMove);

            var moves = ChessRules.GetLegalMoves(board, sideToMove);
            if (moves.Count == 0)
            {
                // Không còn nước đi: nếu đang bị chiếu thì thua (chiếu hết), ngược lại thua vì bị vây
                bool inCheck = CheckDetector.IsInCheck(board, sideToMove);
                return inCheck ? -KingValue - depth : -KingValue / 2;
            }

            var ordered = OrderMovesByCapture(board, moves, sideToMove);

            int bestScore = int.MinValue;
            Move? bestMove = null;
            var childPv = new List<Move>();

            foreach (var move in ordered)
            {
                var undo = ChessRules.ApplyMove(board, move);

                childPv.Clear();
                int score = -Negamax(board, Opposite(sideToMove), depth - 1, -beta, -alpha, result, stopwatch, budgetMs, childPv);

                ChessRules.UndoMove(board, undo);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMove = move;
                    childPv.Insert(0, move);
                    pv.Clear();
                    pv.AddRange(childPv);
                }

                if (bestScore > alpha)
                    alpha = bestScore;

                if (alpha >= beta)
                    break;  // cắt
            }

            return bestScore;
        }

        private static IEnumerable<Move> OrderMovesByCapture(ChessBoard board, List<Move> moves, Side sideToMove)
        {
            return moves
                .OrderByDescending(m => board[m.ToRow, m.ToCol] != null)
                .ThenByDescending(m => PieceValue(board[m.FromRow, m.FromCol]))
                .ToList();
        }

        private static int PieceValue(ChessPiece? piece)
        {
            if (piece == null) return 0;

            return piece.Type switch
            {
                PieceType.King => KingValue,
                PieceType.Chariot => ChariotValue,
                PieceType.Cannon => CannonValue,
                PieceType.Horse => HorseValue,
                PieceType.Soldier => SoldierValue,
                PieceType.Elephant => ElephantValue,
                PieceType.Advisor => AdvisorValue,
                _ => 0
            };
        }

        /// <summary>
        /// Điểm đánh giá bàn cờ, luôn dương là có lợi cho bên đang đi.
        /// </summary>
        public static int EvaluateBoard(ChessBoard board, Side sideToMove)
        {
            int score = 0;

            for (int row = 0; row < ChessRules.Rows; row++)
            {
                for (int col = 0; col < ChessRules.Cols; col++)
                {
                    var piece = board[row, col];
                    if (piece == null) continue;

                    int value = PieceValue(piece);

                    // Tốt càng tiến càng đáng giá
                    if (piece.Type == PieceType.Soldier)
                    {
                        int advanced = piece.Color == Side.Red ? piece.Row : ChessRules.Rows - 1 - piece.Row;
                        value += advanced * 2;
                    }

                    score += piece.Color == sideToMove ? value : -value;
                }
            }

            return score;
        }

        /// <summary>
        /// Ký hiệu nước đi. Dùng chung GameEvaluator.ToSan để chỉ có một cách ký hiệu.
        /// </summary>
        public static string MoveToSan(ChessBoard board, Move move)
        {
            return GameEvaluator.ToSan(board, move);
        }

        public static List<string> GetPrincipalVariation(ChessBoard board, Side sideToMove, int maxLength)
        {
            var moves = new List<string>();
            var working = board.Clone();
            var side = sideToMove;

            for (int i = 0; i < maxLength; i++)
            {
                var legal = ChessRules.GetLegalMoves(working, side);
                if (legal.Count == 0) break;

                var result = FindBestMove(working, side, AiDifficulty.Easy);
                if (result.BestMove == null) break;

                moves.Add(MoveToSan(working, result.BestMove));
                ChessRules.ApplyMove(working, result.BestMove);
                side = Opposite(side);
            }

            return moves;
        }

        private static Side Opposite(Side side) => side == Side.Red ? Side.Black : Side.Red;
    }
}