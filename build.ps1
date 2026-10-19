param([switch]$Test)
$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.8 / Windows x64 is required.' }
$outDir = Join-Path $repoRoot 'dist'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Runtime.Serialization.dll','System.Xaml.dll') | ForEach-Object { '/r:' + (Join-Path $framework $_) }
$references += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/r:' + (Join-Path $framework ('WPF\' + $_)) }
$sources = @((Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Filter '*.cs').FullName)
$sources += @((Get-ChildItem -LiteralPath (Join-Path $repoRoot 'tests') -Filter '*.cs').FullName)
$executable = Join-Path $outDir 'PeripheralHalo.exe'
$iconOptions = @()
if (Test-Path -LiteralPath (Join-Path $repoRoot 'assets\app.ico')) { $iconOptions += '/win32icon:' + (Join-Path $repoRoot 'assets\app.ico') }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output "/out:$executable" "/win32manifest:$(Join-Path $repoRoot 'app.manifest')" "/resource:$(Join-Path $repoRoot 'src\razer-models.txt'),razer-models.txt" "/resource:$(Join-Path $repoRoot 'src\razer-keyboards.txt'),razer-keyboards.txt" $iconOptions $references $sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Copy-Item -LiteralPath (Join-Path $repoRoot 'PeripheralHalo.exe.config') -Destination $outDir
foreach ($document in @('README.md','LICENSE','THIRD_PARTY_NOTICES.md')) { if (Test-Path -LiteralPath (Join-Path $repoRoot $document)) { Copy-Item -LiteralPath (Join-Path $repoRoot $document) -Destination $outDir } }
if (Test-Path -LiteralPath (Join-Path $repoRoot 'docs')) { Copy-Item -LiteralPath (Join-Path $repoRoot 'docs') -Destination $outDir -Recurse -Force }
if ($Test) {
    $resultPath = Join-Path $outDir 'test-results.txt'
    $process = Start-Process -FilePath $executable -ArgumentList @('--self-test', ('"' + $resultPath + '"')) -Wait -PassThru -WindowStyle Hidden
    Get-Content -LiteralPath $resultPath
    if ($process.ExitCode -ne 0) { throw 'Self-tests failed.' }
}
Write-Output "Built: $executable"
