import json
import os
import struct
import sys
from collections import OrderedDict
from pathlib import Path
from socketserver import StreamRequestHandler, TCPServer

import numpy as np
import onnxruntime
from piper import PiperVoice, SynthesisConfig
from piper.config import PiperConfig
from piper.voice import ESPEAK_DATA_DIR


SAMPLE_RATE = 48000
MAX_SECONDS = 28
MAX_CACHE_BYTES = 16 * 1024 * 1024
MODEL = "ru_RU-irina-medium"
speech_style = SynthesisConfig(length_scale=1.0, noise_scale=0.55, noise_w_scale=0.65)
cache = OrderedDict()
cache_bytes = 0


def load_voice(directory, name):
    model = directory / (name + ".onnx")
    config = PiperConfig.from_dict(json.loads(model.with_suffix(".onnx.json").read_text()))
    options = onnxruntime.SessionOptions()
    options.intra_op_num_threads = 1
    options.inter_op_num_threads = 1
    session = onnxruntime.InferenceSession(str(model), sess_options=options, providers=["CPUExecutionProvider"])
    return PiperVoice(session=session, config=config, espeak_data_dir=ESPEAK_DATA_DIR)


def synthesize(voice, text):
    global cache_bytes
    key = text
    if key in cache:
        cache.move_to_end(key)
        return cache[key]

    parts = []
    length = 0
    for chunk in voice.synthesize(text, syn_config=speech_style):
        native = chunk.audio_float_array
        if not len(native):
            continue
        rate_ratio = chunk.sample_rate * 0.98 / SAMPLE_RATE
        output = np.interp(np.arange(0, len(native), rate_ratio), np.arange(len(native)), native)
        if length + len(output) > MAX_SECONDS * SAMPLE_RATE:
            break
        parts.append(output)
        length += len(output)
    if not parts:
        return b""

    signal = np.concatenate(parts)
    echo = 1488
    if len(signal) > echo:
        signal[echo:] += 0.10 * signal[:-echo]
    fade = min(240, len(signal) // 2)
    if fade:
        envelope = np.linspace(0, 1, fade)
        signal[:fade] *= envelope
        signal[-fade:] *= envelope[::-1]

    result = (np.clip(signal * 0.88, -1, 1) * 32767).astype("<i2").tobytes()
    while cache and cache_bytes + len(result) > MAX_CACHE_BYTES:
        _, dropped = cache.popitem(last=False)
        cache_bytes -= len(dropped)
    cache[key] = result
    cache_bytes += len(result)
    return result


class VoiceRequest(StreamRequestHandler):
    timeout = 2

    def handle(self):
        try:
            header = self.rfile.read(4)
            if len(header) != 4:
                return
            size = struct.unpack("<i", header)[0]
            audio = b""
            if 0 < size <= 2048:
                language, text = self.rfile.read(size).decode("utf-8").split("\n", 1)
                if language == "ru" and 0 < len(text) <= 480:
                    audio = synthesize(self.server.voice, text)
            self.wfile.write(struct.pack("<i", len(audio)))
            self.wfile.write(audio)
        except (OSError, ValueError):
            pass


class VoiceServer(TCPServer):
    allow_reuse_address = True

    def __init__(self, port, voice):
        self.voice = voice
        super().__init__(("127.0.0.1", port), VoiceRequest)


def main():
    parent = os.getppid()
    os.nice(10)
    directory = Path(sys.argv[1])
    with VoiceServer(int(sys.argv[2]), load_voice(directory, MODEL)) as server:
        server.timeout = 1
        while parent != 1 and os.getppid() == parent:
            server.handle_request()


if __name__ == "__main__":
    main()
