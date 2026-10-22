# SPDX-License-Identifier: GPL-3.0-only
"""Build optional language packs; never download anything during game startup."""
import concurrent.futures
import argparse
import time
import hashlib
import json
from pathlib import Path, PurePosixPath
import shutil
import urllib.request
import urllib.error
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
VERSION = ET.parse(ROOT / 'RuntimeTranslator.csproj').findtext('PropertyGroup/Version')
PACKS = ROOT / 'LanguagePacks'
CACHE = ROOT / 'LocalEngine' / 'build-cache' / 'language-packs'
LANGUAGES = {'ChineseSimplified': ('zh', '简体中文'), 'Japanese': ('ja', '日本語'),
             'Korean': ('ko', '한국어'), 'Russian': ('ru', 'Русский'), 'German': ('de', 'Deutsch'),
             'French': ('fr', 'Français'), 'Spanish': ('es', 'Español'), 'Portuguese': ('pt', 'Português')}
INSTRUCTIONS = {
'zh': '像普通模组一样安装：将本文件夹放入 RimWorld/Mods，或在创意工坊订阅后，在游戏模组列表同时启用 RimLingo 和本扩展包。基础包已含英文到中文；本扩展添加中文到英文。无需向基础模组复制模型文件。',
'ja': '通常のMODとして RimWorld/Mods に入れるか、Steamワークショップで購読してください。MOD一覧で RimLingo と本言語パックの両方を有効にしてください。英語と日本語の双方向モデルです。モデルファイルを手動で移動する必要はありません。',
'ko': '일반 모드처럼 RimWorld/Mods 에 넣거나 Steam 창작마당에서 구독하세요. 게임 모드 목록에서 RimLingo와 이 언어 팩을 함께 활성화하세요. 영어와 한국어 양방향 모델이며 모델 파일을 따로 옮길 필요가 없습니다.',
'ru': 'Установите как обычный мод в RimWorld/Mods или подпишитесь в Steam Workshop. В списке модов включите RimLingo и этот языковой пакет. Содержит английский ↔ русский. Перемещать файлы моделей вручную не нужно.',
'de': 'Wie einen normalen Mod unter RimWorld/Mods installieren oder im Steam Workshop abonnieren. RimLingo und dieses Sprachpaket in der Modliste aktivieren. Enthält Englisch ↔ Deutsch. Modelldateien müssen nicht manuell verschoben werden.',
'fr': 'Installez ce dossier comme un mod normal dans RimWorld/Mods, ou abonnez-vous via le Steam Workshop. Activez RimLingo et ce pack dans la liste des mods. Modèles anglais ↔ français. Aucun déplacement manuel des modèles n’est nécessaire.',
'es': 'Instala esta carpeta como un mod normal en RimWorld/Mods o suscríbete en Steam Workshop. Activa RimLingo y este paquete en la lista de mods. Incluye inglés ↔ español. No hace falta mover los modelos manualmente.',
'pt': 'Instale esta pasta como um mod normal em RimWorld/Mods ou subscreva na Steam Workshop. Ative RimLingo e este pacote na lista de mods. Inclui inglês ↔ português. Não é necessário mover os modelos manualmente.'}



def digest(path):
    with path.open('rb') as f:
        return hashlib.file_digest(f, 'sha256').hexdigest()


def build(language, code, title, index):
    folder = PACKS / language
    folder.mkdir(parents=True, exist_ok=True)
    records = []
    directions = [('zh', 'en')] if code == 'zh' else [('en', code), (code, 'en')]
    for source, target in directions:
        item = next(p for p in index if p['from_code'] == source and p['to_code'] == target)
        url = next(link for link in item['links'] if link.startswith('https://'))
        archive_path = CACHE / url.rsplit('/', 1)[-1]
        if not archive_path.exists():
            print('Downloading ' + archive_path.name, flush=True)
            temporary = archive_path.with_suffix('.tmp')
            download_url = 'https://data.argosopentech.com/argospm/v1/' + archive_path.name
            for attempt in range(5):
                try:
                    offset = temporary.stat().st_size if temporary.exists() else 0
                    headers = {'Range': 'bytes=' + str(offset) + '-'} if offset else {}
                    request = urllib.request.Request(download_url, headers=headers)
                    with urllib.request.urlopen(request, timeout=60) as response:
                        append = offset > 0 and response.status == 206
                        total = int(response.headers['Content-Range'].split('/')[-1]) if response.status == 206 else int(response.headers.get('Content-Length', '0'))
                        with temporary.open('ab' if append else 'wb') as output:
                            shutil.copyfileobj(response, output)
                    if total and temporary.stat().st_size != total:
                        raise IOError('Incomplete model download')
                    with zipfile.ZipFile(temporary) as verify:
                        if verify.testzip() is not None: raise ValueError('Model ZIP checksum failed')
                    temporary.replace(archive_path)
                    break
                except (TimeoutError, urllib.error.URLError, OSError) as error:
                    if attempt == 4: raise
                    print('Retrying ' + archive_path.name + ': ' + str(error), flush=True)
                    time.sleep(2)
        sha = digest(archive_path)
        with zipfile.ZipFile(archive_path) as archive:
            for member in archive.infolist():
                parts = PurePosixPath(member.filename)
                if parts.is_absolute() or '..' in parts.parts or '\\' in member.filename:
                    raise ValueError('Unsafe model archive path')
                if member.is_dir() or any(part in ('stanza', 'minisbd') for part in parts.parts):
                    continue
                destination = folder / 'models' / 'packages' / Path(*parts.parts)
                destination.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(member) as original, destination.open('wb') as output:
                    shutil.copyfileobj(original, output)
        model = next(p for p in (folder / 'models' / 'packages').glob('*/metadata.json')
                     if (data := json.loads(p.read_text(encoding='utf-8')))['from_code'] == source and data['to_code'] == target)
        readme = model.parent / 'README.md'
        if not readme.exists():
            raise ValueError('Missing model attribution: ' + str(model))
        records.append({'from_code': source, 'to_code': target, 'version': item['package_version'],
                        'url': url, 'sha256': sha, 'model': model.parent.name,
                        'upstream_readme': readme.relative_to(folder).as_posix()})
    (folder / 'manifest.json').write_text(json.dumps({'version': VERSION, 'language': language, 'models': records}, ensure_ascii=False, indent=2), encoding='utf-8')
    license_folder = folder / 'licenses'
    license_folder.mkdir(exist_ok=True)
    for name in ('CC-BY-4.0.txt', 'Argos-MIT.txt'):
        shutil.copy2(ROOT / 'LocalEngine' / 'licenses' / name, license_folder / name)
    package_id = 'gesvd.rimlingo.language.' + language.lower()
    about = ET.Element('ModMetaData')
    ET.SubElement(about, 'name').text = 'RimLingo Language Pack — ' + title
    ET.SubElement(about, 'author').text = 'Gesvd & Codex'
    ET.SubElement(about, 'packageId').text = package_id
    versions = ET.SubElement(about, 'supportedVersions')
    for version in ('1.5', '1.6'): ET.SubElement(versions, 'li').text = version
    dependency = ET.SubElement(ET.SubElement(about, 'modDependencies'), 'li')
    ET.SubElement(dependency, 'packageId').text = 'gesvd.rimlingo'
    ET.SubElement(dependency, 'displayName').text = 'Gesvd’s RimLingo'
    ET.SubElement(ET.SubElement(about, 'loadAfter'), 'li').text = 'gesvd.rimlingo'
    ET.SubElement(about, 'description').text = INSTRUCTIONS[code] + '\n\nRimLingo v' + VERSION + ' optional offline language mod. Enable after the base mod. Models are detected automatically; no manual path configuration or file copying is required. Other non-English language pairs may need both endpoint packs and use English as a pivot.'
    (folder / 'About').mkdir(exist_ok=True)
    ET.indent(about, space='  ')
    ET.ElementTree(about).write(folder / 'About' / 'About.xml', encoding='utf-8', xml_declaration=True)
    shutil.copy2(ROOT / 'Artwork' / 'LanguagePacks' / (language + '-Preview.png'), folder / 'About' / 'Preview.png')
    (folder / 'README.md').write_text('# RimLingo v' + VERSION + ' — ' + title + '\n\n' + INSTRUCTIONS[code] +
        '\n\nInstall like a normal RimWorld mod: extract the ZIP into RimWorld/Mods, or subscribe on Steam Workshop after publication. Enable the base RimLingo mod and this expansion in the game mod list, with the base first. The base detects enabled expansion paths automatically, including numeric Workshop directories. No model files need to be copied into the base mod. Restart after enabling/disabling packs.\n\nSelect LibreTranslate → Use bundled offline engine. Other non-English pairs need the corresponding endpoint packs and may pivot through English, with slower or less accurate results.\n\n' +
        'Package ID: `' + package_id + '`. Model downloads and SHA-256 are recorded in manifest.json. Original model README files retain author, license and training-data attribution. The bundle omits unused Stanza/MiniSBD files and uses RimLingo’s existing sentence splitter; model weights are unchanged. Retain About/, models/, licenses/, manifest.json and this README when redistributing.\n', encoding='utf-8')
    (folder / 'README.zh-CN.md').write_text('# RimLingo v' + VERSION + ' — ' + title + '扩展模组\n\n像普通模组一样安装：把 ZIP 解压到游戏的 `Mods` 目录，或待作者发布后在 Steam 创意工坊订阅。在模组列表同时启用 **Gesvd的边缘翻译 / RimLingo** 和 **本语言扩展**，让基础模组排在扩展之前，然后按游戏提示重启。无需打开基础模组目录，也无需搬动模型或填写路径。\n\n在翻译服务页选择 LibreTranslate 并启用内置离线引擎，从下拉菜单选择语言。其他非英语语言之间通常经英语中转，需要启用两端语言包；基础包已含英文→简体中文。禁用扩展后对应模型不再加载。\n\n模型来源、版本和 SHA-256 见 manifest.json；原作者与许可声明见各模型 README 和 licenses/，请保留全部声明。本扩展与基础模组共享 Python，不需额外安装程序。发布到创意工坊时上传本目录（直接包含 About 和 models），不要上传 ZIP 本身或带额外父文件夹的目录。\n', encoding='utf-8')
    output = PACKS / ('RimLingo-v' + VERSION + '-' + language + '.zip')
    with zipfile.ZipFile(output.with_suffix('.tmp'), 'w', zipfile.ZIP_DEFLATED, compresslevel=1) as archive:
        for path in sorted(folder.rglob('*')):
            if path.is_file():
                archive.write(path, 'RimLingo-Language-' + language + '/' + path.relative_to(folder).as_posix())
    output.with_suffix('.tmp').replace(output)
    output.with_suffix('.zip.sha256').write_text(digest(output) + '  ' + output.name + '\n', encoding='ascii')
    print('Built ' + output.name + ' (' + str(round(output.stat().st_size / 1048576, 1)) + ' MiB)', flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--languages', nargs='+', choices=LANGUAGES.keys())
    selected = parser.parse_args().languages or list(LANGUAGES)
    CACHE.mkdir(parents=True, exist_ok=True)
    PACKS.mkdir(exist_ok=True)
    index_file = CACHE / 'upstream-index.json'
    if index_file.exists():
        index = json.loads(index_file.read_text(encoding='utf-8'))
    else:
        with urllib.request.urlopen('https://raw.githubusercontent.com/argosopentech/argospm-index/main/index.json', timeout=60) as response:
            index = json.load(response)
        index_file.write_text(json.dumps(index, indent=2), encoding='utf-8')
    english = PACKS / 'English'
    english.mkdir(exist_ok=True)
    (english / 'README.md').write_text('# English — RimLingo v' + VERSION + '\n\nEnglish is the shared pivot language. The base mod includes English → Simplified Chinese. Other language packs include their English ↔ language models. No separate English model pack is needed. Install the desired language expansions as normal mods and enable them together with RimLingo. English needs no separate expansion mod.\n', encoding='utf-8')
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        futures = [pool.submit(build, language, code, title, index) for language, (code, title) in LANGUAGES.items() if language in selected]
        for future in concurrent.futures.as_completed(futures):
            future.result()


if __name__ == '__main__':
    main()
