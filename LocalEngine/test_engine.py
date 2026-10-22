# SPDX-License-Identifier: GPL-3.0-only
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parent


def main():
    with socket.socket() as listener:
        listener.bind(('127.0.0.1', 0))
        port = listener.getsockname()[1]
    identity = 'offline-integration-test'
    endpoint = f'http://127.0.0.1:{port}'
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    # A disposable parent verifies cleanup after an unexpected game exit.
    parent = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(180)'], creationflags=subprocess.CREATE_NO_WINDOW)
    with tempfile.TemporaryDirectory(prefix='rim-local-engine-') as data:
        pack_args = []
        if os.environ.get('RT_TEST_LANGUAGE_PACKS') == '1':
            for folder in sorted((ROOT.parent / 'LanguagePacks').iterdir()):
                if (folder / 'About' / 'About.xml').is_file():
                    pack_args.extend(['--pack', str(folder)])
        process = subprocess.Popen([sys.executable, '-X', 'utf8', str(ROOT / 'serve.py'), '--port', str(port),
                                    '--parent-pid', str(parent.pid), '--identity', identity, '--data', data] + pack_args,
                                   creationflags=subprocess.CREATE_NO_WINDOW)
        try:
            deadline = time.monotonic() + 120
            while time.monotonic() < deadline:
                if process.poll() is not None:
                    raise AssertionError((Path(data) / 'engine.log').read_text(encoding='utf-8'))
                try:
                    with opener.open(endpoint + '/runtime-translator/health', timeout=2) as response:
                        assert response.read().decode() == identity
                    break
                except (urllib.error.URLError, TimeoutError):
                    time.sleep(0.5)
            else:
                raise AssertionError('Engine did not become ready')
            print('PASS: portable runtime starts with outbound networking disabled', flush=True)
            def translate(text, source='en', target='zh'):
                request = urllib.request.Request(endpoint + '/runtime-translator/translate',
                    data=json.dumps({'q': text, 'source': source, 'target': target}).encode(),
                    headers={'Content-Type': 'application/json'})
                with opener.open(request, timeout=60) as response:
                    return json.load(response)['translatedText']
            with opener.open(endpoint + '/runtime-translator/languages', timeout=10) as response:
                inventory = json.load(response)
            assert 'en' in inventory['languages'] and 'zh' in inventory['languages']
            if not pack_args:
                assert inventory['languages'] == ['en', 'zh'], 'Disabled expansion mods must not be loaded'
            print('PASS: installed offline languages: ' + ', '.join(inventory['languages']), flush=True)
            output = translate('Hello world. This is a colony on a distant planet.')
            assert any('\u4e00' <= c <= '\u9fff' for c in output), output
            print('PASS: real English -> Chinese translation: ' + output, flush=True)
            output = translate('__RT_TOKEN_0__ The __RT_TOKEN_1__ needs food. __RT_TOKEN_2__\n')
            for token in ('__RT_TOKEN_0__', '__RT_TOKEN_1__', '__RT_TOKEN_2__'):
                assert output.count(token) == 1, output
            assert output.endswith('\n'), repr(output)
            print('PASS: glossary/format placeholders and paragraph whitespace preserved', flush=True)
            batch_request = urllib.request.Request(endpoint + '/runtime-translator/translate',
                data=json.dumps({'q': ['Hello colony.', '__RT_TOKEN_0__ needs food.\n\nSecond paragraph.'], 'source': 'en', 'target': 'zh'}).encode(),
                headers={'Content-Type': 'application/json'})
            with opener.open(batch_request, timeout=60) as response:
                bulk = json.load(response)
            assert len(bulk['translatedTexts']) == 2 and bulk['errors'] == [None, None], bulk
            assert bulk['translatedTexts'][1].count('__RT_TOKEN_0__') == 1 and '\n\n' in bulk['translatedTexts'][1], bulk
            print('PASS: batch endpoint preserves input order, placeholders and paragraph breaks', flush=True)
            for invalid_query in ([], ['valid', 42], None):
                invalid = urllib.request.Request(endpoint + '/runtime-translator/translate',
                    data=json.dumps({'q': invalid_query, 'source': 'en', 'target': 'zh'}).encode(),
                    headers={'Content-Type': 'application/json'})
                try:
                    opener.open(invalid, timeout=5)
                    raise AssertionError('Invalid batch accepted')
                except urllib.error.HTTPError as error:
                    assert error.code == 400
            assert translate('__RT_TOKEN_0__') == '__RT_TOKEN_0__'
            assert translate('') == ''
            try:
                translate('Hello', target='xx')
                raise AssertionError('Unsupported target accepted')
            except urllib.error.HTTPError as error:
                assert error.code == 400
            request = urllib.request.Request(endpoint + '/runtime-translator/translate', data=b'[]',
                                             headers={'Content-Type': 'application/json'})
            try:
                opener.open(request, timeout=5)
                raise AssertionError('Malformed request accepted')
            except urllib.error.HTTPError as error:
                assert error.code == 400
            with opener.open(endpoint + '/runtime-translator/source', timeout=10) as response:
                assert response.read(2) == b'PK'
            print('PASS: invalid input/language rejected; corresponding source available', flush=True)
            if os.environ.get('RT_TEST_LANGUAGE_PACKS') == '1':
                samples = {'zh': '这是一个遥远星球上的殖民地。', 'ja': 'これは遠い惑星の植民地です。',
                           'ko': '이곳은 먼 행성의 식민지입니다.', 'ru': 'Это колония на далёкой планете.',
                           'de': 'Dies ist eine Kolonie auf einem fernen Planeten.',
                           'fr': 'C’est une colonie sur une planète lointaine.',
                           'es': 'Esta es una colonia en un planeta lejano.',
                           'pt': 'Esta é uma colónia num planeta distante.'}
                for code in sorted(samples):
                    assert code in inventory['languages'], 'Missing pack: ' + code
                    output = translate('This is a colony on a distant planet.', target=code)
                    assert output.strip() and '__RT_TOKEN_' not in output, (code, output)
                    back = translate(samples[code], source=code, target='en')
                    assert back.strip() and any(c.isascii() and c.isalpha() for c in back), (code, back)
                    print('PASS: real en <-> ' + code + ': ' + output + ' / ' + back, flush=True)
                output = translate('__RT_TOKEN_0__ ' + samples['ja'], source='ja', target='zh')
                assert output.count('__RT_TOKEN_0__') == 1 and any('\u4e00' <= c <= '\u9fff' for c in output), output
                print('PASS: Japanese -> English -> Chinese pivot with protected glossary token: ' + output, flush=True)
            parent.terminate()
            parent.wait(timeout=10)
            process.wait(timeout=10)
            assert process.returncode == 0, process.returncode
            print('PASS: engine exits automatically when its parent exits', flush=True)
        finally:
            for owned in (process, parent):
                if owned.poll() is None:
                    owned.kill()
                owned.wait(timeout=10)


if __name__ == '__main__':
    main()
