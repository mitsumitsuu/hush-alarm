$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$buildRoot = Join-Path $projectRoot 'build\windows'
New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /codepage:65001 /target:exe /out:"$buildRoot\LogicTests.exe" "$projectRoot\windows\Core.cs" "$projectRoot\windows\Tests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
& "$buildRoot\LogicTests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
& $compiler /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ /win32icon:"$projectRoot\assets\HushAlarm.ico" /win32manifest:"$projectRoot\windows\app.manifest" /out:"$buildRoot\HushAlarm.exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Security.dll "$projectRoot\windows\Core.cs" "$projectRoot\windows\Audio.cs" "$projectRoot\windows\App.cs"
if ($LASTEXITCODE -ne 0) { throw 'App compilation failed' }
Write-Output "Built $buildRoot\HushAlarm.exe"
