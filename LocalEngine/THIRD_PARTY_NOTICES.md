# Third-party notices and redistribution review

Reviewed 2026-10-19; project licensing scope updated 2026-10-20. This records upstream licensing evidence and the conditions used for this bundle; it is not a legal opinion about every jurisdiction or distribution platform.

## Our project code

Copyright (c) 2026 Gesvd. Project authors: Gesvd & Codex. The original C# mod, tests, project files and our build/preparation/package scripts are licensed under GPL-3.0-only. The C# mod has the additional RimWorld/Unity linking permission in [LICENSE-EXCEPTION.md](../LICENSE-EXCEPTION.md). See [LICENSING.md](../LICENSING.md) and [LICENSE](../LICENSE). This permission does not apply to LibreTranslate or third-party code. Earlier copies distributed under MIT retain their original grant.

## LibreTranslate and the local service

LibreTranslate 1.9.6 is AGPL-3.0. Its upstream source and license:
https://github.com/LibreTranslate/LibreTranslate
https://raw.githubusercontent.com/LibreTranslate/LibreTranslate/main/LICENSE

Redistribution is permitted subject to the AGPL conditions, including keeping notices and making Corresponding Source available (sections 4–6); modified network services must offer source to users (section 13). Local integration `serve.py`, copyright 2026 Gesvd and Codex, is released under AGPL-3.0-only, without warranty. It adds offline boot, local-only networking, parent-process cleanup, health/source endpoints, protected-token translation and standard-library sentence splitting. Upstream installed source files are not edited. The service runs independently and the GPL-3.0-only C# mod (with the project-specific RimWorld/Unity linking exception) uses HTTP; we treat this distribution as an aggregate under section 5. The third-party licenses are not replaced by the project's GPL LICENSE.

The bundle includes copyleft source distributions, installed Python source, retained package notices and build scripts; `dependencies.json` identifies exact versions, upstream URLs and source hashes. `sources.zip` serves this source material at `/runtime-translator/source`. Distributors must keep equivalent source access alongside binary downloads for as long as required, including dependency and native-library source; do not strip notices or publish only the DLL. PyMuPDF/MuPDF, LexiLang, MiniSBD and pysrt also have copyleft terms. PyMuPDF is pinned to 1.24.11; its build requires MuPDF 1.24.10, whose complete source archive is included separately.

## Translation model: English → Chinese 1.9

Original file: https://argos-net.com/v1/translate-en_zh-1_9.argosmodel
SHA-256: 433e7c4f034d87fbe2353161e05f18646d7999452f801a4e1f0378522b9850ab
Argos maintainer explicitly states that .argosmodel binaries are under MIT/CC0:
https://github.com/argosopentech/argos-translate/issues/533

The actual package README additionally states that the original OPUS model is CC BY 4.0. We retain that README and the CC BY 4.0 legal text and provide this attribution:

Jörg Tiedemann and Santhosh Thottingal (2020), “OPUS-MT — Building open translation services for the World”, Proceedings of the 22nd Annual Conference of the European Association for Machine Translation, Lisbon, Portugal.
https://aclanthology.org/2020.eamt-1.61/
https://creativecommons.org/licenses/by/4.0/

The distributed Argos package is converted from the OPUS model by upstream. This bundle removes its unused Stanza tokenizer and uses a standard-library sentence splitter; the translation weights and SentencePiece file are unchanged. No endorsement by these authors or projects is implied.

## Sentence splitting

The local integration uses Python standard-library regular expressions for sentence boundaries. It ships no MiniSBD or Stanza tokenizer weights. The original unused Stanza directory is removed from the model package; the original download stays only in build-cache and must not be distributed. MiniSBD's Python library remains an upstream Argos dependency, and its AGPL source and license are preserved. The standard-library splitter may split abbreviations early.

## Python and other dependencies

Portable CPython 3.12.13 is PSF-licensed, with bundled-library notices in runtime/LICENSE.txt, runtime/Lib and runtime/DLLs; preserve them. Runtime build provenance: python-build-standalone build 20260623, https://github.com/astral-sh/python-build-standalone. Argos Translate code is MIT/CC0; CTranslate2 and ONNX Runtime are MIT, SentencePiece Apache-2.0. The unused NVIDIA cuDNN DLL from the CTranslate2 wheel is excluded from this CPU-only bundle. The OpenMP runtime notice is retained separately. Upstream CTranslate2 Windows binaries also use oneDNN, Intel performance libraries and CUDA build components; their separate Apache/OpenMP, Intel and NVIDIA terms are retained under licenses/, and do not become MIT merely because the Python package is MIT. Native source archive URLs for CTranslate2/ONNX Runtime are recorded in dependencies.json. Remaining dependencies retain their own licenses and copyright notices under licenses/ and their installed dist-info directories. `dependencies.json` is the complete inventory, including exact source archive links rather than just project homepages.

Do not bundle or redistribute RimWorld's Assembly-CSharp.dll or Unity DLLs with this mod. Build references are provided by the player's installation.
