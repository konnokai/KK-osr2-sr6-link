#!/usr/bin/env pwsh
<#
.SYNOPSIS
    把 plugin 和 WPF 的 Release 產物集中到 bin，資料夾結構跟遊戲根目錄一樣。

.DESCRIPTION
    輸出結構：
        bin/BepInEx/plugins/KKS_osr2_sr6_link.dll
        bin/BepInEx/plugins/KKS_osr2_sr6_link.pdb
        bin/Link_osr2_sr6_to_kk_studio_wpf/（WPF 輸出資料夾整個複製）

    整個 bin 底下的內容可以直接蓋到遊戲根目錄（例如 F:\Koikatsu Sunshine）。

    plugin 只取自己的 dll 和 pdb。輸出資料夾裡的 BepInEx、KKSAPI、Unity 等組件是編譯時
    從遊戲複製過來的參考，遊戲本身就有，蓋回去反而可能換掉別的版本。

    WPF 的 config.ini 是使用者自己的設定，不在建置輸出裡，所以複製過去不會蓋掉。
    覆蓋 plugin 前要先關掉 CharaStudio，覆蓋 WPF 前要先關掉 WPF 程式。

.PARAMETER SkipBuild
    跳過 Release 建置，只做複製。

.EXAMPLE
    ./stage.ps1
    建置後輸出到 bin

.EXAMPLE
    ./stage.ps1 -SkipBuild
    不建置，直接把現有產物複製到 bin
#>
[CmdletBinding()]
param(
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$outputRoot = Join-Path $repoRoot 'bin'

$pluginProject = Join-Path $repoRoot 'plugin/kk_osr2_sr6_link.csproj'
$pluginName = 'KKS_osr2_sr6_link'
$pluginSource = Join-Path $repoRoot 'plugin/bin/Release'
$pluginTarget = Join-Path $outputRoot 'BepInEx/plugins'

$wpfProject = Join-Path $repoRoot 'wpf/KKOsr2Sr6Link.Wpf/KKOsr2Sr6Link.Wpf.csproj'
# 跟遊戲目錄裡現有的 WPF 資料夾同名，蓋回去才會落在同一個地方
$wpfTarget = Join-Path $outputRoot 'Link_osr2_sr6_to_kk_studio_wpf'

# 目標框架從 csproj 讀，升級 .NET 後不用改腳本
$project = [xml](Get-Content -LiteralPath $wpfProject -Raw)
$tfm = $project.Project.PropertyGroup.TargetFramework | Where-Object { $_ } | Select-Object -First 1
if (-not $tfm) {
    throw "讀不到 $wpfProject 的 <TargetFramework>"
}
$wpfSource = Join-Path $repoRoot "wpf/KKOsr2Sr6Link.Wpf/bin/Release/$tfm"

if (-not $SkipBuild) {
    foreach ($csproj in $pluginProject, $wpfProject) {
        Write-Host "建置 Release：$csproj" -ForegroundColor Cyan
        & dotnet build $csproj --configuration Release --no-restore --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "建置失敗（dotnet build 結束碼 $LASTEXITCODE），未進行複製"
        }
    }
}

$pluginFiles = "$pluginName.dll", "$pluginName.pdb" | ForEach-Object { Join-Path $pluginSource $_ }
foreach ($file in $pluginFiles) {
    if (-not (Test-Path -LiteralPath $file)) {
        throw "找不到 plugin 產物：$file"
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $wpfSource 'Link_osr2_sr6_to_kk_studio.exe'))) {
    throw "找不到 WPF 產物：$wpfSource\Link_osr2_sr6_to_kk_studio.exe（csproj 的 TargetFramework 是 $tfm）"
}

# 整個重做，避免舊版殘留的檔案被一起帶走
if (Test-Path -LiteralPath $outputRoot) {
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $pluginTarget | Out-Null
$pluginFiles | Copy-Item -Destination $pluginTarget
Write-Host "plugin -> $pluginTarget" -ForegroundColor Green
Write-Host "    $pluginName.dll, $pluginName.pdb"

Copy-Item -LiteralPath $wpfSource -Destination $wpfTarget -Recurse
$wpfCount = (Get-ChildItem -LiteralPath $wpfTarget -File -Recurse).Count
Write-Host "WPF ($tfm) -> $wpfTarget" -ForegroundColor Green
Write-Host "    $wpfCount 個檔案"

Write-Host "完成：$outputRoot" -ForegroundColor Cyan
Write-Host "把 $outputRoot 底下的內容複製到遊戲根目錄即可（先關掉 CharaStudio 和 WPF 程式）"
