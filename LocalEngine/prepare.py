# SPDX-License-Identifier: MIT
"""Release preparation, never run from the game. Requires installed local dependencies."""
import concurrent.futures
import hashlib
import importlib.metadata as metadata
import json
from pathlib import Path
import shutil
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parent
SOURCES = ROOT / 'sources'
LICENSES = ROOT / 'licenses'


def download(url, path):
    if not path.exists() or path.stat().st_size == 0:
        print('Downloading ' + url, flush=True)
        temporary = path.with_name(path.name + '.tmp')
        with urllib.request.urlopen(url, timeout=180) as response, open(temporary, 'wb') as output:
            shutil.copyfileobj(response, output)
        temporary.replace(path)
    return hashlib.sha256(path.read_bytes()).hexdigest()


def collect_distribution(dist):
    name, version = dist.metadata['Name'], dist.version
    with urllib.request.urlopen(f'https://pypi.org/pypi/{name}/{version}/json', timeout=60) as response:
        info = json.load(response)
    source = next((f for f in info['urls'] if f['packagetype'] == 'sdist'), None)
    record = {'name': name, 'version': version,
              'license': dist.metadata.get('License-Expression') or dist.metadata.get('License', ''),
              'upstream': dist.metadata.get_all('Project-URL') or [],
              'source': source['url'] if source else None,
              'source_sha256': source['digests']['sha256'] if source else None}
    if name.lower() in ('ctranslate2', 'onnxruntime'):
        repo = 'OpenNMT/CTranslate2' if name.lower() == 'ctranslate2' else 'microsoft/onnxruntime'
        record['native_source'] = f'https://github.com/{repo}/archive/refs/tags/v{version}.tar.gz'
    notices = LICENSES / f'{name}-{version}'
    notices.mkdir(exist_ok=True)
    for item in dist.files or []:
        if any(term in str(item).lower() for term in ('license', 'copying', 'notice')):
            origin = dist.locate_file(item)
            if origin.is_file():
                destination = notices / str(item).replace('..', '_').replace('/', '_').replace('\\', '_')
                shutil.copy2(origin, destination)
    # Ship exact source distributions for copyleft Python dependencies.
    copyleft = any(term in (record['license'] + ' '.join(dist.metadata.get_all('Classifier') or [])).lower()
                   for term in ('agpl', 'affero', 'gpl', 'lgpl', 'lesser', 'mpl-2.0'))
    if copyleft:
        if not source:
            raise RuntimeError('No corresponding source archive for ' + name)
        digest = download(source['url'], SOURCES / source['filename'])
        if digest != record['source_sha256']:
            raise RuntimeError('Source checksum mismatch: ' + name)
        record['bundled_source'] = 'sources/' + source['filename']
    return record


def main():
    SOURCES.mkdir(exist_ok=True)
    LICENSES.mkdir(exist_ok=True)
    (ROOT / 'models').mkdir(exist_ok=True)
    (ROOT / 'build-cache').mkdir(exist_ok=True)
    model = ROOT / 'build-cache' / 'translate-en_zh-1_9.argosmodel'
    digest = download('https://argos-net.com/v1/translate-en_zh-1_9.argosmodel', model)
    assert digest == '433e7c4f034d87fbe2353161e05f18646d7999452f801a4e1f0378522b9850ab', 'Model checksum mismatch'
    with zipfile.ZipFile(model) as archive:
        archive.extractall(ROOT / 'models' / 'packages')
        (LICENSES / 'MODEL-README.md').write_bytes(archive.read('translate-en_zh-1_9/README.md'))
    # No third-party tokenizer weights are needed by the standard-library splitter.
    stanza = ROOT / 'models' / 'packages' / 'translate-en_zh-1_9' / 'stanza'
    if stanza.exists():
        shutil.rmtree(stanza)
    cuda = ROOT / 'runtime' / 'Lib' / 'site-packages' / 'ctranslate2' / 'cudnn64_9.dll'
    if cuda.exists():
        cuda.unlink()  # CPU-only bundle does not need NVIDIA's separately licensed runtime.
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
        distributions = sorted(pool.map(collect_distribution, metadata.distributions()), key=lambda d: d['name'].lower())
    (ROOT / 'dependencies.json').write_text(json.dumps(distributions, ensure_ascii=False, indent=2), encoding='utf-8')
    (ROOT / 'requirements-lock.txt').write_text('\n'.join(f"{d['name']}=={d['version']}" for d in distributions if d['name'] != 'pip') + '\n', encoding='utf-8')
    for name, url in {
        'AGPL-3.0.txt': 'https://raw.githubusercontent.com/LibreTranslate/LibreTranslate/main/LICENSE',
        'Argos-MIT.txt': 'https://raw.githubusercontent.com/argosopentech/argos-translate/master/LICENSE',
        'CC-BY-4.0.txt': 'https://raw.githubusercontent.com/creativecommons/legalcode/main/by_4.0.txt',
        'CTranslate2-MIT.txt': 'https://raw.githubusercontent.com/OpenNMT/CTranslate2/v4.8.3/LICENSE',
        'SentencePiece-Apache-2.0.txt': 'https://raw.githubusercontent.com/google/sentencepiece/v0.2.2/LICENSE',
        'FlatBuffers-Apache-2.0.txt': 'https://raw.githubusercontent.com/google/flatbuffers/v25.12.19/LICENSE',
        'oneDNN-Apache-2.0.txt': 'https://raw.githubusercontent.com/oneapi-src/oneDNN/v3.1.1/LICENSE',
        'Intel-Simplified-License.pdf': 'https://cdrdv2.intel.com/v1/dl/getContent/749362',
        'CUDA-EULA.html': 'https://docs.nvidia.com/cuda/archive/12.8.1/eula/index.html',
        'OpenMP-LICENSE.txt': 'https://raw.githubusercontent.com/llvm/llvm-project/main/openmp/LICENSE.TXT',
        'GPL-3.0.txt': 'https://raw.githubusercontent.com/gcc-mirror/gcc/master/COPYING3',
        'Stanza-Apache-2.0.txt': 'https://raw.githubusercontent.com/stanfordnlp/stanza/main/LICENSE',
        'MiniSBD-AGPL.txt': 'https://raw.githubusercontent.com/LibreTranslate/MiniSBD/main/LICENSE',
    }.items():
        download(url, LICENSES / name)
    shutil.copy2(ROOT / 'runtime' / 'LICENSE.txt', LICENSES / 'Python-LICENSE.txt')
    download('https://mupdf.com/downloads/archive/mupdf-1.24.10-source.tar.gz', SOURCES / 'mupdf-1.24.10-source.tar.gz')
    with zipfile.ZipFile(ROOT / 'sources.zip', 'w', compression=zipfile.ZIP_STORED) as archive:
        for path in (ROOT / 'runtime' / 'Lib' / 'site-packages').rglob('*.py'):
            archive.write(path, 'python-source/' + path.relative_to(ROOT / 'runtime' / 'Lib' / 'site-packages').as_posix())
        for path in SOURCES.iterdir():
            archive.write(path, 'sources/' + path.name)
        for path in LICENSES.rglob('*'):
            if path.is_file():
                archive.write(path, 'licenses/' + path.relative_to(LICENSES).as_posix())
        for name in ('serve.py', 'prepare.py', 'Build.ps1', 'requirements-lock.txt', 'dependencies.json', 'README.md', 'THIRD_PARTY_NOTICES.md', 'test_engine.py', 'package.py'):
            archive.write(ROOT / name, name)
    (ROOT / 'READY.json').write_text(json.dumps({'libretranslate': '1.9.6', 'model': 'en_zh-1.9',
                                             'model_sha256': digest, 'sentence_splitter': 'stdlib-regex-v1'}, indent=2), encoding='utf-8')
    print('Prepared local engine and corresponding source archives.', flush=True)


if __name__ == '__main__':
    main()
