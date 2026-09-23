$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$destination = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
& (Join-Path $framework 'csc.exe') /nologo /target:exe /codepage:65001 ("/out:" + (Join-Path $destination 'CoreTests.exe')) ("/r:" + (Join-Path $framework 'System.Web.Extensions.dll')) (Join-Path $PSScriptRoot 'Core.cs') (Join-Path $PSScriptRoot 'CoreTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
& (Join-Path $destination 'CoreTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
