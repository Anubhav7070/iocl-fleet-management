window.qrScanner = {
    stream: null,
    video: null,
    canvas: null,
    animationFrameId: null,
    dotNetHelper: null,

    start: async function (videoElementId, canvasElementId, dotNetHelper) {
        this.dotNetHelper = dotNetHelper;
        this.video = document.getElementById(videoElementId);
        this.canvas = document.getElementById(canvasElementId);

        if (!this.video || !this.canvas) return;

        if (!window.jsQR) {
            await new Promise((resolve) => {
                const script = document.createElement('script');
                script.src = 'https://cdn.jsdelivr.net/npm/jsqr@1.4.0/dist/jsQR.js';
                script.onload = resolve;
                document.head.appendChild(script);
            });
        }

        try {
            this.stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' } });
            this.video.srcObject = this.stream;
            this.video.setAttribute('playsinline', 'true');
            await this.video.play();
            this.tick();
        } catch (err) {
            console.error('[QRScanner] Camera access error:', err);
            this.dotNetHelper.invokeMethodAsync('OnCameraError', 'Camera access denied. Please allow camera permissions and try again.');
        }
    },

    tick: function () {
        if (!this.video || !this.canvas || !window.jsQR) return;

        if (this.video.readyState === this.video.HAVE_ENOUGH_DATA) {
            this.canvas.height = this.video.videoHeight;
            this.canvas.width = this.video.videoWidth;
            const ctx = this.canvas.getContext('2d');
            ctx.drawImage(this.video, 0, 0, this.canvas.width, this.canvas.height);
            const imageData = ctx.getImageData(0, 0, this.canvas.width, this.canvas.height);
            const code = window.jsQR(imageData.data, imageData.width, imageData.height, { inversionAttempts: 'dontInvert' });

            if (code) {
                // Expected URL pattern matches /verify/vehicle/{id} or /verify/{id}
                const match = code.data.match(/\/verify\/(?:vehicle\/)?(\d+)/);
                if (match) {
                    const vehicleId = parseInt(match[1]);
                    this.stop();
                    this.dotNetHelper.invokeMethodAsync('OnQrCodeScanned', vehicleId);
                    return;
                }
            }
        }
        this.animationFrameId = requestAnimationFrame(this.tick.bind(this));
    },

    stop: function () {
        if (this.animationFrameId) {
            cancelAnimationFrame(this.animationFrameId);
            this.animationFrameId = null;
        }
        if (this.stream) {
            this.stream.getTracks().forEach(t => t.stop());
            this.stream = null;
        }
        if (this.video) {
            this.video.srcObject = null;
        }
    }
};
