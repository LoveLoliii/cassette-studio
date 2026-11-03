"""Generate short original WAV tones for the public demo; never replace user files."""
from pathlib import Path
import math
import shutil
import struct
import wave

root = Path(__file__).resolve().parents[1]
folder = root / 'packs/demo'
(folder / 'audio').mkdir(parents=True, exist_ok=True)
(folder / 'artwork').mkdir(exist_ok=True)
if not (folder / 'artwork/gift_box.png').exists():
    shutil.copyfile(root / 'assets/default-gift-box.png', folder / 'artwork/gift_box.png')
for number in (1, 2):
    path = folder / 'audio' / f'demo_{number:02}.wav'
    if path.exists():
        continue
    rate = 22050
    data = bytearray()
    for frequency in (261.63, 329.63, 392.0, 523.25, 440.0, 392.0, 329.63, 261.63):
        frequency *= 2 ** ((number - 1) / 12)
        for index in range(rate // 2):
            seconds = index / rate
            envelope = min(seconds / .02, 1) * max(0, min((.48 - seconds) / .12, 1))
            data.extend(struct.pack('<h', int(.12 * envelope * math.sin(2 * math.pi * frequency * seconds) * 32767)))
    with wave.open(str(path), 'wb') as audio:
        audio.setnchannels(1)
        audio.setsampwidth(2)
        audio.setframerate(rate)
        audio.writeframes(data)
    print(f'Generated original demo audio: {path.name}')
