"""Original Hiking GPS sound design; Python standard library only."""
from pathlib import Path
import math
import random
import struct
import wave

ROOT = Path(__file__).resolve().parent
SR = 44100
PEAK = round(32768 * 10 ** (-6 / 20))
TAU = 2 * math.pi


def empty(seconds):
    return [0.0] * round(seconds * SR)


def mix(dst, src, seconds=0.0, gain=1.0):
    start = round(seconds * SR)
    for i, value in enumerate(src):
        if start + i < len(dst):
            dst[start + i] += gain * value


def lowpass(values, cutoff):
    a = 1 - math.exp(-TAU * cutoff / SR)
    state = 0.0
    out = []
    for value in values:
        state += a * (value - state)
        out.append(state)
    return out


def button(duration, seed, pitch=1.0, decay=1.0):
    rng = random.Random(seed)
    noise = lowpass([rng.uniform(-1, 1) for _ in empty(duration)], 1900)
    out = []
    for i, grit in enumerate(noise):
        t = i / SR
        attack = 1 - math.exp(-t / 0.00020)
        # Rubber dome: heavily damped body, soft plastic contact, no beep.
        body = math.sin(TAU * 310 * pitch * t) * math.exp(-t / (0.0048 * decay))
        shell = 0.23 * math.sin(TAU * 735 * pitch * t) * math.exp(-t / 0.0022)
        friction = 0.48 * grit * math.exp(-t / (0.0038 * decay))
        out.append(attack * (body + shell + friction))
    return out


def tone(duration, frequency):
    out = []
    for i in range(round(duration * SR)):
        t = i / SR
        attack = min(1.0, t / 0.0035)
        release = min(1.0, max(0.0, (duration - t) / 0.013))
        env = math.sin(attack * math.pi / 2) ** 2 * math.sin(release * math.pi / 2) ** 2
        # Fixed pitch, restrained harmonics: a small cheap mono speaker.
        phase = TAU * frequency * t
        signal = math.sin(phase) + 0.13 * math.sin(2 * phase) + 0.045 * math.sin(3 * phase)
        out.append(env * signal * (0.94 + 0.06 * math.exp(-t / 0.025)))
    return lowpass(out, 3000)


def finish(values):
    # Smooth edge taper and weighted DC removal preserve silent endpoints.
    n = len(values)
    weights = []
    for i in range(n):
        edge_in = min(1.0, i / (0.00015 * SR))
        edge_out = min(1.0, (n - 1 - i) / (0.008 * SR))
        weights.append(edge_in * (0.5 - 0.5 * math.cos(math.pi * edge_out)))
    out = [v * w for v, w in zip(values, weights)]
    correction = sum(out) / sum(weights)
    out = [v - correction * w for v, w in zip(out, weights)]
    gain = PEAK / max(abs(v) for v in out)
    pcm = [round(v * gain) for v in out]
    # Exact zero-sum PCM, spreading sub-LSB correction through active samples.
    residual = sum(pcm)
    candidates = sorted(range(1, n - round(0.010 * SR)), key=lambda i: abs(pcm[i]), reverse=True)
    for i in candidates:
        if residual == 0:
            break
        if abs(pcm[i]) == PEAK:
            continue
        step = 1 if residual > 0 else -1
        pcm[i] -= step
        residual -= step
    assert sum(pcm) == 0
    assert pcm[0] == pcm[-1] == 0
    assert max(abs(v) for v in pcm) == PEAK
    return pcm


def write(name, pcm):
    with wave.open(str(ROOT / name), 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(struct.pack(f'<{len(pcm)}h', *pcm))


def build():
    sounds = {}
    on = empty(0.350)
    mix(on, button(0.030, 71, 0.9), gain=0.48)
    mix(on, tone(0.115, 660), 0.022, 0.70)
    mix(on, tone(0.175, 880), 0.147, 0.70)
    sounds['power_on.wav'] = finish(on)

    off = empty(0.260)
    mix(off, tone(0.091, 880), gain=0.70)
    mix(off, tone(0.102, 660), 0.101, 0.70)
    mix(off, button(0.040, 71, 0.9), 0.214, 0.48)
    sounds['power_off.wav'] = finish(off)

    for name, duration, seed, pitch, decay in [
        ('click_a.wav', 0.074, 101, 1.000, 1.00),
        ('click_b.wav', 0.078, 109, 0.965, 1.06),
        ('click_c.wav', 0.071, 127, 1.035, 0.95),
    ]:
        sounds[name] = finish(button(duration, seed, pitch, decay))

    limit = empty(0.120)
    for i in range(len(limit)):
        t = i / SR
        # Brief muted end-stop, with a much lower sustained energy than buttons.
        contact = math.sin(TAU * 360 * t) * math.exp(-t / 0.0012)
        body = 0.055 * math.sin(TAU * 145 * t) * math.exp(-t / 0.007)
        limit[i] = (1 - math.exp(-t / 0.00016)) * (contact + body)
    sounds['zoom_limit.wav'] = finish(limit)

    for name, pcm in sounds.items():
        write(name, pcm)
    gap = [0] * round(0.300 * SR)
    preview = []
    for pcm in sounds.values():
        if preview:
            preview.extend(gap)
        preview.extend(pcm)
    write('preview.wav', preview)
    # Ready-made listening sequence: two previews, then ten ABC click cycles.
    audition = preview + gap + preview + [0] * SR
    for _ in range(10):
        for name in ('click_a.wav', 'click_b.wav', 'click_c.wav'):
            audition.extend(sounds[name])
            audition.extend([0] * round(0.130 * SR))
    write('audition_repeat.wav', audition)

    def db(value):
        return 20 * math.log10(value) if value else -math.inf

    rows = []
    for name, pcm in sounds.items():
        rms = math.sqrt(sum(v * v for v in pcm) / len(pcm)) / 32768
        rows.append(f'| `{name}` | {len(pcm) / SR * 1000:.0f} | {db(max(abs(v) for v in pcm) / 32768):.2f} | {db(rms):.2f} |')
    readme = '''# Hiking GPS — звуки прибора

Шесть оригинальных синтезированных звуков. Формат всех WAV: моно, PCM 16 бит, 44 100 Гц.

| Файл | Длительность, мс | Пик, dBFS | RMS, dBFS |
|---|---:|---:|---:|
''' + '\n'.join(rows) + '''

RMS измерен по всей длине файла, без весовых фильтров; это измерение энергии, а не оценка воспринимаемой громкости.

## Характер и обработка

Включение: мягкий контакт и два последовательных тона 660 → 880 Гц.
Выключение: та же пара 880 → 660 Гц и контакт в конце.
Небольшая примесь гармоник и фильтр низких частот придают сигналам характер маленького динамика.
Кнопки: короткие затухающие колебания резинового купола с фильтрованным шумом контакта.
У трёх вариантов немного отличаются атака, частота корпуса и затухание.
Упор зума: приглушённый короткий контакт с низким телом 145 Гц, без сигнала ошибки.

Обработка: Python, только стандартная библиотека (math, random, wave, struct, pathlib).
Без внешних записей, музыкальных фраз, реверберации и компрессии.
Детерминированный синтез, фильтрация, огибающие, сглаживание последних 8 мс,
удаление DC, нормализация каждого эффекта в −6 dBFS и квантование в PCM16.
Сумма PCM-выборок каждого эффекта точно равна нулю; последняя выборка — ноль.

Требование одинакового пика сочетается с меньшей энергией коротких щелчков;
упор зума имеет ещё меньшую энергию. Тональная пара и три кнопки согласованы внутри своих групп.
Субъективное соотношение громкости и утомляемость требуют проверки человеком в игре.

## Прослушивание и проверка

- `preview.wav`: шесть исходных файлов в порядке задания, между ними ровно 300 мс тишины, без дополнительной нормализации.
- `audition_repeat.wav`: preview дважды, затем десять циклов A/B/C (каждый щелчок десять раз), паузы между кнопками 130 мс.
- `verification.txt`: вывод проверочного кода из задания без изменений.
- `verify.py`: сам проверочный код; запускать из этой папки: `python verify.py`.
- `generate_sounds.py`: воспроизводимый исходник; запуск: `python generate_sounds.py`.

Автоматические проверки выполнены. Субъективное прослушивание ассистентом не выполнено: в этой сессии нет инструмента для прослушивания и оценки звука. Повторная последовательность подготовлена для проверки пользователем; оценка приятности и утомляемости не заявляется как пройденная.
'''
    (ROOT / 'README.md').write_text(readme, encoding='utf-8')


if __name__ == '__main__':
    build()
