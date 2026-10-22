# SPDX-License-Identifier: GPL-3.0-only
"""Verify optional release archives, model manifests and original download checksums."""
import hashlib
import json
import struct
from pathlib import Path
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
PACKS = ROOT / 'LanguagePacks'
CACHE = ROOT / 'LocalEngine' / 'build-cache' / 'language-packs'


def sha256(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def main():
    folders = sorted(folder for folder in PACKS.iterdir() if folder.is_dir() and (folder / 'manifest.json').exists())
    assert len(folders) == 8, len(folders)
    total = 0
    preview_hashes = set()
    for folder in folders:
        manifest = json.loads((folder / 'manifest.json').read_text(encoding='utf-8'))
        assert manifest['language'] == folder.name
        about = ET.parse(folder / 'About' / 'About.xml').getroot()
        assert about.findtext('packageId') == 'gesvd.rimlingo.language.' + folder.name.lower()
        assert about.findtext('modDependencies/li/packageId') == 'gesvd.rimlingo'
        assert about.findtext('loadAfter/li') == 'gesvd.rimlingo'
        assert [node.text for node in about.findall('supportedVersions/li')] == ['1.5', '1.6']
        preview = (folder / 'About' / 'Preview.png').read_bytes()
        assert preview == (ROOT / 'Artwork' / 'LanguagePacks' / (folder.name + '-Preview.png')).read_bytes()
        assert preview[:8] == b'\x89PNG\r\n\x1a\n' and struct.unpack('>II', preview[16:24]) == (800, 450)
        assert len(preview) < 1048576
        assert preview != (ROOT / 'About' / 'Preview.png').read_bytes()
        preview_hashes.add(hashlib.sha256(preview).hexdigest())
        for model in manifest['models']:
            metadata = json.loads((folder / 'models' / 'packages' / model['model'] / 'metadata.json').read_text(encoding='utf-8'))
            assert (metadata['from_code'], metadata['to_code']) == (model['from_code'], model['to_code'])
            assert sha256(CACHE / model['url'].rsplit('/', 1)[-1]) == model['sha256']
            assert (folder / model['upstream_readme']).is_file()
        output = PACKS / ('RimLingo-v' + manifest['version'] + '-' + folder.name + '.zip')
        assert output.with_suffix('.zip.sha256').read_text(encoding='ascii').split()[0] == sha256(output)
        with zipfile.ZipFile(output) as archive:
            assert archive.testzip() is None
            names = set(archive.namelist())
            for path in folder.rglob('*'):
                if path.is_file():
                    name = 'RimLingo-Language-' + folder.name + '/' + path.relative_to(folder).as_posix()
                    assert name in names, name
                    if path.name in ('README.md', 'README.zh-CN.md', 'metadata.json', 'manifest.json', 'About.xml', 'Preview.png'):
                        assert archive.read(name) == path.read_bytes(), name
            assert not any('/stanza/' in name or '/minisbd/' in name for name in names)
        total += output.stat().st_size
        print('PASS: ' + output.name + ' — ' + str(round(output.stat().st_size / 1048576, 1)) + ' MiB', flush=True)
    assert len(preview_hashes) == 8, 'Each expansion must keep its own cover'
    assert (PACKS / 'English' / 'README.md').is_file()
    print('Verified 8 model packs plus English instructions; combined ZIP size ' + str(round(total / 1048576, 1)) + ' MiB', flush=True)


if __name__ == '__main__':
    main()
