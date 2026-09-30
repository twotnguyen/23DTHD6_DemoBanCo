/* ==========================================================================
   chess-board.js — Module hiển thị bàn cờ tướng 10x9.

   Không phụ thuộc Vue, không phụ thuộc framework: chỉ là một object gắn vào
   window.ChessBoardView. Bàn cờ được vẽ bằng ảnh nền + các <img> quân đặt
   theo phần trăm, nên co giãn theo màn hình mà không vỡ tỉ lệ.

   Cài đặt:
       <script src="/js/chess-board.js"></script>

   Dùng:
       const board = ChessBoardView.init(document.getElementById('boardRoot'), '/images/');
       board.onMove = (from, to, pieceId) => { ... };
       board.setPieces([{ id: 'xe_do_1', file: 'xedo1.svg', row: 9, col: 0, color: 'do' }]);
       board.setLegalMoves(match.legalMoves);

   ------------------------------------------------------------------------
   HỆ TOẠ ĐỘ (quan trọng — đọc trước khi sửa)
   ------------------------------------------------------------------------
   Ảnh nền bancotuong.jpg là hình VUÔNG 850x850. Bàn cờ không chiếm hết
   ảnh: 9 đường cột nằm trong khoảng 15.5% .. 84.5% theo chiều ngang, 10
   đường hàng nằm trong khoảng 10.5% .. 91% theo chiều dọc.

   Vì mọi thứ đặt bằng PHẦN TRĂM của khung chứa (không phải pixel), bàn cờ
   tự co giãn đúng theo kích thước hiển thị, kể cả khi bố cục đổi.

   Chiều "phần trăm"      ->  ô (row, col):
       tỉ lệ = (phầnTrăm - grid.left) / (grid.right - grid.left)   [cột]
                (phầnTrăm - grid.top ) / (grid.bottom - grid.top )  [hàng]
       rồi nhân với (số ô - 1) rồi LÀM TRÒN.

   Chiều ngược lại         ô (row, col) -> phần trăm:
       pctX = grid.left  + col * (grid.right  - grid.left ) / (COLS - 1)
       pctY = grid.top   + row * (grid.bottom - grid.top ) / (ROWS - 1)

   Hàng 0 nằm ở ĐỈNH ảnh, tức là phía quân ĐEN; hàng 9 ở đáy là phía quân ĐỎ.
   Đây đúng là quy ước hàng mà engine dùng (ChessRules.CreateInitialBoard) và
   đúng bố cục của ảnh nền, nên số hàng lấy NGUYÊN TỪ FEN, không đảo.

   HƯỚNG NHÌN: mặc định vẽ như đang nhìn từ phe đỏ. Người cầm đen
   (yourSide === 1) sẽ XOAY bàn 180 độ — xem setOrientation().
   ========================================================================== */
(function (global) {
    'use strict';

    const ROWS = 10;
    const COLS = 9;

    const ChessBoardView = {
        rows: ROWS,
        cols: COLS,

        /* Lề của lưới bàn cờ tính theo % so với ảnh nền 850x850 */
        grid: { left: 15.5, right: 84.5, top: 10.5, bottom: 91 },

        /* Ngưỡng px để phân biệt "bấm" với "kéo" */
        dragThreshold: 5,

        /* ---------------------------------------------------------------
           Trạng thái nội bộ
           --------------------------------------------------------------- */
        container: null,
        imgBasePath: '/images/',

        onMove: null,              // (from, to, pieceId) -> void
        onSelectionChange: null,   // (pieceId | null) -> void

        _layer: null,             // <div> chứa toàn bộ quân + ô tô
        _elements: {},             // pieceId -> <img>
        _markers: [],              // pool các <div> dùng để tô ô
        _dragState: null,
        _keyHandler: null,

        thisSide: null,            // 'do' | 'den' | null  (phe của người xem)
        canMove: true,             // server báo được phép đi hay không
        flipped: false,            // true = xoay bàn cho người cầm đen
        statusText: '',
        _legalMoves: [],           // [{fromRow, fromCol, toRow, toCol}]
        _legalTargets: [],         // [{row, col}]  — tập ô đang tô
        _capturable: [],           // [{row, col}]  — tập ô bắt được
        _lastMove: null,           // {from:{row,col}, to:{row,col}}
        _checkCell: null,          // {row, col}
        _selectedId: null,

        /* ===============================================================
           KHỞI TẠO
           =============================================================== */

        /* ---------------------------------------------------------------
           init(container, imgBasePath)
           Dựng khung bàn cờ vào container, gắn listener một lần duy nhất.
           Gọi lại init trên cùng container sẽ hủy sạch trước.
           --------------------------------------------------------------- */
        init(container, imgBasePath) {
            if (!container) {
                throw new Error('ChessBoardView.init: thiếu container');
            }

            /* init lại trên cùng container -> dọn sạch trước để không nhân bản listener */
            if (this.container === container) {
                this.destroy();
            }

            this.container = container;
            this.imgBasePath = imgBasePath || '/images/';

            this._elements = {};
            this._markers = [];
            this._selectionRing = null;
            this._dragState = null;
            this._selectedId = null;
            this._legalMoves = [];
            this._legalTargets = [];
            this._capturable = [];
            this._lastMove = null;
            this._checkCell = null;

            container.classList.add('chess-board');
            container.innerHTML = '';
            container.style.position = 'relative';

            /* Ảnh nền bàn cờ: đặt làm phần tử đầu, các quân nằm đè lên */
            const boardImage = document.createElement('img');
            boardImage.className = 'board-image xb-image';
            boardImage.src = this.imgBasePath + 'bancotuong.jpg';
            boardImage.alt = 'Bàn cờ tướng 10 hàng 9 cột';
            boardImage.draggable = false;
            container.appendChild(boardImage);

            /* Vùng chứa các quân, tách riêng để listener kéo-thả gắn vào đây
               mà không phải đụng tới ảnh nền. */
            const layer = document.createElement('div');
            layer.className = 'xb-layer';
            container.appendChild(layer);
            this._layer = layer;

            /* Nghe click nền bàn cờ (vùng không có quân) */
            this._onBoardClick = (e) => this._handleBoardClick(e);
            container.addEventListener('click', this._onBoardClick);

            /* Escape huỷ chọn. Gắn ở cấp document để hoạt động kể cả khi
               focus đang ở ô nhập chat bên cạnh. */
            this._keyHandler = (e) => {
                if (e.key === 'Escape') {
                    this.clearSelection();
                }
            };
            document.addEventListener('keydown', this._keyHandler);

            return this;
        },

        /* ===============================================================
           QUY ĐỔI TOẠ ĐỘ
           =============================================================== */

        /* Ô (row,col) -> { x, y } tính bằng phần trăm của khung chứa.

           Khi bàn bị xoay (người chơi cầm đen), hàng và cột đều đảo theo
           công thức (n - 1 - v). Bàn cờ đối xứng nên phép xoay này vẫn giữ
           đúng vị trí quân, chỉ đổi góc nhìn. Việc đảo CHỈ nằm ở hai hàm
           quy đổi này — mọi nơi khác (legalMoves, _row/_col, _pieceAt)
           vẫn dùng toạ độ logic, nên không có chỗ nào phải biết bàn đang xoay. */
        _cellToPct(row, col) {
            const vRow = this.flipped ? (this.rows - 1 - row) : row;
            const vCol = this.flipped ? (this.cols - 1 - col) : col;

            const x = this.grid.left
                + vCol * (this.grid.right - this.grid.left) / (this.cols - 1);

            const y = this.grid.top
                + vRow * (this.grid.bottom - this.grid.top) / (this.rows - 1);

            return { x: x, y: y };
        },

        /* ---------------------------------------------------------------
           setFlipped(flipped)
           true = xoay bàn 180° cho người cầm đen (yourSide === 1).
           Sau khi đổi hướng, mọi quân và ô tô phải vẽ lại vì % đã đổi.
           --------------------------------------------------------------- */
        setFlipped(flipped) {
            const next = !!flipped;
            if (this.flipped === next) return;

            this.flipped = next;

            const ids = Object.keys(this._elements);
            for (let i = 0; i < ids.length; i++) {
                const el = this._elements[ids[i]];
                this._applyPosition(el, el._row, el._col, false);
            }

            this._refreshMarkers();
        },

        /* Toạ độ chuột (clientX/clientY) -> { row, col } đã kẹp về trong bàn.
           Trả về null nếu điểm nằm ngoài vùng lưới bàn cờ. */
        _pointToCell(clientX, clientY) {
            if (!this.container) return null;

            const rect = this.container.getBoundingClientRect();
            if (!rect.width || !rect.height) return null;

            /* clientX -> pixel tính từ mép trái khung -> phần trăm */
            const percentX = (clientX - rect.left) / rect.width * 100;
            const percentY = (clientY - rect.top) / rect.height * 100;

            /* Nằm ngoài lưới bàn cờ (vùng viền trắng của ảnh) -> không phải ô nào */
            if (percentX < this.grid.left || percentX > this.grid.right ||
                percentY < this.grid.top || percentY > this.grid.bottom) {
                return null;
            }

            let col = Math.round(
                (percentX - this.grid.left)
                / (this.grid.right - this.grid.left)
                * (this.cols - 1)
            );

            let row = Math.round(
                (percentY - this.grid.top)
                / (this.grid.bottom - this.grid.top)
                * (this.rows - 1)
            );

            /* Bàn đang xoay thì đảo ngược lại để trả về toạ độ logic
               (cùng hệ với toạ độ server gửi trong legalMoves). */
            if (this.flipped) {
                row = this.rows - 1 - row;
                col = this.cols - 1 - col;
            }

            /* Kẹp về [0, COLS-1] và [0, ROWS-1] để sai số làm tròn không
               làm trượt ra ngoài bàn. */
            return {
                row: Math.max(0, Math.min(this.rows - 1, row)),
                col: Math.max(0, Math.min(this.cols - 1, col))
            };
        },

        _sameCell(a, b) {
            return !!a && !!b && a.row === b.row && a.col === b.col;
        },

        _key(row, col) {
            return row + ':' + col;
        },

        _pieceAt(row, col) {
            const ids = Object.keys(this._elements);
            for (let i = 0; i < ids.length; i++) {
                const p = this._elements[ids[i]];
                if (p._row === row && p._col === col) {
                    return p;
                }
            }
            return null;
        },

        /* ===============================================================
           VẼ QUÂN
           =============================================================== */

        /* ---------------------------------------------------------------
           setPieces(pieces)
           pieces: [{ id, file, row, col, color }]

           Tái sử dụng <img> cũ khi trùng id => quân TỰ TRƯỢT từ ô này sang
           ô khác (CSS transition), không nhảy/teleport. Quân không còn
           trong danh sách sẽ bị xoá khỏi DOM.
           --------------------------------------------------------------- */
        setPieces(pieces) {
            if (!this._layer) return;

            const list = Array.isArray(pieces) ? pieces : [];
            const seen = {};

            for (let i = 0; i < list.length; i++) {
                const p = list[i];
                if (!p || p.id == null) continue;

                seen[p.id] = true;
                let el = this._elements[p.id];

                if (!el) {
                    el = document.createElement('img');
                    el.className = 'piece xb-piece';
                    el.draggable = false;
                    el.src = this.imgBasePath + (p.file || '');
                    el.alt = p.type || '';
                    this._layer.appendChild(el);

                    /* Nhớ id trên chính phần tử: _handleBoardClick đọc
                       el._id khi báo nước đi bằng click. */
                    el._id = p.id;
                    this._elements[p.id] = el;

                    el.addEventListener('pointerdown',
                        (e) => this._onPiecePointerDown(e, p.id));
                }

                el._row = p.row;
                el._col = p.col;
                el._color = p.color || this._guessColor(p);
                el.title = p.type
                    ? p.type + (el._color === 'den' ? ' (đen)' : ' (đỏ)')
                    : '';

                this._applySideClass(el, el._color);
                this._applyPosition(el, p.row, p.col, true);
            }

            /* Dọn quân không còn tồn tại (đã bị bắt) */
            const ids = Object.keys(this._elements);
            for (let i = 0; i < ids.length; i++) {
                if (!seen[ids[i]]) {
                    const dead = this._elements[ids[i]];
                    if (dead && dead.parentNode) dead.parentNode.removeChild(dead);
                    delete this._elements[ids[i]];
                    if (this._selectedId === ids[i]) this._selectedId = null;
                }
            }

            this._refreshTargets();
            this._refreshMarkers();
            this._refreshCursor();
        },

        /* Suy ra phe khi payload không có field color: id dạng "xe_den_1" */
        _guessColor(p) {
            const id = String(p.id || '');
            if (id.indexOf('den') !== -1) return 'den';
            if (id.indexOf('do') !== -1) return 'do';
            if (p.file) {
                const f = String(p.file);
                if (f.indexOf('den') !== -1) return 'den';
                if (f.indexOf('do') !== -1) return 'do';
            }
            return null;
        },

        /* Viền phân biệt phe — yêu cầu trợ năng DT-21: người mù màu vẫn
           phân biệt được quân đỏ với quân đen nhờ viền khác màu, không chỉ
           nhờ màu của hoa văn. */
        _applySideClass(el, color) {
            el.classList.remove('xb-side-red', 'xb-side-black');
            if (color === 'do') {
                el.classList.add('xb-side-red');
            } else if (color === 'den') {
                el.classList.add('xb-side-black');
            }
        },

        /* ---------------------------------------------------------------
           _applyPosition(el, row, col, animate)
           Đặt quân bằng left/top theo %. Mặc định có transition nên quân
           trượt mượt từ ô cũ sang ô mới.
           --------------------------------------------------------------- */
        _applyPosition(el, row, col, animate) {
            const p = this._cellToPct(row, col);

            el.style.left = p.x + '%';
            el.style.top = p.y + '%';
            el.style.transform = 'translate(-50%, -50%)';

            if (!animate) {
                /* Tắt transition trước khi đặt, ép trình duyệt ghi nhận vị trí
                   mới, bật lại sau: nhờ vậy lần thay đổi kế tiếp vẫn trượt mượt
                   chứ không nhảy. */
                el.classList.add('xb-noanim');
                void el.offsetWidth;
                el.classList.remove('xb-noanim');
            }
        },

        /* ===============================================================
           PHẦN TÔ Ô
           =============================================================== */

        /* ---------------------------------------------------------------
           setLegalTargets(cells)
           cells: [{ row, col }]
           Tô chấm xanh mờ ở các ô đến được.
           --------------------------------------------------------------- */
        setLegalTargets(cells) {
            this._legalTargets = (Array.isArray(cells) ? cells : [])
                .map((c) => this._normalizeCell(c))
                .filter((c) => c !== null);

            this._refreshMarkers();
        },

        /* ---------------------------------------------------------------
           setCapturable(cells)
           cells: [{ row, col }]
           Viền đỏ nhấp nháy ở ô có quân đối phủ sắp bị bắt.
           --------------------------------------------------------------- */
        setCapturable(cells) {
            this._capturable = (Array.isArray(cells) ? cells : [])
                .map((c) => this._normalizeCell(c))
                .filter((c) => c !== null);

            this._refreshMarkers();
        },

        /* ---------------------------------------------------------------
           setLegalMoves(moves)
           moves: [{ fromRow, fromCol, toRow, toCol }]  <- đúng shape do
           realtime gửi trong payload "MatchUpdated".

           Hàm này GIỮ TOÀN BỘ danh sách để khi người chơi bấm vào quân nào
           thì tự lọc ra các ô đến được của đúng quân đó. Client KHÔNG tự
           suy luận luật cờ — server là nguồn sự thật.
           --------------------------------------------------------------- */
        setLegalMoves(moves) {
            this._legalMoves = (Array.isArray(moves) ? moves : [])
                .map((m) => {
                    if (m == null) return null;
                    if (m.fromRow == null) return null;
                    return {
                        fromRow: m.fromRow, fromCol: m.fromCol,
                        toRow: m.toRow, toCol: m.toCol
                    };
                })
                .filter((m) => m !== null);

            this._refreshTargets();
            this._refreshMarkers();
        },

        /* Chấp nhận cả [{row,col}] lẫn [{fromRow,toRow,...}] */
        _normalizeCell(c) {
            if (c == null) return null;
            if (c.row != null) {
                return { row: c.row, col: c.col };
            }
            if (c.toRow != null) {
                return { row: c.toRow, col: c.toCol };
            }
            return null;
        },

        /* Tính lại tập ô cần tô, dựa trên quân đang được chọn. */
        _refreshTargets() {
            if (this._selectedId == null) {
                this._legalTargets = [];
                this._capturable = [];
                return;
            }

            const el = this._elements[this._selectedId];
            if (!el) {
                this._legalTargets = [];
                this._capturable = [];
                return;
            }

            const targets = [];
            const captures = [];

            for (let i = 0; i < this._legalMoves.length; i++) {
                const m = this._legalMoves[i];
                if (m.fromRow !== el._row || m.fromCol !== el._col) continue;

                const cell = { row: m.toRow, col: m.toCol };

                /* Cùng ô đích với ô đang đứng thì bỏ qua để không tô chấm
                   ngay dưới chân quân. */
                if (this._sameCell(cell, { row: el._row, col: el._col })) continue;

                targets.push(cell);

                /* Ô đích đang có quân đối phủ => nước bắt quân.
                   realtime không gửi riêng danh sách này, nên suy ra từ
                   legalMoves + trạng thái bàn hiện tại. */
                const occupant = this._pieceAt(cell.row, cell.col);
                if (occupant && occupant._color && el._color &&
                    occupant._color !== el._color) {
                    captures.push(cell);
                }
            }

            this._legalTargets = targets;
            this._capturable = captures;
        },

        /* ---------------------------------------------------------------
           setLastMove(from, to)
           Viền vàng/cam ở ô xuất phát và ô đích của nước vừa đi.
           --------------------------------------------------------------- */
        setLastMove(from, to) {
            this._lastMove = {
                from: this._normalizeCell(from),
                to: this._normalizeCell(to)
            };
            this._refreshMarkers();
        },

        /* ---------------------------------------------------------------
           setCheckCell(cell)
           Viền đỏ + rung nhẹ ở ô tướng đang bị chiếu. cell = {row, col} | null
           --------------------------------------------------------------- */
        setCheckCell(cell) {
            const c = this._normalizeCell(cell);
            this._checkCell = (c && c.row != null) ? c : null;
            this._refreshMarkers();
        },

        /* Xoá toàn bộ ô tô, giữ nguyên vị trí quân. */
        clearHighlights() {
            this._legalTargets = [];
            this._capturable = [];
            this._lastMove = null;
            this._checkCell = null;

            const pool = this._markers;
            for (let i = 0; i < pool.length; i++) {
                if (pool[i].parentNode) pool[i].parentNode.removeChild(pool[i]);
            }
            this._markers = [];
        },

        /* Vẽ lại toàn bộ lớp ô tô từ trạng thái hiện tại. */
        _refreshMarkers() {
            if (!this._layer) return;

            /* Dọn pool cũ */
            const pool = this._markers;
            for (let i = 0; i < pool.length; i++) {
                if (pool[i].parentNode) pool[i].parentNode.removeChild(pool[i]);
            }
            this._markers = [];

            const add = (row, col, cls) => {
                const cell = document.createElement('div');
                cell.className = 'xb-marker ' + cls;
                const p = this._cellToPct(row, col);
                cell.style.left = p.x + '%';
                cell.style.top = p.y + '%';
                this._layer.appendChild(cell);
                this._markers.push(cell);
            };

            /* Thứ tự vẽ: ô đến được (dưới) -> ô bắt được -> nước vừa đi ->
               ô tướng bị chiếu (trên cùng) */
            for (let i = 0; i < this._legalTargets.length; i++) {
                const c = this._legalTargets[i];
                if (c) add(c.row, c.col, 'xb-marker-target');
            }

            for (let i = 0; i < this._capturable.length; i++) {
                const c = this._capturable[i];
                if (c) add(c.row, c.col, 'xb-marker-capture');
            }

            if (this._lastMove) {
                if (this._lastMove.from) {
                    add(this._lastMove.from.row, this._lastMove.from.col,
                        'xb-marker-last');
                }
                if (this._lastMove.to) {
                    add(this._lastMove.to.row, this._lastMove.to.col,
                        'xb-marker-last');
                }
            }

            if (this._checkCell) {
                add(this._checkCell.row, this._checkCell.col,
                    'xb-marker-check');
            }

            /* Vòng sáng quân đang chọn (nằm trên cùng, sau các ô tô).
               Ring cũ phải được gỡ trước: _refreshMarkers chạy rất nhiều
               lần (mỗi lần chọn / mỗi nước đi), nếu không gỡ thì các
               vòng cũ tích tụ trong DOM. */
            if (this._selectionRing) {
                if (this._selectionRing.parentNode) {
                    this._selectionRing.parentNode.removeChild(this._selectionRing);
                }
                this._selectionRing = null;
            }

            if (this._selectedId != null) {
                const el = this._elements[this._selectedId];
                if (el) {
                    const ring = document.createElement('div');
                    ring.className = 'xb-selection';
                    const p = this._cellToPct(el._row, el._col);
                    ring.style.left = p.x + '%';
                    ring.style.top = p.y + '%';
                    this._layer.appendChild(ring);
                    this._selectionRing = ring;
                }
            }
        },

        /* ===============================================================
           NẠP VÁN TỪ FEN (payload "MatchUpdated" của realtime)
           =============================================================== */

        /* Ánh xạ ký tự FEN -> [loại quân, phe].
           Chữ hoa = phe ĐỎ, chữ thường = phe ĐEN.

             K = tướng (general)   R = xe      N/H = mã
             A = sĩ (advisor)      C = pháo    P = tốt
             B/E = tượng (elephant)

           HAI ĐIỂM DỄ SAI NHẤT, đã kiểm lại theo bố cục hàng 0 của dự án
           (xe mã tượng sĩ TƯỚNG sĩ tượng mã xe):

           1) K là TƯỚNG, không phải tượng. Tượng là B (hay E ở một số bộ
              FEN khác). Nếu đảo K với B thì tướng và tượng đổi hình, và số
              quân mỗi phe cũng lệch (tượng có 2 quân mỗi phe, tướng chỉ 1).

           2) Tên loại quân trong dự án: tướng = "chu_tuong" (chutuongden.svg),
              tượng = "tuong" (tuongden1.svg, tuongden2.svg). Ghi chú: ba chữ
              "tướng" trong tiếng Việt chỉ 2 loại quân khác nhau, dễ lẫn. */
        _FEN_MAP: {
            K: ['chu_tuong', 'do'], R: ['xe', 'do'],
            N: ['ma', 'do'],        A: ['si', 'do'],
            C: ['phao', 'do'],      P: ['tot', 'do'],
            B: ['tuong', 'do'],     E: ['tuong', 'do'],
            k: ['chu_tuong', 'den'], r: ['xe', 'den'],
            n: ['ma', 'den'],       a: ['si', 'den'],
            c: ['phao', 'den'],     p: ['tot', 'den'],
            b: ['tuong', 'den'],    e: ['tuong', 'den']
        },

        /* Tên file cố định cho loại quân chỉ có ĐÚNG 1 quân mỗi phe:
           chu tướng (chutuongdo.svg / chutuongden.svg).
           Tượng ("tuong") có 2 quân mỗi phe nên dùng file đánh số
           tuongdo1..2 / tuongden1..2, xử lý ở _PIECE_COUNT bên dưới. */
        _FILE_FIXED: {
            'chu_tuong': { 'do': 'chutuongdo.svg', 'den': 'chutuongden.svg' }
        },

        /* Số quân mỗi phe của từng loại, quyết định hậu tố file ảnh.
           tượng/xe/mã/sĩ/pháo đều 2, tốt 5 — khớp với file thật trong
           wwwroot/images. Chu tướng không khai ở đây vì tên file cố định. */
        _PIECE_COUNT: {
            'tuong': 2, 'xe': 2, 'ma': 2, 'si': 2, 'phao': 2, 'tot': 5
        },

        /* ---------------------------------------------------------------
           parseFen(fen) -> mảng { id, file, type, color, row, col }

           Chuyển chuỗi FEN thành danh sách quân đúng shape mà setPieces()
           nhận, để bàn cờ được vẽ lại từ trạng thái server.

           VỀ HÀNG (đã chốt với realtime, khớp engine ChessRules.CreateInitialBoard):
           hàng 0 = PHÍA ĐEN ở trên, hàng 9 = PHÍA ĐỎ ở dưới. Đây cũng đúng
           bố cục của ảnh nền và của file co_tuong_initial.json.

           Vì vậy SỐ HÀNG LẤY NGUYÊN TỪ FEN, KHÔNG ĐẢO. (Trước đây có đảo
           9 - row vì hiểu sai quy ước là hàng 0 = đỏ; đảo như vậy làm mọi quân
           nằm ngược hàng với legalMoves mà server gửi.)

           id sinh ra ỔN ĐỊNH theo thứ tự xuất hiện ("xe_den_1") để khi server
           gửi payload mới, setPieces tái dùng đúng <img> cũ => quân trượt mượt
           tới ô mới thay vì nhảy/teleport.
           --------------------------------------------------------------- */
        parseFen(fen) {
            if (!fen) return [];

            const boardField = String(fen).split(' ')[0];
            const ranks = boardField.split('/');
            const pieces = [];
            const counter = {};

            for (let f = 0; f < ranks.length; f++) {
                /* FEN xếp hàng TỪ TRÊN XUỐNG, và hàng trên cùng là hàng 0 —
                   phía ĐEN. Nên chỉ số của mảng chính là số hàng, không
                   cần đảo. (Đảo ở đây làm quân nằm ngược với legalMoves.) */
                const rowFen = f;

                const rank = ranks[f];
                let col = 0;

                for (let i = 0; i < rank.length; i++) {
                    const ch = rank[i];

                    /* Chữ số = số ô trống liên tiếp */
                    if (ch >= '1' && ch <= '9') {
                        col += parseInt(ch, 10);
                        continue;
                    }

                    const meta = this._FEN_MAP[ch];
                    if (!meta) continue;

                    const type = meta[0];
                    const color = meta[1];

                    /*
                        id của quân = thứ tự xuất hiện trong FEN, KHÔNG quay
                        vòng: giữ nó tăng đơn điệu để id ổn định giữa các
                        payload, nhờ đó setPieces tái dùng đúng <img> cũ và
                        quân trượt mượt tới ô mới thay vì nhảy/teleport.
                    */
                    const key = type + '_' + color;
                    counter[key] = (counter[key] || 0) + 1;
                    const n = counter[key];

                    /*
                        Chọn tên file ảnh:
                          - tướng / chu tướng: tên cố định, không hậu tố.
                          - còn lại: hậu tố là biến thể ảnh, KHÔNG phải số
                            thứ tự quân. Mỗi loại chỉ có 2 ảnh (tốt có 5) cho
                            mỗi phe, nên hậu tố phải quay vòng trong phạm vi đó.
                            Ví dụ phe đen có 2 pháo: hậu tố 1 rồi 2, không bao
                            giờ sinh ra "phaoden3.svg" — file đó không tồn tại
                            trong wwwroot/images và sẽ hiện ô trống.
                    */
                    let file;
                    const variants = this._PIECE_COUNT[type];
                    if (variants) {
                        const suffix = ((n - 1) % variants) + 1;
                        file = type + (color === 'den' ? 'den' : 'do') + suffix + '.svg';
                    } else {
                        const table = this._FILE_FIXED[type];
                        file = table ? table[color] : '';
                    }

                    pieces.push({
                        id: type + '_' + color + '_' + n,
                        type: type,
                        color: color,
                        file: file,
                        row: rowFen,   // hàng 0 = phía đen, giữ nguyên theo FEN
                        col: col
                    });

                    col++;
                }
            }

            return pieces;
        },

        /* ---------------------------------------------------------------
           applyMatch(payload)
           Điểm vào MỘT-LẦN cho payload "MatchUpdated" của realtime.

           payload = {
             fen, yourSide:int (0 đỏ/1 đen), turnSide:int, canMove:bool,
             legalMoves:[{fromRow,fromCol,toRow,toCol}],
             lastMove:{fromRow,fromCol,toRow,toCol}|null, status, endReason,
             youWon
           }

           Việc phát âm thanh nằm ở caller (realtime/ui glue) chứ không nằm
           trong module này, để ChessBoardView không phụ thuộc SoundBoard.
           --------------------------------------------------------------- */
        applyMatch(payload) {
            if (!payload) return;

            /* 1. Vẽ lại quân từ FEN */
            const pieces = this.parseFen(payload.fen);
            if (pieces.length) {
                this.setPieces(pieces);
            }

            /* 2. Lượt + quyền đi.
                  yourSide: 0 đỏ, 1 đen, -1 khán giả — truyền nguyên giá trị
                  xuống setTurnState để nơi đó quyết định, tránh biến -1 thành
                  phe đen do nhánh else. */
            const finished = payload.status === 'FINISHED';
            const isSpectator = payload.yourSide === -1;

            this.setTurnState({
                yourSide: payload.yourSide,
                canMove: payload.canMove === true && !finished,
                status: finished
                    ? this._endMessage(payload)
                    : (isSpectator
                        ? 'Bạn đang xem với tư cách khán giả.'
                        : (payload.canMove === false
                            ? 'Chưa đến lượt của bạn.'
                            : ''))
            });

            /* 3. Tập nước đi hợp lệ do server gửi — client không tự suy luật */
            this.setLegalMoves(payload.legalMoves);

            /* 4. Nước vừa đi */
            if (payload.lastMove) {
                this.setLastMove(
                    { row: payload.lastMove.fromRow, col: payload.lastMove.fromCol },
                    { row: payload.lastMove.toRow, col: payload.lastMove.toCol }
                );
            } else {
                this.setLastMove(null, null);
            }

            /* 5. Viền nhấp nháy quân đang cầm khi tới lượt người xem.
                  Khán giả không được viền gì. */
            this.setTurnMarker(
                !isSpectator
                && payload.yourSide != null
                && payload.turnSide === payload.yourSide
            );

            /* 6. Ô tướng đang bị chiếu.
                  Payload hiện KHÔNG có cờ này (engine xác nhận MatchUpdated chỉ
                  có status/endReason/canMove/turnSide/legalMoves), nên KHÔNG
                  tự suy trên client.

                  Vì sao không suy được từ legalMoves: tướng bị chiếu thì hẳn
                  nhiên không nước hợp lệ nào chạm tới ô của tướng — nhưng khi
                  tướng bị RỚI (đang đi) thì mọi nước cũng bị cấm, nên "không có
                  nước nào chạm tới" xảy ra y hệt ở cả hai trường hợp. Một quân
                  khác bị cờ vây dẫn tới cùng kết quả đó mà vẫn chưa bị chiếu.
                  Suy vậy sẽ vẽ viền đỏ sai.

                  Nên chỉ vẽ khi server thật sự gửi. setCheckCell() đã sẵn sàng:
                  realtime/engine thêm field là bật được ngay, không phải sửa
                  lại module. */
            this.setCheckCell(
                payload.checkSide == null
                    ? null
                    : this._kingCell(pieces, payload.checkSide)
            );
        },

        /* Vị trí tướng của một phe, tra từ danh sách quân đã parse từ FEN.
           side nhận 0 = đỏ, 1 = đen, hoặc chuỗi 'do'/'den'. */
        _kingCell(pieces, side) {
            const want = (side === 0 || side === 'do')
                ? 'do'
                : ((side === 1 || side === 'den') ? 'den' : null);

            if (!want || !Array.isArray(pieces)) return null;

            const king = pieces.find(function (p) {
                return p.type === 'chu_tuong' && p.color === want;
            });

            return king ? { row: king.row, col: king.col } : null;
        },

        /* Thông điệp kết ván theo endReason của realtime. */
        _endMessage(payload) {
            const reasons = {
                1: 'Kết thúc: chiếu tướng.',
                2: 'Kết thúc: hoà vì không nước đi nào hợp lệ.',
                3: 'Kết thúc: hoà vì lặp vị trí.',
                4: 'Kết thúc: một bên đã đầu hàng.',
                5: 'Kết thúc: hết giờ.',
                6: 'Kết thúc: đối thủ mất kết nối.',
                7: 'Kết thúc: không hoạt động quá lâu.',
                8: 'Kết thúc: hai bên đồng ý hoà.',
                9: 'Kết thúc: ván bị gián đoạn.'
            };
            const base = reasons[payload.endReason] || 'Ván đấu kết thúc.';
            if (payload.youWon === true) return base + ' Bạn thắng!';
            if (payload.youWon === false) return base + ' Bạn thua.';
            return base;
        },

        /* Viền nhấp nháy trên quân thuộc phe người xem khi tới lượt. */
        setTurnMarker(on) {
            const layer = this._layer;
            if (!layer) return;

            const existing = layer.querySelector('.xb-turn-marker');
            if (existing) existing.parentNode.removeChild(existing);

            if (!on || this.thisSide == null) return;

            const ids = Object.keys(this._elements);
            for (let i = 0; i < ids.length; i++) {
                const el = this._elements[ids[i]];
                if (el._color !== this.thisSide) continue;
                el.classList.add('xb-your-turn');
                break;
            }
        },

        /* ===============================================================
           TRẠNG THÁI LƯỢT / QUYỀN ĐI (theo "MatchUpdated" của realtime)
           =============================================================== */

        /* ---------------------------------------------------------------
           setTurnState(state)
           state = { yourSide, canMove, status }

           yourSide: phe của người xem, theo hợp đồng "MatchUpdated" của
                     realtime:  0 = đỏ, 1 = đen, -1 = khán giả.
                     Cũng chấp nhận chuỗi 'do'/'den' cho tiện khi gọi tay.
                     LƯU Ý: phải kiểm -1 TRƯỚC, vì -1 khác 0 và 1 nên nếu
                     viết if (x === 0) ... else thisSide = 'den' thì khán
                     giả sẽ bị nhận nhầm là phe đen và được quyền cầm quân.
           canMove : server đã tính sẵn (đúng lượt + đúng phe + ván còn PLAYING).
                     false => khoá mọi thao tác kéo-thả trên bàn.
           status  : dòng nhắc hiển thị dưới bàn.
           --------------------------------------------------------------- */
        setTurnState(state) {
            const s = state || {};
            const side = s.yourSide;

            if (side === -1) {
                this.thisSide = null;          // khán giả: không cầm quân nào
            } else if (side === 0 || side === 'do') {
                this.thisSide = 'do';
            } else if (side === 1 || side === 'den') {
                this.thisSide = 'den';
            } else {
                this.thisSide = null;
            }

            /* Hướng nhìn: cầm đen thì xoay bàn 180°, cầm đỏ thì bình thường,
               khán giả thì để mặc định. */
            this.setFlipped(this.thisSide === 'den');

            this.canMove = s.canMove !== false;

            if (s.status != null) {
                this.setStatus(s.status);
            } else if (!this.canMove) {
                this.setStatus(this.thisSide == null
                    ? 'Bạn đang xem với tư cách khán giả.'
                    : 'Chưa đến lượt của bạn.');
            } else {
                this.setStatus('');
            }

            this._refreshCursor();
        },

        setStatus(text) {
            this.statusText = text || '';
            if (!this.container) return;

            let el = this.container.querySelector('.xb-status');
            if (!el) {
                el = document.createElement('div');
                el.className = 'xb-status';
                this.container.appendChild(el);
            }
            el.textContent = this.statusText;
            el.style.display = this.statusText ? '' : 'none';
        },

        /* Đổi con trỏ theo quyền: được phép -> grab, không -> not-allowed */
        _refreshCursor() {
            const layer = this._layer;
            if (!layer) return;

            const ids = Object.keys(this._elements);
            for (let i = 0; i < ids.length; i++) {
                const el = this._elements[ids[i]];
                const movable = this.canMove && this._isOwn(el);
                el.classList.toggle('xb-movable', movable);
                el.style.cursor = movable ? 'grab' : 'not-allowed';
            }
        },

        _isOwn(el) {
            if (this.thisSide == null || !el || !el._color) {
                /* Chưa biết phe người xem -> cho phép thao tác, server vẫn
                   kiểm lại nên không rủi ro gian lận. */
                return true;
            }
            return el._color === this.thisSide;
        },

        /* ===============================================================
           CHỌN / BỎ CHỌN
           =============================================================== */

        _selectPiece(id) {
            if (this._selectedId === id) {
                this.clearSelection();
                return;
            }

            this._selectedId = id;
            this._refreshTargets();
            this._refreshMarkers();

            if (typeof this.onSelectionChange === 'function') {
                this.onSelectionChange(id);
            }
        },

        clearSelection() {
            const had = this._selectedId != null;
            this._selectedId = null;
            this._legalTargets = [];
            this._capturable = [];
            this._refreshMarkers();

            if (had && typeof this.onSelectionChange === 'function') {
                this.onSelectionChange(null);
            }
        },

        /* ===============================================================
           KÉO-THẢ (pointer events: dùng được cho chuột lẫn cảm ứng)
           =============================================================== */

        _onPiecePointerDown(e, id) {
            /* Chỉ chuột trái / chạm đơn */
            if (e.pointerType === 'mouse' && e.button !== 0) return;

            const el = this._elements[id];
            if (!el) return;

            if (!this.canMove || !this._isOwn(el)) {
                /* Kéo quân của đối thủ hoặc chưa đến lượt: từ chối ngay,
                   không vào được chế độ kéo nên không có gì phải trả lại. */
                this.setStatus(this.canMove
                    ? 'Bạn chỉ được cầm quân của phe mình.'
                    : 'Chưa đến lượt của bạn.');
                if (typeof e.preventDefault === 'function') e.preventDefault();
                return;
            }

            const rect = this.container.getBoundingClientRect();
            const pieceRect = el.getBoundingClientRect();

            this._dragState = {
                id: id,
                el: el,
                startX: e.clientX,
                startY: e.clientY,
                /* Giữ nguyên khoảng cách giữa điểm cầm và tâm quân */
                grabX: e.clientX - (pieceRect.left + pieceRect.width / 2),
                grabY: e.clientY - (pieceRect.top + pieceRect.height / 2),
                originRow: el._row,
                originCol: el._col,
                rect: rect,
                halfW: pieceRect.width / 2,
                halfH: pieceRect.height / 2,
                moved: false
            };

            this._moveHandler = (ev) => this._onPointerMove(ev);
            this._upHandler = (ev) => this._onPointerUp(ev);

            global.addEventListener('pointermove', this._moveHandler);
            global.addEventListener('pointerup', this._upHandler);
            global.addEventListener('pointercancel', this._upHandler);

            if (typeof e.preventDefault === 'function') e.preventDefault();
        },

        _onPointerMove(e) {
            const st = this._dragState;
            if (!st) return;

            const dx = e.clientX - st.startX;
            const dy = e.clientY - st.startY;

            if (!st.moved &&
                Math.abs(dx) < this.dragThreshold &&
                Math.abs(dy) < this.dragThreshold) {
                /* Chưa vượt ngưỡng -> coi như chưa kéo, để xử lý ở pointerup */
                return;
            }

            st.moved = true;
            st.el.classList.add('xb-dragging');

            /* Kẹp quân trong khung bàn: không cho trượt ra ngoài */
            const maxDX = st.rect.width - st.halfW;
            const maxDY = st.rect.height - st.halfH;
            const cdx = Math.max(-st.halfW, Math.min(maxDX, dx + st.grabX));
            const cdy = Math.max(-st.halfH, Math.min(maxDY, dy + st.grabY));

            st.currentX = cdx;
            st.currentY = cdy;

            st.el.style.transform =
                'translate(calc(-50% + ' + cdx + 'px), calc(-50% + ' + cdy + 'px))';
        },

        _onPointerUp(e) {
            const st = this._dragState;
            if (!st) return;

            this._endDrag();

            if (!st.moved) {
                /* Không kéo -> đây là CLICK-TO-MOVE: chọn/bỏ chọn quân */
                this._selectPiece(st.id);
                return;
            }

            const target = this._pointToCell(e.clientX, e.clientY);

            const valid = target !== null
                && !this._sameCell(target, { row: st.originRow, col: st.originCol })
                && this._isLegalTarget(st.originRow, st.originCol, target);

            if (!valid) {
                /* Thả sai chỗ -> quân TRƯỢT MƯỢT về ô cũ (không teleport) */
                this._applyPosition(st.el, st.originRow, st.originCol, true);
                return;
            }

            /* Thả hợp lệ -> đặt quân vào ô đích, vẫn chuyển động mượt */
            this._applyPosition(st.el, target.row, target.col, true);
            st.el._row = target.row;
            st.el._col = target.col;

            this.clearSelection();

            if (typeof this.onMove === 'function') {
                this.onMove(
                    { row: st.originRow, col: st.originCol },
                    { row: target.row, col: target.col },
                    st.id
                );
            }
        },

        _endDrag() {
            const st = this._dragState;
            if (!st) return;

            global.removeEventListener('pointermove', this._moveHandler);
            global.removeEventListener('pointerup', this._upHandler);
            global.removeEventListener('pointercancel', this._upHandler);

            st.el.classList.remove('xb-dragging');
            this._dragState = null;
        },

        _isLegalTarget(fromRow, fromCol, cell) {
            const list = this._legalMoves.length
                ? this._legalMoves
                : null;

            if (list) {
                for (let i = 0; i < list.length; i++) {
                    const m = list[i];
                    if (m.fromRow === fromRow && m.fromCol === fromCol &&
                        m.toRow === cell.row && m.toCol === cell.col) {
                        return true;
                    }
                }
                return false;
            }

            /* Không có legalMoves (chế độ demo) -> chỉ chặn đứng yên tại chỗ */
            return true;
        },

        /* ===============================================================
           CLICK-TO-MOVE
           =============================================================== */

        _handleBoardClick(e) {
            /* Click trúng quân đã được xử lý ở _selectPiece */
            if (e.target && e.target.classList &&
                e.target.classList.contains('xb-piece')) {
                return;
            }

            const cell = this._pointToCell(e.clientX, e.clientY);

            if (this._selectedId == null) return;

            if (cell === null) {
                /* Nhấp ra vùng trống ngoài bàn cờ -> huỷ chọn */
                this.clearSelection();
                return;
            }

            const el = this._elements[this._selectedId];
            if (!el) {
                this.clearSelection();
                return;
            }

            const from = { row: el._row, col: el._col };

            if (this._sameCell(cell, from)) {
                /* Click lại quân đang chọn -> huỷ chọn */
                this.clearSelection();
                return;
            }

            if (!this._isLegalTarget(from.row, from.col, cell)) {
                /* Click ô không hợp lệ -> huỷ chọn, không gửi nước đi */
                this.clearSelection();
                return;
            }

            this._applyPosition(el, cell.row, cell.col, true);
            el._row = cell.row;
            el._col = cell.col;
            this.clearSelection();

            if (typeof this.onMove === 'function') {
                this.onMove(from, { row: cell.row, col: cell.col }, el._id);
            }
        },

        /* ---------------------------------------------------------------
           rejectMove(pieceId)
           Server từ chối nước đi: đưa quân về đúng ô server vừa gửi lại.
           Thực tế thường chỉ cần gọi setPieces(match) — hàm này dành cho
           trường hợp muốn trượt ngược ngay mà chưa có payload mới.
           --------------------------------------------------------------- */
        rejectMove(pieceId) {
            const el = this._elements[pieceId];
            if (!el) return;
            this._applyPosition(el, el._row, el._col, true);
        },

        /* ===============================================================
           DỌN DẸP
           =============================================================== */

        destroy() {
            this._endDrag();
            this.clearSelection();

            if (this.container && this._onBoardClick) {
                this.container.removeEventListener('click', this._onBoardClick);
            }
            if (this._keyHandler) {
                document.removeEventListener('keydown', this._keyHandler);
            }

            this._elements = {};
            this._markers = [];
            this._selectionRing = null;
            this._layer = null;
            this._onBoardClick = null;
            this._keyHandler = null;
            this._moveHandler = null;
            this._upHandler = null;

            if (this.container) {
                this.container.innerHTML = '';
            }

            this.container = null;
        }
    };

    global.ChessBoardView = ChessBoardView;
})(window);
