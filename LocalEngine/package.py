# SPDX-License-Identifier: MIT
"""Package the playable mod without caches, game DLLs or unused model assets."""
import hashlib
from pathlib import Path
import zipfile

ENGINE = Path(__file__).resolve().parent
ROOT = ENGINE.parent
OUTPUT = ROOT / 'Releases' / 'Gesvds-RimLingo-LibreTranslate-win64.zip'


def main():
    for required in ('READY.json', 'sources.zip', 'runtime/python.exe', 'models/packages/translate-en_zh-1_9/model/model.bin'):
        if not (ENGINE / required).is_file():
            raise RuntimeError('Missing release input: ' + required)
    if not (ROOT / 'Assemblies' / 'RuntimeTranslator.dll').is_file():
        raise RuntimeError('Build the C# mod first')
    OUTPUT.parent.mkdir(exist_ok=True)
    files = [ROOT / name for name in ('LICENSE', 'README.md', 'RuntimeTranslator.csproj')]
    for folder in ('About', 'Languages', 'Assemblies', 'Source'):
        files.extend(path for path in (ROOT / folder).rglob('*') if path.is_file())
    for folder in ('runtime', 'models/packages', 'licenses'):
        files.extend(path for path in (ENGINE / folder).rglob('*') if path.is_file() and '__pycache__' not in path.parts)
    files.extend(path for path in ENGINE.iterdir() if path.is_file() and path.suffix in ('.py', '.ps1', '.md', '.json', '.txt'))
    files.append(ENGINE / 'sources.zip')
    temporary = OUTPUT.with_suffix('.tmp')
    with zipfile.ZipFile(temporary, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=1) as archive:
        for path in sorted(set(files)):
            archive.write(path, 'RimLingo/' + path.relative_to(ROOT).as_posix())
    with zipfile.ZipFile(temporary) as archive:
        names = archive.namelist()
        assert 'RimLingo/LocalEngine/serve.py' in names
        assert 'RimLingo/LocalEngine/sources.zip' in names
        assert not any('build-cache/' in name or '/stanza/' in name or '/minisbd/en.onnx' in name or name.endswith('Assembly-CSharp.dll') for name in names)
        assert not any(name.endswith('cudnn64_9.dll') for name in names)
        assert archive.testzip() is None
    temporary.replace(OUTPUT)
    digest = hashlib.sha256(OUTPUT.read_bytes()).hexdigest()
    OUTPUT.with_suffix('.zip.sha256').write_text(digest + '  ' + OUTPUT.name + '\n', encoding='ascii')
    print(f'Packaged {OUTPUT} ({OUTPUT.stat().st_size / 1048576:.1f} MiB), SHA256 {digest}', flush=True)


if __name__ == '__main__':
    main()
