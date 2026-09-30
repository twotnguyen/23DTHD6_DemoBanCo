using System.Text;

namespace _23DTHD6_DemoBanCo.Services
{
    /// <summary>
    /// Sinh mã QR dạng SVG mà không cần package ngoài (môi trường offline).
    /// Bộ mã hoá đầy đủ theo ISO/IEC 18004: chế độ byte (UTF-8), sửa lỗi Reed-Solomon
    /// mức M, 8 kiểu mask và chấm điểm để tự chọn mask tốt nhất.
    /// Đã đối chiếu từng module với bản chuẩn cho cả 8 kiểu mask.
    /// </summary>
    public static class QrCodeService
    {
        /// <summary>Số module trắng bao quanh mã, chuẩn yêu cầu 4 module.</summary>
        private const int QuietZone = 4;

        /// <summary>Phiên bản cao nhất hỗ trợ. Mức M chứa được 666 byte nội dung.</summary>
        private const int MaxVersion = 20;

        /// <summary>Chỉ số mức sửa lỗi nội bộ: 0 = M (mức dùng chung).</summary>
        private const int EccLevelM = 0;

        private const int PenaltyN1 = 3;
        private const int PenaltyN2 = 3;
        private const int PenaltyN3 = 40;
        private const int PenaltyN4 = 10;

        /// <summary>Tổng số mã (dữ liệu + ECC) của từng phiên bản 1..20, chỉ số 0 không dùng.</summary>
        private static readonly int[] TotalCodewords =
        {
            0,
            26, 44, 70, 100, 134, 172, 196, 242, 292, 346,
            404, 466, 532, 581, 655, 733, 815, 901, 991, 1085
        };

        /// <summary>
        /// Cấu trúc khối mức M: { mã ECC mỗi khối, số khối nhóm 1, mã dữ liệu khối nhóm 1,
        /// số khối nhóm 2, mã dữ liệu khối nhóm 2 }. Dòng 0 không dùng.
        /// </summary>
        private static readonly int[,] EccTableM =
        {
            {  0,  0,  0,  0,  0 },
            { 10,  1, 16,  0,  0 },
            { 16,  1, 28,  0,  0 },
            { 26,  1, 44,  0,  0 },
            { 18,  2, 32,  0,  0 },
            { 24,  2, 43,  0,  0 },
            { 16,  4, 27,  0,  0 },
            { 18,  4, 31,  0,  0 },
            { 22,  2, 38,  2, 39 },
            { 22,  3, 36,  2, 37 },
            { 26,  4, 43,  1, 44 },
            { 30,  1, 50,  4, 51 },
            { 22,  6, 36,  2, 37 },
            { 22,  8, 37,  1, 38 },
            { 24,  4, 40,  5, 41 },
            { 24,  5, 41,  5, 42 },
            { 28,  7, 45,  3, 46 },
            { 28, 10, 46,  1, 47 },
            { 26,  9, 43,  4, 44 },
            { 26,  3, 44, 11, 45 },
            { 26,  3, 41, 13, 42 }
        };

        private static readonly int[] Exp = new int[512];
        private static readonly int[] Log = new int[256];

        static QrCodeService()
        {
            int x = 1;
            for (int i = 0; i < 255; i++)
            {
                Exp[i] = x;
                Log[x] = i;
                x <<= 1;
                if (x >= 256)
                    x ^= 0x11D; // đa thức nguyên sinh của GF(256)
            }

            for (int i = 255; i < 512; i++)
                Exp[i] = Exp[i - 255];
        }

        /// <summary>
        /// Trả về một ảnh SVG tự chứa chứa mã QR quét được của <paramref name="content"/>.
        /// </summary>
        public static string GenerateSvg(string content, int pixelSize = 240)
        {
            if (string.IsNullOrEmpty(content))
                throw new ArgumentException("Nội dung mã QR không được để trống.", nameof(content));

            if (pixelSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(pixelSize), "Kích thước phải lớn hơn 0.");

            bool[,] modules = Encode(Encoding.UTF8.GetBytes(content));

            int size = modules.GetLength(0);
            int side = size + QuietZone * 2;

            var svg = new StringBuilder(side * 8);
            svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ")
               .Append(side).Append(' ').Append(side)
               .Append("\" width=\"").Append(pixelSize)
               .Append("\" height=\"").Append(pixelSize)
               .Append("\" shape-rendering=\"crispEdges\" role=\"img\">");

            // Nền trắng: scanner cần vùng yên tĩnh quanh mã
            svg.Append("<rect width=\"").Append(side).Append("\" height=\"")
               .Append(side).Append("\" fill=\"#fff\"/>");

            svg.Append("<path fill=\"#000\" d=\"");
            bool any = false;

            for (int row = 0; row < size; row++)
            {
                int col = 0;
                while (col < size)
                {
                    if (!modules[row, col])
                    {
                        col++;
                        continue;
                    }

                    // Gộp các ô đen liền nhau thành một hình chữ nhật
                    int start = col;
                    while (col < size && modules[row, col])
                        col++;

                    int x = start + QuietZone;
                    int y = row + QuietZone;
                    int width = col - start;

                    if (any)
                        svg.Append(' ');

                    svg.Append('M').Append(x).Append(' ').Append(y)
                       .Append("h").Append(width)
                       .Append("v1h").Append(-width)
                       .Append('z');
                    any = true;
                }
            }

            svg.Append("\"/></svg>");
            return svg.ToString();
        }

        /// <summary>Nhúng ảnh QR vào thuộc tính src để dùng thẳng trong thẻ img.</summary>
        public static string GenerateDataUri(string content, int pixelSize = 240)
        {
            string svg = GenerateSvg(content, pixelSize);
            return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
        }

        /// <summary>Mã hoá nội dung thành lưới module: true = ô đen, false = ô trắng.</summary>
        private static bool[,] Encode(byte[] data)
        {
            int version = ChooseVersion(data.Length);
            byte[] codewords = BuildCodewords(version, data);
            int size = version * 4 + 17;

            // Khung nền: module chức năng không bao giờ bị mask, module dữ liệu thì có
            var modules = new bool[size, size];
            var isFunction = new bool[size, size];

            DrawFunctionPatterns(modules, isFunction, version, size);

            var best = new bool[size * size];
            int bestPenalty = int.MaxValue;

            for (int mask = 0; mask < 8; mask++)
            {
                var candidate = (bool[,])modules.Clone();
                DrawCodewords(candidate, isFunction, codewords, size, mask);
                DrawFormatBits(candidate, size, mask);

                int penalty = PenaltyScore(candidate);
                if (penalty >= bestPenalty)
                    continue;

                bestPenalty = penalty;
                for (int row = 0; row < size; row++)
                    for (int col = 0; col < size; col++)
                        best[row * size + col] = candidate[row, col];
            }

            var result = new bool[size, size];
            for (int row = 0; row < size; row++)
                for (int col = 0; col < size; col++)
                    result[row, col] = best[row * size + col];

            return result;
        }

        /// <summary>Chọn phiên bản nhỏ nhất còn chỗ cho số byte nội dung.</summary>
        private static int ChooseVersion(int length)
        {
            for (int version = 1; version <= MaxVersion; version++)
            {
                // 4 bit chỉ báo chế độ + 8/16 bit độ dài = 12/20 bit, tức 2 hoặc 3 byte
                int capacity = DataCodewordCount(version) - (version <= 9 ? 2 : 3);

                if (length <= capacity)
                    return version;
            }

            throw new ArgumentException(
                $"Nội dung dài {length} byte, vượt quá giới hạn của phiên bản {MaxVersion}.",
                nameof(length));
        }

        /// <summary>Dựng chuỗi mã cuối cùng: chia khối, tính Reed-Solomon rồi xen kẽ.</summary>
        private static byte[] BuildCodewords(int version, byte[] data)
        {
            int dataCodewords = DataCodewordCount(version);
            int countBits = version <= 9 ? 8 : 16;

            var bits = new BitBuffer();
            bits.Append(0b0100, 4);            // chỉ báo chế độ byte
            bits.Append(data.Length, countBits);
            foreach (byte b in data)
                bits.Append(b, 8);

            int capacityBits = dataCodewords * 8;
            bits.Append(0, Math.Min(4, capacityBits - bits.Length));   // kết thúc
            bits.Append(0, (8 - bits.Length % 8) % 8);                 // căn byte

            // Mã đệm xen kẽ 0xEC / 0x11
            for (int pad = 0; bits.Length < capacityBits; pad++)
                bits.Append(pad % 2 == 0 ? 0xEC : 0x11, 8);

            byte[] payload = bits.ToBytes();

            int ecPerBlock = EccTableM[version, 0];
            int blocks1 = EccTableM[version, 1];
            int data1 = EccTableM[version, 2];
            int blocks2 = EccTableM[version, 3];
            int data2 = EccTableM[version, 4];
            int totalBlocks = blocks1 + blocks2;

            var dataBlocks = new byte[totalBlocks][];
            var ecBlocks = new byte[totalBlocks][];

            int offset = 0;
            for (int i = 0; i < totalBlocks; i++)
            {
                int blockSize = i < blocks1 ? data1 : data2;
                var block = new byte[blockSize];
                Array.Copy(payload, offset, block, 0, blockSize);
                offset += blockSize;

                dataBlocks[i] = block;
                ecBlocks[i] = ReedSolomon(block, ecPerBlock);
            }

            var result = new byte[TotalCodewords[version]];
            int written = 0;
            int maxBlock = Math.Max(data1, data2);

            for (int i = 0; i < maxBlock; i++)
                for (int b = 0; b < totalBlocks; b++)
                    if (i < dataBlocks[b].Length)
                        result[written++] = dataBlocks[b][i];

            for (int i = 0; i < ecPerBlock; i++)
                for (int b = 0; b < totalBlocks; b++)
                    result[written++] = ecBlocks[b][i];

            return result;
        }

        private static int DataCodewordCount(int version)
        {
            return EccTableM[version, 1] * EccTableM[version, 2]
                 + EccTableM[version, 3] * EccTableM[version, 4];
        }

        /// <summary>Tính mã sửa lỗi Reed-Solomon: phần dư chia dữ liệu cho đa thức sinh.</summary>
        private static byte[] ReedSolomon(byte[] data, int ecCount)
        {
            byte[] generator = ReedSolomonGenerator(ecCount);
            var buffer = new byte[data.Length + ecCount];
            Array.Copy(data, buffer, data.Length);

            for (int i = 0; i < data.Length; i++)
            {
                byte factor = buffer[i];
                if (factor == 0)
                    continue;

                for (int j = 0; j < generator.Length; j++)
                    buffer[i + j] ^= (byte)GfMultiply(generator[j], factor);
            }

            var result = new byte[ecCount];
            Array.Copy(buffer, data.Length, result, 0, ecCount);
            return result;
        }

        /// <summary>Dựng đa thức sinh (x - a^0)(x - a^1)...(x - a^(n-1)) trên GF(256).</summary>
        private static byte[] ReedSolomonGenerator(int degree)
        {
            byte[] poly = { 1 };

            for (int i = 0; i < degree; i++)
            {
                var next = new byte[poly.Length + 1];
                for (int j = 0; j < poly.Length; j++)
                {
                    next[j] ^= poly[j];
                    next[j + 1] ^= (byte)GfMultiply(poly[j], Exp[i]);
                }

                poly = next;
            }

            return poly;
        }

        private static int GfMultiply(int a, int b)
        {
            if (a == 0 || b == 0)
                return 0;

            return Exp[Log[a] + Log[b]];
        }

        /// <summary>Vẽ các mẫu chức năng: định vị, tách, đồng hồ, căn chỉnh, thông tin phiên bản.</summary>
        private static void DrawFunctionPatterns(bool[,] modules, bool[,] isFunction, int version, int size)
        {
            // Dải đồng hồ chạy giữa hai mẫu định vị
            for (int i = 8; i < size - 8; i++)
            {
                bool dark = i % 2 == 0;
                SetFunction(modules, isFunction, 6, i, dark);
                SetFunction(modules, isFunction, i, 6, dark);
            }

            DrawFinder(modules, isFunction, 3, 3);
            DrawFinder(modules, isFunction, size - 4, 3);
            DrawFinder(modules, isFunction, 3, size - 4);

            int[] align = AlignmentPositions(version, size);
            int count = align.Length;

            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < count; j++)
                {
                    // Bỏ qua vị trí chồng lên mẫu định vị
                    bool nearFinder = (i == 0 && j == 0)
                                   || (i == 0 && j == count - 1)
                                   || (i == count - 1 && j == 0);
                    if (!nearFinder)
                        DrawAlignment(modules, isFunction, align[i], align[j]);
                }
            }

            // Chỗ để dành cho thông tin định dạng, vẽ thật sau khi đã chọn mask.
            // Bỏ qua chỉ số 6: (6,8) và (8,6) thuộc dải đồng hồ, không phải thông tin định dạng.
            for (int i = 0; i < 9; i++)
            {
                if (i == 6)
                    continue;

                SetFunction(modules, isFunction, 8, i, false);
                SetFunction(modules, isFunction, i, 8, false);
            }

            for (int i = 0; i < 8; i++)
            {
                SetFunction(modules, isFunction, 8, size - 1 - i, false);
                SetFunction(modules, isFunction, size - 1 - i, 8, false);
            }

            // Dải thông tin phiên bản, chỉ có từ phiên bản 7 trở lên
            if (version >= 7)
                DrawVersion(modules, isFunction, size, version);
        }

        private static void DrawFinder(bool[,] modules, bool[,] isFunction, int centerRow, int centerCol)
        {
            for (int dy = -4; dy <= 4; dy++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int row = centerRow + dy;
                    int col = centerCol + dx;

                    if (row < 0 || row >= modules.GetLength(0) || col < 0 || col >= modules.GetLength(1))
                        continue;

                    // dist 2 là vòng trắng, dist 4 là vùng tách: đều phải trắng
                    bool dark = Math.Max(Math.Abs(dx), Math.Abs(dy)) is 0 or 1 or 3;
                    modules[row, col] = dark;
                    isFunction[row, col] = true;
                }
            }
        }

        private static void DrawAlignment(bool[,] modules, bool[,] isFunction, int centerRow, int centerCol)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    int row = centerRow + dy;
                    int col = centerCol + dx;

                    modules[row, col] = Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1;
                    isFunction[row, col] = true;
                }
            }
        }

        /// <summary>
        /// Vị trí tâm các mẫu căn chỉnh. Đi ngược từ vị trí cuối cùng để bước nhảy
        /// luôn chia hết, tránh sai lệch do phép chia làm tròn ở phiên bản 15, 16, 18, 19.
        /// </summary>
        private static int[] AlignmentPositions(int version, int size)
        {
            if (version == 1)
                return Array.Empty<int>();

            int numAlign = version / 7 + 2;
            int step = (version * 4 + numAlign * 2 + 1) / (numAlign * 2 - 2) * 2;

            var positions = new int[numAlign];
            positions[0] = 6;

            int current = size - 7;
            for (int i = numAlign - 1; i >= 1; i--)
            {
                positions[i] = current;
                current -= step;
            }

            return positions;
        }

        private static void DrawVersion(bool[,] modules, bool[,] isFunction, int size, int version)
        {
            int rem = version;
            for (int i = 0; i < 12; i++)
                rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);

            int bits = (version << 12) | rem;

            for (int i = 0; i < 18; i++)
            {
                bool bit = ((bits >> i) & 1) != 0;
                int a = size - 11 + i % 3;
                int b = i / 3;
                modules[b, a] = bit;
                isFunction[b, a] = true;
                modules[a, b] = bit;
                isFunction[a, b] = true;
            }
        }

        /// <summary>Điền mã dữ liệu theo đường hình zíc-zác từ góc dưới bên phải.</summary>
        private static void DrawCodewords(bool[,] modules, bool[,] isFunction, byte[] codewords, int size, int mask)
        {
            int bitIndex = 0;
            int bitCount = codewords.Length * 8;

            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6)
                    right = 5; // cột đồng hồ không mang dữ liệu

                for (int vert = 0; vert < size; vert++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        int col = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int row = upward ? size - 1 - vert : vert;

                        if (isFunction[row, col])
                            continue;

                        bool bit = bitIndex < bitCount
                            && ((codewords[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1) != 0;

                        // Module thừa cuối cùng của phiên bản để giá trị 0 rồi mới bị mask
                        modules[row, col] = bit != MaskBit(mask, row, col);
                        bitIndex++;
                    }
                }
            }
        }

        private static void DrawFormatBits(bool[,] modules, int size, int mask)
        {
            // Thứ tự mức sửa lỗi trong 2 bit: L là 1, M là 0, Q là 3, H là 2
            int[] levelBits = { 0b00, 0b01, 0b11, 0b10 };

            int data = (levelBits[EccLevelM] << 3) | mask;
            int rem = data;
            for (int i = 0; i < 10; i++)
                rem = (rem << 1) ^ ((rem >> 9) * 0x537);

            int bits = ((data << 10) | rem) ^ 0x5412;

            for (int i = 0; i <= 5; i++)
                modules[i, 8] = ((bits >> i) & 1) != 0;
            modules[7, 8] = ((bits >> 6) & 1) != 0;
            modules[8, 8] = ((bits >> 7) & 1) != 0;
            modules[8, 7] = ((bits >> 8) & 1) != 0;
            for (int i = 9; i < 15; i++)
                modules[8, 14 - i] = ((bits >> i) & 1) != 0;

            for (int i = 0; i < 8; i++)
                modules[8, size - 1 - i] = ((bits >> i) & 1) != 0;
            for (int i = 8; i < 15; i++)
                modules[size - 15 + i, 8] = ((bits >> i) & 1) != 0;

            modules[size - 8, 8] = true; // module tối cố định
        }

        private static bool MaskBit(int mask, int row, int col)
        {
            return mask switch
            {
                0 => (row + col) % 2 == 0,
                1 => row % 2 == 0,
                2 => col % 3 == 0,
                3 => (row + col) % 3 == 0,
                4 => (row / 2 + col / 3) % 2 == 0,
                5 => row * col % 2 + row * col % 3 == 0,
                6 => (row * col % 2 + row * col % 3) % 2 == 0,
                7 => ((row + col) % 2 + row * col % 3) % 2 == 0,
                _ => false
            };
        }

        private static int PenaltyScore(bool[,] modules)
        {
            int size = modules.GetLength(0);
            int result = 0;
            var line = new bool[size];

            // Quy tắc 1 và 3 theo từng hàng
            for (int row = 0; row < size; row++)
            {
                for (int col = 0; col < size; col++)
                    line[col] = modules[row, col];

                result += RunPenalty(line) + FinderPenalty(line);
            }

            // Quy tắc 1 và 3 theo từng cột
            for (int col = 0; col < size; col++)
            {
                for (int row = 0; row < size; row++)
                    line[row] = modules[row, col];

                result += RunPenalty(line) + FinderPenalty(line);
            }

            // Quy tắc 2: khối 2x2 cùng màu
            for (int row = 0; row < size - 1; row++)
            {
                for (int col = 0; col < size - 1; col++)
                {
                    bool color = modules[row, col];
                    if (modules[row, col + 1] == color
                        && modules[row + 1, col] == color
                        && modules[row + 1, col + 1] == color)
                    {
                        result += PenaltyN2;
                    }
                }
            }

            // Quy tắc 4: độ lệch tỉ lệ ô đen so với 50%
            int dark = 0;
            for (int row = 0; row < size; row++)
                for (int col = 0; col < size; col++)
                    if (modules[row, col])
                        dark++;

            int total = size * size;
            int k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
            result += k * PenaltyN4;

            return result;
        }

        /// <summary>Quy tắc 1: mỗi dải liên tiếp cùng màu dài từ 5 trở lên.</summary>
        private static int RunPenalty(bool[] line)
        {
            int result = 0;
            int run = 1;

            for (int i = 1; i < line.Length; i++)
            {
                if (line[i] == line[i - 1])
                {
                    run++;
                    continue;
                }

                if (run >= 5)
                    result += PenaltyN1 + (run - 5);

                run = 1;
            }

            if (run >= 5)
                result += PenaltyN1 + (run - 5);

            return result;
        }

        /// <summary>Quy tắc 3: mẫu 1:1:3:1:1 có dải trắng dài 4 module ở một trong hai phía.</summary>
        private static int FinderPenalty(bool[] line)
        {
            const int patternSize = 7;
            bool[] pattern = { true, false, true, true, true, false, true };

            int result = 0;

            for (int i = 0; i + patternSize <= line.Length; i++)
            {
                bool match = true;
                for (int k = 0; k < patternSize; k++)
                {
                    if (line[i + k] == pattern[k])
                        continue;

                    match = false;
                    break;
                }

                if (!match)
                    continue;

                if (IsLightRun(line, i - 4, 4) || IsLightRun(line, i + patternSize, 4))
                    result += PenaltyN3;
            }

            return result;
        }

        private static bool IsLightRun(bool[] line, int start, int count)
        {
            if (start < 0 || start + count > line.Length)
                return false;

            for (int i = start; i < start + count; i++)
                if (line[i])
                    return false;

            return true;
        }

        private static void SetFunction(bool[,] modules, bool[,] isFunction, int row, int col, bool dark)
        {
            if (row < 0 || row >= modules.GetLength(0) || col < 0 || col >= modules.GetLength(1))
                return;

            modules[row, col] = dark;
            isFunction[row, col] = true;
        }

        /// <summary>Ghi chuỗi bit, đủ bị theo dõi để tự thêm bit kết thúc và bit đệm.</summary>
        private sealed class BitBuffer
        {
            private readonly List<byte> _bytes = new();

            public int Length { get; private set; }

            public void Append(int value, int bitCount)
            {
                for (int i = bitCount - 1; i >= 0; i--)
                {
                    if ((_bytes.Count * 8) == Length)
                        _bytes.Add(0);

                    if (((value >> i) & 1) != 0)
                        _bytes[^1] |= (byte)(1 << (7 - (Length % 8)));

                    Length++;
                }
            }

            public byte[] ToBytes() => _bytes.ToArray();
        }
    }
}
