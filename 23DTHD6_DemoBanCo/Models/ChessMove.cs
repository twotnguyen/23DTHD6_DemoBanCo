namespace _23DTHD6_DemoBanCo.Models
{
    public class ChessMove
    {
        public string PieceId { get; set; } = "";

        public int FromRow { get; set; }
        public int FromCol { get; set; }

        public int ToRow { get; set; }
        public int ToCol { get; set; }
    }
}
