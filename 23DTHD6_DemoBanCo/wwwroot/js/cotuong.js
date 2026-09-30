const { createApp } = Vue;

createApp({

    data() {
        return {

            rows: 10,
            cols: 9,

            pieces: [],

            selectedPiece: null,


            userName: "Player 1",

            chatMessage: "",

            // Chat lấy từ window.__chatMessages: trang (Index.cshtml / Waiting.cshtml)
            // là nơi duy nhất đăng ký handler 'ReceiveChat' và đẩy tin vào đó, nên
            // panel Vue chỉ cần đọc lại đúng mảng đó. Trước đây Vue giữ `messages`
            // riêng nên tin nhắn hiện trên DOM nhưng không vào panel.
            messages: [],

            // Hook do trang gọi sau khi đẩy tin mới, để panel tự cập nhật.
            onChatReceived: null,

            pendingIceCandidates: [],
            localStream: null,

            peerConnection: null,

            cameraEnabled: true,

            microphoneEnabled: true,


            // SignalR
            connection: null,

            // Id phòng lấy theo thứ tự ưu tiên: biến trang truyền vào, rồi query
            // string ?id=, rồi path dạng /Room/Waiting/5. Nếu không lấy được thì
            // để rỗng để bỏ qua JoinRoom, thay vì gửi roomId sai.
            roomId: window.__roomId
                || new URLSearchParams(window.location.search).get('id')
                || (function () {
                    const m = window.location.pathname.match(/\/Waiting\/(\d+)/);
                    return m ? m[1] : '';
                })(),

            grid: {
                left: 15.5,
                right: 84.5,
                top: 10.5,
                bottom: 91
            }
        };
    },


    async mounted() {

        // Gom chat về MỘT nguồn: window.__chatMessages do trang đẩy vào khi có
        // tin mới. Gán hook ngay ở mounted (trước khi nối SignalR) để không bỏ
        // sót tin nào đến sớm.
        this.messages = window.__chatMessages || [];

        this.onChatReceived = (list) => {

            this.messages = list || window.__chatMessages || [];

            this.$nextTick(() => {

                const chat = this.$refs.chatMessages;

                if (chat) {
                    chat.scrollTop = chat.scrollHeight;
                }

            });

        };

        window.__onChatReceived = this.onChatReceived;

        await this.loadBoard();
        this.connectSignalR();

    },


    methods: {

        // ==========================
        // LOAD DATA
        // ==========================
        async initMedia() {

            try {

                this.localStream =
                    await navigator.mediaDevices
                        .getUserMedia({

                            video: true,

                            audio: true

                        });


                this.$refs.localVideo.srcObject =
                    this.localStream;


                console.log(
                    "Camera + microphone ready"
                );

            }
            catch (error) {

                console.error(
                    "Không mở được camera:",
                    error
                );

            }

        },
        async startCall() {

            if (!this.localStream) {

                await this.initMedia();

            }


            this.createPeerConnection();


            const offer =
                await this.peerConnection
                    .createOffer();


            await this.peerConnection
                .setLocalDescription(
                    offer
                );


            await this.connection.invoke(

                "SendOffer",

                this.roomId,

                JSON.stringify(offer)

            );

        },
        createPeerConnection() {

            if (this.peerConnection)
                return;


            this.peerConnection =
                new RTCPeerConnection({

                    iceServers: [

                        {
                            urls:
                                "stun:stun.l.google.com:19302"
                        }

                    ]

                });


            // =====================
            // LOCAL -> REMOTE
            // =====================

            if (this.localStream) {

                this.localStream
                    .getTracks()
                    .forEach(track => {

                        this.peerConnection
                            .addTrack(
                                track,
                                this.localStream
                            );

                    });

            }


            // =====================
            // NHẬN VIDEO REMOTE
            // =====================

            this.peerConnection.ontrack =
                (event) => {

                    console.log(
                        "Received remote stream"
                    );

                    this.$refs.remoteVideo.srcObject =
                        event.streams[0];

                };


            // =====================
            // ICE
            // =====================

            this.peerConnection.onicecandidate =
                async (event) => {

                    if (!event.candidate)
                        return;


                    await this.connection.invoke(

                        "SendIceCandidate",

                        this.roomId,

                        JSON.stringify(
                            event.candidate
                        )

                    );

                };

        },
        async flushIceCandidates() {

            if (
                !this.peerConnection ||
                !this.peerConnection.remoteDescription
            ) {
                return;
            }


            console.log(
                "Processing pending ICE:",
                this.pendingIceCandidates.length
            );


            while (
                this.pendingIceCandidates.length > 0
            ) {

                const candidate =
                    this.pendingIceCandidates.shift();


                try {

                    await this.peerConnection
                        .addIceCandidate(candidate);

                }
                catch (error) {

                    console.error(
                        "Add pending ICE error:",
                        error
                    );

                }

            }

        },
        connectSignalR() {

            this.connection =
                new signalR.HubConnectionBuilder()
                    .withUrl("/chessHub")
                    .withAutomaticReconnect()
                    .build();


            // =========================
            // Nhận nước đi
            // =========================

            this.connection.on(
                "ReceiveMove",
                (move) => {

                    console.log(
                        "ReceiveMove:",
                        move
                    );

                    this.applyMove(move);

                }
            );
            /* Chat không đăng ký handler ở đây: ChessHub phát "ReceiveChat" với MỘT
               object, còn handler cũ "ReceiveMessage" nhận 2 tham số nên không bao
               giờ khớp. Trang (Index.cshtml / Waiting.cshtml) là nơi duy nhất đăng
               ký, đẩy tin vào window.__chatMessages rồi gọi window.__onChatReceived
               mà mounted() đã gán sẵn. */

            this.connection.on(
                "ReceiveOffer",

                async (offerJson) => {

                    console.log(
                        "Receive WebRTC Offer"
                    );


                    if (!this.localStream) {

                        await this.initMedia();

                    }


                    this.createPeerConnection();


                    const offer =
                        JSON.parse(
                            offerJson
                        );


                    await this.peerConnection
                        .setRemoteDescription(
                            new RTCSessionDescription(
                                offer
                            )
                        );
                    await this.flushIceCandidates();

                    const answer =
                        await this.peerConnection
                            .createAnswer();


                    await this.peerConnection
                        .setLocalDescription(
                            answer
                        );


                    await this.connection.invoke(

                        "SendAnswer",

                        this.roomId,

                        JSON.stringify(answer)

                    );

                }
            );
            this.connection.on(
                "ReceiveOffer",

                async (offerJson) => {

                    try {

                        console.log(
                            "Receive WebRTC Offer"
                        );


                        if (!this.localStream) {

                            await this.initMedia();

                        }


                        this.createPeerConnection();


                        const offer =
                            JSON.parse(offerJson);


                        // ========================
                        // SET REMOTE OFFER
                        // ========================

                        await this.peerConnection
                            .setRemoteDescription(
                                new RTCSessionDescription(
                                    offer
                                )
                            );


                        // ========================
                        // ADD ICE ĐANG CHỜ
                        // ========================

                        await this.flushIceCandidates();


                        // ========================
                        // CREATE ANSWER
                        // ========================

                        const answer =
                            await this.peerConnection
                                .createAnswer();


                        await this.peerConnection
                            .setLocalDescription(
                                answer
                            );


                        // ========================
                        // SEND ANSWER
                        // ========================

                        await this.connection.invoke(

                            "SendAnswer",

                            this.roomId,

                            JSON.stringify(answer)

                        );

                    }
                    catch (error) {

                        console.error(
                            "ReceiveOffer error:",
                            error
                        );

                    }

                }
            );
            this.connection.on(
                "ReceiveAnswer",

                async (answerJson) => {

                    try {

                        console.log(
                            "Receive WebRTC Answer"
                        );


                        const answer =
                            JSON.parse(answerJson);


                        await this.peerConnection
                            .setRemoteDescription(
                                new RTCSessionDescription(
                                    answer
                                )
                            );


                        // remoteDescription đã tồn tại
                        // -> xử lý ICE đang chờ
                        await this.flushIceCandidates();


                        console.log(
                            "Remote Answer set"
                        );

                    }
                    catch (error) {

                        console.error(
                            "ReceiveAnswer error:",
                            error
                        );

                    }

                }
            );
            this.connection.on(
                "ReceiveIceCandidate",

                async (candidateJson) => {

                    try {

                        const candidateData =
                            JSON.parse(candidateJson);

                        const candidate =
                            new RTCIceCandidate(
                                candidateData
                            );


                        // PeerConnection chưa tồn tại
                        // hoặc chưa nhận Offer/Answer
                        if (
                            !this.peerConnection ||
                            !this.peerConnection.remoteDescription
                        ) {

                            console.log(
                                "ICE đến sớm -> đưa vào queue"
                            );

                            this.pendingIceCandidates.push(
                                candidate
                            );

                            return;
                        }


                        // Đã có remoteDescription
                        await this.peerConnection
                            .addIceCandidate(candidate);


                        console.log(
                            "ICE candidate added"
                        );

                    }
                    catch (error) {

                        console.error(
                            "ICE error:",
                            error
                        );

                    }

                }
            );

            // =========================
            // Connect + vào phòng
            // =========================

            // Trang không thuộc phòng nào (ví dụ /Room/Lobby) thì chỉ kết nối,
            // KHÔNG invoke JoinRoom: gửi roomId rỗng sẽ ra lỗi
            // "Cannot send data if the connection is not in the Connected State".
            const roomId = parseInt(this.roomId, 10);

            this.connection.start()
                .then(() => {
                    if (!roomId || roomId <= 0) {
                        console.log("Không có phòng, bỏ qua JoinRoom");
                        return null;
                    }
                    return this.connection.invoke("JoinRoom", roomId, null);
                })
                .then(() => console.log("Joined:", roomId || "(không có phòng)"))
                .catch((err) => console.error("SignalR error:", err));
        },
        applyMove(move) {

            const piece =
                this.pieces.find(
                    p => p.id === move.pieceId
                );

            if (!piece) {
                console.error(
                    "Không tìm thấy quân:",
                    move.pieceId
                );

                return;
            }


            // Kiểm tra quân tại vị trí đích
            const capturedPiece =
                this.pieces.find(
                    p =>
                        p.row === move.toRow &&
                        p.col === move.toCol &&
                        p.id !== move.pieceId
                );


            // Nếu có -> loại khỏi bàn
            if (capturedPiece) {

                this.pieces =
                    this.pieces.filter(
                        p => p.id !== capturedPiece.id
                    );

            }


            // Di chuyển
            piece.row = move.toRow;
            piece.col = move.toCol;
        },
        async loadBoard() {

            try {

                const response =
                    await fetch("/api/BanCo/getboard");

                if (!response.ok) {
                    throw new Error(
                        "Không lấy được dữ liệu bàn cờ"
                    );
                }

                const data =
                    await response.json();

                this.pieces =
                    data.pieces;

            }
            catch (error) {

                console.error(
                    "Load board error:",
                    error
                );

            }
        },
        toggleMicrophone() {

            if (!this.localStream)
                return;


            const tracks =
                this.localStream
                    .getAudioTracks();


            tracks.forEach(track => {

                track.enabled =
                    !track.enabled;

                this.microphoneEnabled =
                    track.enabled;

            });

        },
        toggleCamera() {

            if (!this.localStream)
                return;


            const tracks =
                this.localStream
                    .getVideoTracks();


            tracks.forEach(track => {

                track.enabled =
                    !track.enabled;

                this.cameraEnabled =
                    track.enabled;

            });

        },
        endCall() {

            if (this.peerConnection) {

                this.peerConnection.close();

                this.peerConnection = null;

            }


            if (this.localStream) {

                this.localStream
                    .getTracks()
                    .forEach(track => {

                        track.stop();

                    });


                this.localStream = null;

            }


            if (this.$refs.localVideo) {

                this.$refs.localVideo.srcObject =
                    null;

            }


            if (this.$refs.remoteVideo) {

                this.$refs.remoteVideo.srcObject =
                    null;

            }

        },

        // ==========================
        // TÍNH VỊ TRÍ QUÂN CỜ
        // ==========================

        getPieceStyle(piece) {

            const width =
                this.grid.right -
                this.grid.left;

            const height =
                this.grid.bottom -
                this.grid.top;


            const x =
                this.grid.left +
                piece.col *
                width /
                (this.cols - 1);


            const y =
                this.grid.top +
                piece.row *
                height /
                (this.rows - 1);


            return {

                left: x + "%",

                top: y + "%"

            };
        },


        // ==========================
        // CHỌN QUÂN
        // ==========================

        selectPiece(piece) {

            this.selectedPiece =
                piece;

            console.log(
                "Selected:",
                piece
            );

        },


        // ==========================
        // CLICK BÀN CỜ
        // ==========================

        moveTo(event) {

            if (!this.selectedPiece)
                return;


            const board =
                this.$refs.board;


            const rect =
                board.getBoundingClientRect();


            /*
                Tọa độ pixel click
                bên trong bàn cờ
            */

            const mouseX =
                event.clientX -
                rect.left;

            const mouseY =
                event.clientY -
                rect.top;


            /*
                Chuyển sang %
            */

            const percentX =
                mouseX /
                rect.width *
                100;


            const percentY =
                mouseY /
                rect.height *
                100;


            /*
                Chuyển tọa độ %
                sang row / col
            */

            let col =
                Math.round(

                    (percentX -
                        this.grid.left)

                    /

                    (this.grid.right -
                        this.grid.left)

                    *

                    (this.cols - 1)

                );


            let row =
                Math.round(

                    (percentY -
                        this.grid.top)

                    /

                    (this.grid.bottom -
                        this.grid.top)

                    *

                    (this.rows - 1)

                );


            /*
                Không cho vượt bàn cờ
            */

            col =
                Math.max(
                    0,
                    Math.min(
                        this.cols - 1,
                        col
                    )
                );


            row =
                Math.max(
                    0,
                    Math.min(
                        this.rows - 1,
                        row
                    )
                );


            this.movePiece(
                this.selectedPiece,
                row,
                col
            );

        },


        // ==========================
        // DI CHUYỂN
        // ==========================

         movePiece(piece, row, col) {

            const fromRow = piece.row;
            const fromCol = piece.col;


            this.connection.invoke(
                "MovePiece",

                this.roomId,

                piece.id,

                fromRow,
                fromCol,

                row,
                col
            );


            this.selectedPiece = null;
        },

        async sendMessage() {

            const message =
                this.chatMessage.trim();


            if (message === "")
                return;


            if (!this.connection)
                return;


            // Chat thuộc phòng: không có roomId hợp lệ thì không gửi, vì hub
            // cần roomId đúng và sẽ từ chối.
            const roomId = parseInt(this.roomId, 10);
            if (!roomId || roomId <= 0)
                return;


            // Hub có hàm SendChat(roomId, channel, body, sticker), không có
            // "SendMessage" — gọi tên cũ sẽ không tồn tại. channel 0 = phòng chung.
            await this.connection.invoke(
                "SendChat",

                parseInt(this.roomId, 10) || 0,
                0,
                message,
                null
            );


            // Xóa textbox
            this.chatMessage = "";

        }
    }

}).mount("#app");