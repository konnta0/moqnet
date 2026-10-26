const targetRate = 48000;
const frameSamples = 960;
const maxPlaybackQueue = 12;

let capture = null;
let playback = null;

function floatToBytes(samples) {
    return new Uint8Array(samples.buffer.slice(samples.byteOffset, samples.byteOffset + samples.byteLength));
}

function resample(input, fromRate, toRate) {
    if (fromRate === toRate) {
        return input;
    }

    const ratio = fromRate / toRate;
    const out = new Float32Array(Math.max(1, Math.round(input.length / ratio)));
    for (let i = 0; i < out.length; i++) {
        const src = i * ratio;
        const i0 = Math.min(input.length - 1, Math.floor(src));
        const i1 = Math.min(input.length - 1, i0 + 1);
        const f = src - i0;
        out[i] = input[i0] + ((input[i1] - input[i0]) * f);
    }

    return out;
}

async function createWorklet(context, name, source) {
    const blob = new Blob([source], { type: "application/javascript" });
    const url = URL.createObjectURL(blob);
    try {
        await context.audioWorklet.addModule(url);
    } finally {
        URL.revokeObjectURL(url);
    }

    return new AudioWorkletNode(context, name);
}

export async function startCapture(dotNet, width, height, fps) {
    await stop();

    const video = document.getElementById("local-preview");
    const canvas = document.getElementById("capture-canvas");
    const context2d = canvas.getContext("2d", { willReadFrequently: true });
    let stream = null;
    let sourceName = "generated fallback";

    try {
        stream = await navigator.mediaDevices.getUserMedia({
            audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true },
            video: { width: { ideal: width }, height: { ideal: height }, frameRate: { ideal: fps } }
        });
        sourceName = "camera + microphone";
    } catch {
        stream = null;
    }

    const audioContext = new AudioContext({ sampleRate: targetRate });
    await audioContext.resume();
    const captureNode = await createWorklet(audioContext, "moq-capture", `
        class MoqCapture extends AudioWorkletProcessor {
            process(inputs) {
                const channel = inputs[0] && inputs[0][0];
                if (channel && channel.length) {
                    this.port.postMessage(channel);
                }
                return true;
            }
        }
        registerProcessor("moq-capture", MoqCapture);
    `);

    let pending = new Float32Array(0);
    captureNode.port.onmessage = (event) => {
        const resampled = resample(event.data, audioContext.sampleRate, targetRate);
        const merged = new Float32Array(pending.length + resampled.length);
        merged.set(pending, 0);
        merged.set(resampled, pending.length);
        let offset = 0;
        while (offset + frameSamples <= merged.length) {
            dotNet.invokeMethodAsync("OnPcm", floatToBytes(merged.subarray(offset, offset + frameSamples)));
            offset += frameSamples;
        }
        pending = merged.subarray(offset);
    };

    if (stream && stream.getAudioTracks().length > 0) {
        const source = audioContext.createMediaStreamSource(stream);
        source.connect(captureNode);
    } else {
        const oscillator = audioContext.createOscillator();
        oscillator.type = "sine";
        oscillator.frequency.value = 440;
        const gain = audioContext.createGain();
        gain.gain.value = 0.12;
        oscillator.connect(gain);
        gain.connect(captureNode);
        oscillator.start();
        capture = { oscillator };
    }

    if (stream && video) {
        video.srcObject = stream;
        await video.play().catch(() => undefined);
    }

    const intervalMs = Math.max(50, Math.round(1000 / fps));
    const timer = window.setInterval(() => {
        if (!context2d) {
            return;
        }

        if (stream && video && video.readyState >= 2) {
            context2d.drawImage(video, 0, 0, width, height);
        } else {
            const t = performance.now() / 400;
            context2d.fillStyle = `hsl(${(t * 40) % 360} 72% 42%)`;
            context2d.fillRect(0, 0, width, height);
            context2d.fillStyle = "#111";
            context2d.fillRect((t * 48) % width, 0, 24, height);
        }

        const rgba = context2d.getImageData(0, 0, width, height).data;
        dotNet.invokeMethodAsync("OnRgba", new Uint8Array(rgba.buffer.slice(0)));
        canvas.toBlob((blob) => {
            if (!blob) {
                return;
            }

            blob.arrayBuffer().then((buffer) => {
                dotNet.invokeMethodAsync("OnJpeg", new Uint8Array(buffer));
            });
        }, "image/jpeg", 0.6);
    }, intervalMs);

    capture = { ...(capture || {}), stream, audioContext, captureNode, timer, video };
    return sourceName;
}

export async function startPlayback() {
    await stop();
    const audioContext = new AudioContext({ sampleRate: targetRate });
    await audioContext.resume();
    const node = await createWorklet(audioContext, "moq-playback", `
        class MoqPlayback extends AudioWorkletProcessor {
            constructor() {
                super();
                this.queue = [];
                this.offset = 0;
                this.port.onmessage = (event) => {
                    this.queue.push(new Float32Array(event.data));
                    while (this.queue.length > ${maxPlaybackQueue}) {
                        this.queue.shift();
                        this.offset = 0;
                    }
                };
            }
            process(_inputs, outputs) {
                const out = outputs[0][0];
                if (!out) {
                    return true;
                }
                let i = 0;
                while (i < out.length) {
                    if (this.queue.length === 0) {
                        out.fill(0, i);
                        break;
                    }
                    const current = this.queue[0];
                    const take = Math.min(current.length - this.offset, out.length - i);
                    out.set(current.subarray(this.offset, this.offset + take), i);
                    this.offset += take;
                    i += take;
                    if (this.offset >= current.length) {
                        this.queue.shift();
                        this.offset = 0;
                    }
                }
                return true;
            }
        }
        registerProcessor("moq-playback", MoqPlayback);
    `);
    node.connect(audioContext.destination);
    playback = { audioContext, node, objectUrl: null };
}

export function playPcm(bytes) {
    if (!playback) {
        return;
    }

    const samples = new Float32Array(bytes.buffer, bytes.byteOffset, Math.floor(bytes.byteLength / 4));
    playback.node.port.postMessage(samples);
}

export function showJpeg(bytes) {
    const image = document.getElementById("remote-preview");
    if (!image) {
        return;
    }

    const blob = new Blob([bytes], { type: "image/jpeg" });
    const url = URL.createObjectURL(blob);
    if (image) {
        if (image.dataset.url) {
            URL.revokeObjectURL(image.dataset.url);
        }

        image.src = url;
        image.dataset.url = url;
    } else {
        URL.revokeObjectURL(url);
    }
}

export async function stop() {
    if (capture) {
        window.clearInterval(capture.timer);
        capture.stream?.getTracks().forEach((track) => track.stop());
        capture.oscillator?.stop();
        capture.captureNode?.disconnect();
        await capture.audioContext?.close().catch(() => undefined);
        if (capture.video) {
            capture.video.srcObject = null;
        }
        capture = null;
    }

    if (playback) {
        playback.node.disconnect();
        await playback.audioContext.close().catch(() => undefined);
        playback = null;
    }

    const image = document.getElementById("remote-preview");
    if (image?.dataset.url) {
        URL.revokeObjectURL(image.dataset.url);
        delete image.dataset.url;
        image.removeAttribute("src");
    }
}
