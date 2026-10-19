# SPDX-License-Identifier: MIT
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
        process = subprocess.Popen([sys.executable, '-X', 'utf8', str(ROOT / 'serve.py'), '--port', str(port),
                                    '--parent-pid', str(parent.pid), '--identity', identity, '--data', data],
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
            output = translate('Hello world. This is a colony on a distant planet.')
            assert any('\u4e00' <= c <= '\u9fff' for c in output), output
            print('PASS: real English -> Chinese translation: ' + output, flush=True)
            output = translate('__RT_TOKEN_0__ The __RT_TOKEN_1__ needs food. __RT_TOKEN_2__\n')
            for token in ('__RT_TOKEN_0__', '__RT_TOKEN_1__', '__RT_TOKEN_2__'):
                assert output.count(token) == 1, output
            assert output.endswith('\n'), repr(output)
            print('PASS: glossary/format placeholders and paragraph whitespace preserved', flush=True)
            assert translate('__RT_TOKEN_0__') == '__RT_TOKEN_0__'
            assert translate('') == ''
            try:
                translate('Hello', target='ja')
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
