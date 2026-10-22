# SPDX-License-Identifier: AGPL-3.0-only
# Copyright 2026 Gesvd and Codex. Local integration, 2026-10-19.
import argparse
import ctypes
import os
from pathlib import Path
import re
import socket
import sys
import threading

ROOT = Path(__file__).resolve().parent


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--port', type=int, required=True)
    parser.add_argument('--parent-pid', type=int, required=True)
    parser.add_argument('--identity', required=True)
    parser.add_argument('--data', type=Path, required=True)
    parser.add_argument('--pack', action='append', type=Path, default=[])
    args = parser.parse_args()
    args.data.mkdir(parents=True, exist_ok=True)
    log = open(args.data / 'engine.log', 'a', encoding='utf-8', buffering=1)
    sys.stdout = sys.stderr = log
    os.chdir(args.data)

    # A killed game cannot leave the model process running.
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.OpenProcess.restype = ctypes.c_void_p
    kernel.WaitForSingleObject.argtypes = [ctypes.c_void_p, ctypes.c_ulong]
    kernel.CloseHandle.argtypes = [ctypes.c_void_p]
    parent = kernel.OpenProcess(0x00100000, False, args.parent_pid)
    if not parent:
        raise RuntimeError('Cannot watch parent process')
    def watch_parent():
        result = kernel.WaitForSingleObject(parent, 0xFFFFFFFF)
        kernel.CloseHandle(parent)
        os._exit(0 if result == 0 else 1)
    threading.Thread(target=watch_parent, daemon=True).start()

    for name in ('XDG_DATA_HOME', 'XDG_CONFIG_HOME', 'XDG_CACHE_HOME'):
        os.environ[name] = str(args.data / name.lower())
    os.environ.update(ARGOS_PACKAGES_DIR=str(ROOT / 'models' / 'packages'),
                      ARGOS_CHUNK_TYPE='MINISBD', ARGOS_DEVICE_TYPE='cpu',
                      ARGOS_INTRA_THREADS='2', ARGOS_INTER_THREADS='1',
                      ARGOS_MODEL_PROVIDER='OPENNMT')

    # Enforce offline operation even if an upstream boot path attempts a download.
    connect = socket.socket.connect
    def local_connect(sock, address):
        if sock.family in (socket.AF_INET, socket.AF_INET6) and address[0] not in ('127.0.0.1', '::1'):
            raise OSError('Bundled translation engine is offline; outbound connections are disabled')
        return connect(sock, address)
    socket.socket.connect = local_connect

    from argostranslate import package, translate, settings
    settings.package_dirs = list(dict.fromkeys([ROOT / 'models' / 'packages'] +
        [folder / 'models' / 'packages' for folder in args.pack if (folder / 'models' / 'packages').is_dir()]))
    class SentenceSplitter:
        def __init__(self, package):
            pass

        def split_sentences(self, text):
            # shortcut: abbreviations may split early; replace if game text quality needs a trained splitter.
            return re.split(r'(?<=[.!?])\s+', text)

    # Avoid shipping a second model with a separate training-data license chain.
    translate.MiniSBDSentencizer = SentenceSplitter
    import libretranslate.init
    def offline_boot(*unused):
        installed = package.get_installed_packages()
        if not installed:
            raise RuntimeError('No offline models installed. Enable the required RimLingo language expansion mods and restart RimWorld.')
    # Upstream boot downloads models when fewer than two are installed.
    libretranslate.init.boot = offline_boot
    from libretranslate.main import get_parser
    from libretranslate.app import create_app
    from flask import request, jsonify, send_file
    from waitress import serve
    installed = package.get_installed_packages()
    codes = sorted({code for p in installed for code in (p.from_code, p.to_code)})
    config = get_parser().parse_args(['--load-only', ','.join(codes), '--disable-web-ui',
                                     '--disable-files-translation', '--req-limit', '-1'])
    app = create_app(config)
    app.config['MAX_CONTENT_LENGTH'] = 1024 * 1024
    warmup = next((p for p in installed if p.from_code == 'en' and p.to_code == 'zh'), installed[0])
    translate.get_translation_from_codes(warmup.from_code, warmup.to_code).translate('Hello world.')  # Fail before health reports ready.
    token = re.compile(r'(__RT_TOKEN_\d+__)')
    model_lock = threading.Lock()

    @app.get('/runtime-translator/health')
    def health():
        return args.identity

    @app.get('/runtime-translator/source')
    def source():
        return send_file(ROOT / 'sources.zip', as_attachment=True)

    @app.get('/runtime-translator/languages')
    def languages():
        return jsonify(languages=codes, pairs=[{'source': p.from_code, 'target': p.to_code} for p in installed])

    @app.post('/runtime-translator/translate')
    def protected_translate():
        data = request.get_json(silent=True)
        if not isinstance(data, dict):
            return jsonify(error='Expected a JSON object'), 400
        query = data.get('q')
        batch = isinstance(query, list)
        if not isinstance(query, str) and not (batch and 0 < len(query) <= 10000 and all(isinstance(item, str) for item in query)):
            return jsonify(error='Expected q to be a string or a nonempty list of strings'), 400
        source, target = data.get('source'), data.get('target')
        if source not in codes or target not in codes or source == target:
            return jsonify(error='Unavailable offline language pair. Enable the required RimLingo language expansion mods and restart RimWorld.'), 400
        try:
            model = translate.get_translation_from_codes(source, target)
        except Exception:
            return jsonify(error='No installed translation route for this language pair.'), 400
        if model is None:
            return jsonify(error='No installed translation route for this language pair.'), 400
        outputs, errors = [], []
        for text in query if batch else [query]:
            try:
                parts = token.split(text)
                with model_lock:
                    # Machine translation changes placeholder spelling; preserve tokens verbatim.
                    output = ''.join(part if token.fullmatch(part) or not part.strip()
                                     else part[:len(part)-len(part.lstrip())] + model.translate(part.strip())
                                     + part[len(part.rstrip()):] for part in parts)
                outputs.append(output)
                errors.append(None)
            except Exception as error:
                if not batch:
                    return jsonify(error=str(error)[:2000]), 500
                outputs.append(None)
                errors.append(str(error)[:2000])
        return jsonify(translatedTexts=outputs, errors=errors) if batch else jsonify(translatedText=outputs[0])

    print('LibreTranslate ready; source and licenses: ' + str(ROOT), flush=True)
    serve(app, host='127.0.0.1', port=args.port, threads=2, clear_untrusted_proxy_headers=True)


if __name__ == '__main__':
    main()
