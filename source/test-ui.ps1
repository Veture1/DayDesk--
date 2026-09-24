param([string]$AppPath = '')
$ErrorActionPreference = 'Stop'
$base = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($AppPath)) { $AppPath = Join-Path $base 'bin\DayDesk.exe' }
$AppPath = (Resolve-Path -LiteralPath $AppPath).Path
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs = @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Xaml.dll','System.Windows.Forms.dll','System.Drawing.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/r:' + (Join-Path $framework $_) }
$sources = @('Core.cs','Applications.cs','ApplicationsDialog.cs','UI.cs','Dialogs.cs','NotebookDialog.cs','Drawer.cs','WindowAccess.cs','App.cs') | ForEach-Object { Join-Path $base $_ }
& (Join-Path $framework 'csc.exe') /nologo /target:exe /main:UiChecks /codepage:65001 ("/out:" + (Join-Path $env:TEMP 'DayDesk-UiChecks.exe')) $refs $sources (Join-Path $PSScriptRoot 'UiChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'UI check build failed' }
$testOutput = Join-Path $env:TEMP ('DayDesk-ui-checks-' + (Get-Date -Format yyyyMMdd-HHmmss) + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
& (Join-Path $env:TEMP 'DayDesk-UiChecks.exe') $testOutput $AppPath
if ($LASTEXITCODE -ne 0) { throw 'UI checks failed' }
