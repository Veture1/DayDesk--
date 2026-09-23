$ErrorActionPreference = 'Stop'
$base = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs = @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/r:' + (Join-Path $framework $_) }
$sources = @('Core.cs','UI.cs','Dialogs.cs','NotebookDialog.cs','Drawer.cs','App.cs') | ForEach-Object { Join-Path $base $_ }
& (Join-Path $framework 'csc.exe') /nologo /target:exe /main:UiChecks /codepage:65001 ("/out:" + (Join-Path $env:TEMP 'DayDesk-UiChecks.exe')) $refs $sources (Join-Path $PSScriptRoot 'UiChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'UI check build failed' }
$testOutput = Join-Path $env:TEMP ('DayDesk-ui-checks-' + (Get-Date -Format yyyyMMdd-HHmmss))
& (Join-Path $env:TEMP 'DayDesk-UiChecks.exe') $testOutput
if ($LASTEXITCODE -ne 0) { throw 'UI checks failed' }
