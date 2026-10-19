$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$sources = @(Get-ChildItem (Join-Path $projectRoot 'Source') -Recurse -Filter '*.cs' |
    Where-Object Name -NotIn @('RuntimeTranslatorMod.cs', 'ListingExtensions.cs', 'GlossaryTab.cs') |
    ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw })
$sources += @(Get-ChildItem $PSScriptRoot -Filter '*.cs' | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw })
$usings = $sources | ForEach-Object {
    [regex]::Matches($_, '(?m)^using [^;]+;') | ForEach-Object Value
} | Sort-Object -Unique
$bodies = $sources | ForEach-Object { [regex]::Replace($_, '(?m)^using [^;]+;\r?\n', '') }
Add-Type -TypeDefinition (($usings -join "`n") + "`n" + ($bodies -join "`n"))
$testFolder = Join-Path ([IO.Path]::GetTempPath()) ('runtime-translator-tests-' + [guid]::NewGuid().ToString('N'))
[RuntimeTranslator.Tests.SelfAndShutdownChecks]::ProjectRoot = $projectRoot
[RuntimeTranslator.Tests.RegressionChecks]::Run($testFolder)
