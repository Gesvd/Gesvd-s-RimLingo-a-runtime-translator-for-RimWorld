# SPDX-License-Identifier: GPL-3.0-only
param([Parameter(Mandatory=$true)][string]$PythonHome)
$ErrorActionPreference = 'Stop'
$engineRoot = $PSScriptRoot
$pythonSource = (Resolve-Path -LiteralPath $PythonHome).Path
if (-not (Test-Path -LiteralPath (Join-Path $pythonSource 'python.exe'))) { throw 'Provide a portable Windows x64 CPython 3.12 directory.' }
$runtime = Join-Path $engineRoot 'runtime'
New-Item -ItemType Directory -Path $runtime -Force | Out-Null
foreach ($name in @('DLLs','Lib','LICENSE.txt','BUILD','python.exe','python3.dll','python312.dll','vcruntime140.dll','vcruntime140_1.dll')) {
    $item = Join-Path $pythonSource $name
    if (Test-Path -LiteralPath $item) { Copy-Item -LiteralPath $item -Destination $runtime -Recurse -Force }
}
$python = Join-Path $runtime 'python.exe'
$env:UV_CACHE_DIR = Join-Path $engineRoot 'build-cache'
uv pip install --python $python --target (Join-Path $runtime 'Lib\site-packages') --requirements (Join-Path $engineRoot 'requirements-lock.txt') --index-url https://pypi.org/simple
if ($LASTEXITCODE) { throw 'Dependency installation failed' }
& $python -X utf8 (Join-Path $engineRoot 'prepare.py')
if ($LASTEXITCODE) { throw 'Release preparation failed' }
& $python -X utf8 (Join-Path $engineRoot 'test_engine.py')
if ($LASTEXITCODE) { throw 'Offline integration test failed' }

& $python -X utf8 (Join-Path $engineRoot 'package.py')
if ($LASTEXITCODE) { throw 'Packaging failed' }
