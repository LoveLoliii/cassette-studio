"""Cassette Studio build engine. No third-party Python packages required."""
import argparse
import csv
import hashlib
import json
import math
from pathlib import Path
import re
import shutil
import struct
import subprocess
import sys
import time
import uuid
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
ID = re.compile(r'^[A-Za-z0-9_][A-Za-z0-9_.-]{0,63}$')
SPRITES = ['cassette1', 'cassette10', 'cassette3', 'cassette4', 'cassette5',
           'cassette6', 'cassette7', 'cassette8', 'cassette9']

def run(args):
    subprocess.run([str(a) for a in args], check=True)

def asset(base, name):
    if not name:
        raise ValueError('Missing asset path')
    path = (base / name).resolve()
    if not path.is_relative_to(base.resolve()) or not path.is_file():
        raise ValueError(f'Asset must exist inside the pack folder: {name}')
    return path

def validate(config, base):
    if config.get('schemaVersion') != 1:
        raise ValueError('Unsupported pack schema version')
    if not ID.fullmatch(config.get('id', '')):
        raise ValueError('Invalid Mod ID: use ASCII letters, digits, underscores, dots or hyphens')
    for field in ('name', 'author', 'version', 'gameVersion', 'receiptKey'):
        if not str(config.get(field, '')).strip():
            raise ValueError(f'Missing field: {field}')
    if not ID.fullmatch(config['receiptKey']):
        raise ValueError('Invalid receipt key')
    if not 1 <= config.get('deliveryDelayDays', 1) <= 365:
        raise ValueError('Delivery delay must be 1–365 days')
    tracks = config.get('tracks', [])
    if not 1 <= len(tracks) <= 100:
        raise ValueError('A pack must have 1–100 tracks')
    keys = set()
    for track in tracks:
        key = track.get('key', '')
        if not ID.fullmatch(key) or key in keys:
            raise ValueError(f'Duplicate or invalid stable track key: {key}')
        keys.add(key)
        if not track.get('title', '').strip():
            raise ValueError(f'Missing title: {key}')
        asset(base, track.get('audio'))
        if track.get('sprite', 'cassette1') not in SPRITES:
            raise ValueError(f'Unknown cassette sprite: {key}')
    art = asset(base, config.get('artwork'))
    head = art.read_bytes()[:24]
    if len(head) != 24 or head[:8] != b'\x89PNG\r\n\x1a\n':
        raise ValueError('Gift artwork must be PNG')
    w, h = struct.unpack('>II', head[16:24])
    if w % 16 or h % 16 or not 16 <= w <= 128 or not 16 <= h <= 128:
        raise ValueError('Artwork dimensions must be multiples of 16, between 16 and 128')
    target = float(config.get('targetLufs', -18))
    if not math.isfinite(target) or not -30 <= target <= -12:
        raise ValueError('Target loudness must be -30 to -12 LUFS')

def measure(path):
    result = subprocess.run(['ffmpeg', '-hide_banner', '-nostats', '-i', str(path),
        '-af', 'loudnorm=I=-18:TP=-2:LRA=50:print_format=json', '-f', 'null', '-'],
        capture_output=True, encoding='utf-8', errors='replace', check=True)
    matches = re.findall(r'\{\s*"input_i".*?\}', result.stderr, re.S)
    if not matches:
        raise ValueError(f'Cannot measure audio: {path}')
    data = json.loads(matches[-1])
    values = {k: float(data[k]) for k in ('input_i', 'input_tp', 'input_lra')}
    if not all(math.isfinite(v) for v in values.values()):
        raise ValueError(f'Audio is silent or has invalid measurements: {path}')
    return values

def prepare_audio(config, base, destination, cache):
    cache.mkdir(parents=True, exist_ok=True)
    rows = []
    for track in config['tracks']:
        source = asset(base, track['audio'])
        digest = hashlib.sha256(source.read_bytes()).hexdigest()
        stats_path = cache / (digest + '.json')
        if stats_path.exists():
            before = json.loads(stats_path.read_text())
        else:
            before = measure(source)
            stats_path.write_text(json.dumps(before))
        rows.append(dict(track, source=source, sha256=digest, before=before))
        print(f"Measured: {track['title']} ({before['input_i']:.2f} LUFS)", flush=True)
    target = round(min(float(config.get('targetLufs', -18)), min(
        r['before']['input_i'] - r['before']['input_tp'] - 2.5 for r in rows)), 2)
    normalized = config.get('normalize', True)
    report = []
    for row in rows:
        key = row['key']
        if normalized:
            gain = target - row['before']['input_i']
            token = hashlib.sha256(f"v1:{row['sha256']}:{gain:.4f}".encode()).hexdigest()
            prepared, info = cache / (token + '.ogg'), cache / (token + '.output.json')
            if not prepared.exists() or not info.exists():
                run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-nostdin', '-y',
                     '-i', row['source'], '-map', '0:a:0', '-map_metadata', '-1',
                     '-af', f'volume={gain:.4f}dB', '-c:a', 'libvorbis', '-q:a', '6', prepared])
                after = measure(prepared)
                info.write_text(json.dumps(after))
            after = json.loads(info.read_text())
            if after['input_tp'] > -2 or abs(after['input_i'] - target) > .25:
                raise ValueError(f'Output loudness check failed: {row["title"]}')
            shutil.copyfile(prepared, destination / f'track_{key}.ogg')
        else:
            gain, after = 0, row['before']
            # Convert uniformly to Vorbis so all supported import formats work.
            run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-nostdin', '-y',
                 '-i', row['source'], '-map', '0:a:0', '-c:a', 'libvorbis', '-q:a', '6',
                 destination / f'track_{key}.ogg'])
        report.append(dict(key=key, title=row['title'], source_sha256=row['sha256'],
                           gain_db=gain, before=row['before'], after=after))
        print(f"Prepared: {row['title']}", flush=True)
    return dict(normalized=normalized, target_lufs=target if normalized else None, tracks=report)

def generate_runtime(config):
    literal = lambda value: json.dumps(value, ensure_ascii=True)
    tapes = ',\n'.join(f'new TapeDefinition({literal(t["key"])}, {literal(t.get("sprite", "cassette1"))})'
                        for t in config['tracks'])
    # IDs are stable configuration keys, independent of list order.
    return '''namespace MondayCassettePack;
public sealed class TapeDefinition {
 public string Key; public string Sprite;
 public TapeDefinition(string key, string sprite) { Key=key; Sprite=sprite; }
}
public static class PackConfig {
 public const string ModId = %s;
 public const string ReceiptKey = %s;
 public const string GiftSprite = "gift_box";
 public const int DeliveryDelayDays = %d, TapeValue = 150, BoxWidth = 12, BoxHeight = %d;
 public static readonly TapeDefinition[] Tapes = { %s };
}
''' % (literal(config['id']), literal(config['receiptKey']), config.get('deliveryDelayDays', 1),
       max(9, math.ceil(len(config['tracks']) / 3) * 3), tapes)

def localization(config, destination):
    for language in ('zh-Hans', 'zh', 'en'):
        strings = {
            'gift_box_name': config.get('giftName') or config['name'],
            'gift_box_description': config.get('giftDescription') or f"内含 {len(config['tracks'])} 盘磁带的音乐礼盒。",
            'courier_name': '磁带配送员' if language != 'en' else 'Cassette Courier',
            'courier_intro': f"有你的音乐礼盒！里面装了 {len(config['tracks'])} 盘磁带，全都送你。",
            'courier_done': '礼盒送到了！打开盒子取出磁带，放进播放器就能听。',
            'no_space': '柜台和背包都放不下了。腾点位置吧，我明天再来。',
            'audio_missing': '录音暂时无法读取，请检查模组音频文件。我明天再来。'
        }
        if language == 'en':
            strings.update(courier_intro=f"A music gift box with {len(config['tracks'])} cassettes for you!",
                courier_done='Open the box and put a cassette in your player. Enjoy!',
                no_space='There is no space. I will try again tomorrow.',
                audio_missing='The recordings could not be loaded. Please check the mod files.')
        for track in config['tracks']:
            strings['tape_' + track['key'] + '_name'] = (
                track.get('titleEn') or track['title']) if language == 'en' else track['title']
            strings['tape_' + track['key'] + '_description'] = (
                track.get('descriptionEn') or track.get('description', '')) if language == 'en' else track.get('description', '')
        with (destination / (language + '.csv')).open('w', encoding='utf-8', newline='') as f:
            csv.writer(f).writerows(strings.items())

def install(package, game, mod_id):
    # Never touch files of a running game or a different mod.
    processes = subprocess.check_output(['tasklist', '/FO', 'CSV', '/NH'], text=True, errors='replace')
    if 'probably stolen.exe' in processes.lower():
        raise ValueError('请先保存并完全退出游戏，再点击安装。')
    mods = (game / 'Mods').resolve()
    mods.mkdir(exist_ok=True)
    target = (mods / mod_id).resolve()
    if target.parent != mods:
        raise ValueError('Unsafe install target')
    if target.exists():
        if ET.parse(target / 'manifest.xml').findtext('ID') != mod_id:
            raise ValueError('Existing folder belongs to another mod')
    stamp = time.strftime('%Y%m%d-%H%M%S') + '-' + uuid.uuid4().hex[:6]
    staged = game / ('.cassette-studio-stage-' + stamp)
    shutil.copytree(package, staged)
    backup = game / 'CassetteStudioBackups' / (mod_id + '-' + stamp)
    if target.exists():
        backup.parent.mkdir(exist_ok=True)
        target.rename(backup)
    try:
        staged.rename(target)
    except Exception:
        if backup.exists() and not target.exists():
            backup.rename(target)
        raise
    print(f'Installed: {target}', flush=True)
    return str(target)

def build(pack_path, game, do_install=False):
    pack_path, game = Path(pack_path).resolve(), Path(game).resolve()
    base = pack_path.parent
    config = json.loads(pack_path.read_text(encoding='utf-8-sig'))
    validate(config, base)
    managed = game / 'Probably Stolen_Data/Managed'
    if not (managed / 'Assembly-CSharp.dll').is_file():
        raise ValueError('游戏目录不正确，找不到 Assembly-CSharp.dll')
    for tool in ('ffmpeg', 'dotnet'):
        if not shutil.which(tool):
            raise ValueError(f'Missing required tool: {tool}')
    stamp = time.strftime('%Y%m%d-%H%M%S') + '-' + uuid.uuid4().hex[:6]
    work = ROOT / '.build' / config['id'] / stamp
    source_dir, package = work / 'src', work / 'package'
    source_dir.mkdir(parents=True)
    for folder in ('Sounds', 'Localization', 'Textures/Items'):
        (package / folder).mkdir(parents=True)
    assembly = 'CassettePack_' + hashlib.sha256(config['id'].encode()).hexdigest()[:12]
    for filename in ('ModEntry.cs', 'DeliverySchedule.cs', 'MondayCassettePack.csproj'):
        shutil.copyfile(ROOT / 'src' / filename, source_dir / filename)
    (source_dir / 'PackConfig.cs').write_text(generate_runtime(config), encoding='utf-8')
    run(['dotnet', 'build', source_dir / 'MondayCassettePack.csproj', '-c', 'Release',
         f'-p:GameDirectory={game}', f'-p:AssemblyName={assembly}'])
    shutil.copyfile(source_dir / 'bin/Release/netstandard2.1' / (assembly + '.dll'), package / (assembly + '.dll'))
    manifest = ET.Element('Manifest')
    for tag, value in [('ID', config['id']), ('Name', config['name']), ('Author', config['author']),
                       ('ModVersion', config['version']), ('Description', config.get('description', ''))]:
        ET.SubElement(manifest, tag).text = value
    ET.SubElement(ET.SubElement(manifest, 'GameVersions'), 'Version').text = config['gameVersion']
    ET.SubElement(manifest, 'Prerequisites')
    ET.SubElement(ET.SubElement(manifest, 'Assemblies'), 'Assembly').text = assembly + '.dll'
    ET.SubElement(manifest, 'Capabilities')
    ET.ElementTree(manifest).write(package / 'manifest.xml', encoding='utf-8', xml_declaration=True)
    shutil.copyfile(asset(base, config['artwork']), package / 'Textures/Items/gift_box.png')
    localization(config, package / 'Localization')
    report = prepare_audio(config, base, package / 'Sounds', ROOT / '.cache/audio')
    run(['dotnet', 'run', '--project', ROOT / 'tools/Scanner/Scanner.csproj', '-c', 'Release',
         f'-p:GameDirectory={game}', '--', package, managed])
    output = ROOT / 'dist' / config['id'] / stamp
    output.parent.mkdir(parents=True, exist_ok=True)
    shutil.copytree(package, output)
    report_path = output.parent / (stamp + '-audio-report.json')
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    archive = output.parent / (stamp + '.zip')
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as z:
        for path in output.rglob('*'):
            if path.is_file():
                z.write(path, Path(config['id']) / path.relative_to(output))
    result = dict(package=str(output), archive=str(archive), audioReport=str(report_path))
    if do_install:
        result['installed'] = install(output, game, config['id'])
    print('RESULT_JSON:' + json.dumps(result, ensure_ascii=False), flush=True)
    return result

if __name__ == '__main__':
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    parser = argparse.ArgumentParser()
    parser.add_argument('pack')
    parser.add_argument('--game', required=True)
    parser.add_argument('--install', action='store_true')
    args = parser.parse_args()
    try:
        build(args.pack, args.game, args.install)
    except Exception as error:
        print('ERROR: ' + str(error), file=sys.stderr)
        sys.exit(1)
