$ErrorActionPreference = 'Stop'
$source = $PSScriptRoot
$destination = Join-Path $source 'bin'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$refs = @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Xaml.dll','System.Windows.Forms.dll','System.Drawing.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/r:' + (Join-Path $framework $_) }
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ("/win32icon:" + (Join-Path $source 'DayDesk.ico')) ("/out:" + (Join-Path $destination 'DayDesk.exe')) $refs (Join-Path $source 'Core.cs') (Join-Path $source 'Applications.cs') (Join-Path $source 'ApplicationsDialog.cs') (Join-Path $source 'WindowAccess.cs') (Join-Path $source 'UI.cs') (Join-Path $source 'Dialogs.cs') (Join-Path $source 'NotebookDialog.cs') (Join-Path $source 'Drawer.cs') (Join-Path $source 'App.cs')
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Output (Join-Path $destination 'DayDesk.exe')
