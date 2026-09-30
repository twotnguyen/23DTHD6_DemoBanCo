/* ==========================================================================
   audio.js — Hiệu ứng âm thanh tổng hợp bằng Web Audio API.

   KHÔNG tải file MP3 nào: mọi tiếng đều được dựng trực tiếp từ OscillatorNode
   + GainNode + BiquadFilter. Nhờ vậy không có độ trễ tải, không phụ thuộc
   mạng, và dung lượng bản build không tăng.

   Cài đặt:
       <script src="/js/audio.js"></script>

   Dùng:
       SoundBoard.playMove();
       SoundBoard.setMuted(true);

   Mọi hàm đều an toàn (no-op) khi AudioContext chưa sẵn sàng.
   ========================================================================== */
(function (global) {
    'use strict';

    const SoundBoard = {
        ctx: null,
        muted: false,

        /* ---------------------------------------------------------------
           _ensureCtx: lấy (hoặc tạo) AudioContext.

           Trình duyệt chặn AudioContext cho tới khi người dùng tương tác
           lần đầu. Vì vậy mỗi hàm phát âm đều gọi resume() trước khi tạo
           node — nếu người dùng vừa bấm chuột thì context sẽ chuyển sang
           trạng thái "running" và âm thanh phát được. Nếu resume() bị từ
           chối (chưa có tương tác nào) ta nuốt lỗi, KHÔNG ném exception ra
           ngoài — âm thanh chỉ là hiệu ứng, không được phép làm hỏng game.
           --------------------------------------------------------------- */
        _ensureCtx() {
            try {
                if (!this.ctx) {
                    const AC = global.AudioContext || global.webkitAudioContext;
                    if (!AC) return null;

                    this.ctx = new AC();
                }

                if (this.ctx.state === 'suspended') {
                    const resumed = this.ctx.resume();
                    if (resumed && typeof resumed.catch === 'function') {
                        resumed.catch(function () { /* chưa có tương tác người dùng */ });
                    }
                }

                return this.ctx;
            } catch (e) {
                return null;
            }
        },

        /* ---------------------------------------------------------------
           _voice: hạt âm cơ bản dùng chung cho mọi tiếng.

           freq      – tần số nốt (Hz)
           type      – dạng sóng ('sine' | 'triangle' | 'square' | 'sawtooth')
           gain      – đỉnh âm lượng
           startAt   – thời điểm bắt đầu (s, theo ctx.currentTime)
           duration  – độ dài (s)
           filterFreq– tần số cắt của bộ lọc thông thấp (làm âm nghe ấm kiểu gỗ)
           --------------------------------------------------------------- */
        _voice(ctx, dest, opts) {
            const t0 = opts.startAt;
            const osc = ctx.createOscillator();
            const filter = ctx.createBiquadFilter();
            const gain = ctx.createGain();

            osc.type = opts.type || 'triangle';
            osc.frequency.setValueAtTime(opts.freq, t0);

            /*
                Bộ lọc thông thấp: cắt bớt phần cao chói, chỉ để lại dải
                trầm gần với tiếng gõ mộc. Q = 1.2 đủ nhạy mà không bị cộng hưởng.
            */
            filter.type = 'lowpass';
            filter.frequency.setValueAtTime(opts.filterFreq, t0);
            filter.Q.setValueAtTime(1.2, t0);

            /*
                Envelope hình chữ V ngược:
                    0 -> peak -> gần 0
                Dùng setValueAtTime(0) rồi exponentialRampToValueAtTime lên đỉnh,
                sau đó ramp xuống 0.0001 (không dùng 0 vì exponentialRampToValueAtTime
                không chấp nhận 0). Kết quả: không có tiếng "click" ở đầu/cuối.
            */
            gain.gain.setValueAtTime(0.0001, t0);
            gain.gain.exponentialRampToValueAtTime(opts.gain, t0 + 0.006);
            gain.gain.exponentialRampToValueAtTime(0.0001, t0 + opts.duration);

            osc.connect(filter);
            filter.connect(gain);
            gain.connect(dest);

            osc.start(t0);
            osc.stop(t0 + opts.duration + 0.02);
        },

        /* ---------------------------------------------------------------
           _noise: một xung nhiễu ngắn, dùng để tạo cảm giác "va chạm".
           Buffer sin sẵn 0.12 giây, lọc thông dải trung để nghe như gõ mạnh.
           --------------------------------------------------------------- */
        _noise(ctx, dest, startAt, gainPeak, filterFreq) {
            const frames = Math.floor(ctx.sampleRate * 0.12);
            const buffer = ctx.createBuffer(1, frames, ctx.sampleRate);
            const data = buffer.getChannelData(0);

            for (let i = 0; i < frames; i++) {
                /* Taper dần về 0 ở cuối để không bị cắt cụt */
                const decay = 1 - (i / frames);
                data[i] = (Math.random() * 2 - 1) * decay;
            }

            const src = ctx.createBufferSource();
            src.buffer = buffer;

            const filter = ctx.createBiquadFilter();
            filter.type = 'bandpass';
            filter.frequency.setValueAtTime(filterFreq, startAt);
            filter.Q.setValueAtTime(0.8, startAt);

            const gain = ctx.createGain();
            gain.gain.setValueAtTime(0.0001, startAt);
            gain.gain.exponentialRampToValueAtTime(gainPeak, startAt + 0.004);
            gain.gain.exponentialRampToValueAtTime(0.0001, startAt + 0.12);

            src.connect(filter);
            filter.connect(gain);
            gain.connect(dest);

            src.start(startAt);
            src.stop(startAt + 0.14);
        },

        /* ---------------------------------------------------------------
           playMove — tiếng gõ quân xuống bàn: nốt thấp, ngắn, êm.
           --------------------------------------------------------------- */
        playMove() {
            if (this.muted) return;

            const ctx = this._ensureCtx();
            if (!ctx) return;

            try {
                const t0 = ctx.currentTime;
                const dest = ctx.destination;

                this._voice(ctx, dest, {
                    startAt: t0,
                    freq: 210,
                    type: 'triangle',
                    gain: 0.22,
                    duration: 0.16,
                    filterFreq: 1400
                });

                /* Lớp thứ hai cao hơn một chút, tạo cảm giác "cộng hưởng" */
                this._voice(ctx, dest, {
                    startAt: t0,
                    freq: 320,
                    type: 'sine',
                    gain: 0.10,
                    duration: 0.11,
                    filterFreq: 2200
                });
            } catch (e) { /* âm thanh là hiệu ứng, nuốt lỗi */ }
        },

        /* ---------------------------------------------------------------
           playCapture — bắt quân: đậm hơn hẳn playMove.
           = gain cao gấp đôi + thêm một xung nhiễu (tiếng va chạm) +
             hai oscillator chồng nhau ở hai tần số khác nhau.
           --------------------------------------------------------------- */
        playCapture() {
            if (this.muted) return;

            const ctx = this._ensureCtx();
            if (!ctx) return;

            try {
                const t0 = ctx.currentTime;
                const dest = ctx.destination;

                /* Thân âm trầm, đặc */
                this._voice(ctx, dest, {
                    startAt: t0,
                    freq: 150,
                    type: 'triangle',
                    gain: 0.40,
                    duration: 0.22,
                    filterFreq: 1000
                });

                /* Lớp nửa tần số thấp, tạo cảm giác nặng */
                this._voice(ctx, dest, {
                    startAt: t0 + 0.01,
                    freq: 92,
                    type: 'sine',
                    gain: 0.26,
                    duration: 0.26,
                    filterFreq: 700
                });

                /* Xung nhiễu = tiếng "cạch" của hai quân va vào nhau */
                this._noise(ctx, dest, t0, 0.30, 2400);
            } catch (e) { /* nuốt lỗi */ }
        },

        /* ---------------------------------------------------------------
           playCheck — chuông cảnh báo tướng bị chiếu: hai nốt chuông trầm.
           --------------------------------------------------------------- */
        playCheck() {
            if (this.muted) return;

            const ctx = this._ensureCtx();
            if (!ctx) return;

            try {
                const t0 = ctx.currentTime;
                const dest = ctx.destination;

                this._voice(ctx, dest, {
                    startAt: t0,
                    freq: 660,
                    type: 'sine',
                    gain: 0.20,
                    duration: 0.30,
                    filterFreq: 2600
                });

                this._voice(ctx, dest, {
                    startAt: t0 + 0.16,
                    freq: 495,
                    type: 'sine',
                    gain: 0.18,
                    duration: 0.36,
                    filterFreq: 2200
                });
            } catch (e) { /* nuốt lỗi */ }
        },

        /* ---------------------------------------------------------------
           playEndGame — hồi còi kết thúc: hợp âm bậc thứ hai đi xuống.
           --------------------------------------------------------------- */
        playEndGame() {
            if (this.muted) return;

            const ctx = this._ensureCtx();
            if (!ctx) return;

            try {
                const t0 = ctx.currentTime;
                const dest = ctx.destination;

                /* 523.25 = C5, 392.00 = G4 */
                this._voice(ctx, dest, {
                    startAt: t0,
                    freq: 523.25,
                    type: 'sine',
                    gain: 0.22,
                    duration: 0.34,
                    filterFreq: 2800
                });

                this._voice(ctx, dest, {
                    startAt: t0,
                    freq: 392.00,
                    type: 'sine',
                    gain: 0.20,
                    duration: 0.34,
                    filterFreq: 2400
                });

                this._voice(ctx, dest, {
                    startAt: t0 + 0.26,
                    freq: 261.63,
                    type: 'triangle',
                    gain: 0.22,
                    duration: 0.62,
                    filterFreq: 1600
                });
            } catch (e) { /* nuốt lỗi */ }
        },

        setMuted(v) {
            this.muted = !!v;
        },

        isMuted() {
            return this.muted;
        }
    };

    global.SoundBoard = SoundBoard;
})(window);
