#requires -version 3.0
<#
    一键把本目录发布到 GitHub（首次初始化 + 提交 + 推送）。

    用法：
        powershell -NoProfile -ExecutionPolicy Bypass -File .\publish.ps1
        powershell -NoProfile -ExecutionPolicy Bypass -File .\publish.ps1 -RepoUrl https://github.com/你的名字/txt-file-manager.git

    前置条件：
      1. 已安装 Git for Windows：https://git-scm.com/download/win
         （安装后在新的终端里能运行 git --version）
      2. 已在 GitHub 网页上创建好一个空仓库（不要勾选 README / .gitignore / LICENSE），
         因为本目录里已经有这些文件。

    说明：
      - 本脚本只操作当前目录，不会改动其他任何位置。
      - 首次提交前若没有配置 git 身份，会写入本仓库的 local 配置（不影响全局）。
#>

param(
    [string]$RepoUrl = '',
    [string]$Branch = 'main',
    [string]$UserName = '',
    [string]$UserEmail = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $root

function Step($text) { Write-Host ""; Write-Host "== $text" -ForegroundColor Cyan }
function Info($text) { Write-Host "   $text" -ForegroundColor DarkGray }

# ---------------------------------------------------------------- 1. 检查 git
Step '检查 Git'
$git = Get-Command git -ErrorAction SilentlyContinue
if (-not $git) {
    Write-Host "   没有找到 git 命令。" -ForegroundColor Yellow
    Write-Host "   请先安装 Git for Windows：https://git-scm.com/download/win" -ForegroundColor Yellow
    Write-Host "   安装完成后关掉这个窗口，重新打开一个终端再运行本脚本。" -ForegroundColor Yellow
    exit 1
}
Info "git: $($git.Source)"
Info (& git --version)

# ---------------------------------------------------------- 2. 提交身份配置
Step '检查提交身份'
$name = (& git config user.name)
$mail = (& git config user.email)
if (-not $name -or -not $mail) {
    if (-not $UserName) { $UserName = 'txt-file-manager' }
    if (-not $UserEmail) { $UserEmail = 'txt-file-manager@users.noreply.github.com' }
    if ($UserName -and $UserEmail) {
        & git config --local user.name $UserName
        & git config --local user.email $UserEmail
        Info "已写入本仓库配置: $UserName <$UserEmail>"
    }
} else {
    Info "使用已有配置: $name <$mail>"
}

# ------------------------------------------------------------- 3. 初始化仓库
Step '初始化本地仓库'
if (-not (Test-Path (Join-Path $root '.git'))) {
    & git init -b $Branch | Out-Null
    Info "已 git init（分支 $Branch）"
} else {
    Info '已存在 .git，跳过初始化'
    $current = (& git rev-parse --abbrev-ref HEAD)
    if ($current -and $current -ne $Branch) {
        & git branch -M $Branch
        Info "当前分支已重命名为 $Branch"
    }
}

# ------------------------------------------------------------- 4. 关联远端
if (-not $RepoUrl) {
    Write-Host ""
    Write-Host "请输入 GitHub 仓库地址（例如 https://github.com/你的名字/txt-file-manager.git）" -ForegroundColor Yellow
    Write-Host "留空则只做本地提交、不推送：" -NoNewline
    $RepoUrl = Read-Host
}

if ($RepoUrl) {
    Step '关联远端 origin'
    $existing = (& git remote)
    if ($existing -contains 'origin') {
        & git remote set-url origin $RepoUrl
        Info "已更新 origin -> $RepoUrl"
    } else {
        & git remote add origin $RepoUrl
        Info "已添加 origin -> $RepoUrl"
    }
}

# --------------------------------------------------- 4.5 把徽章里的占位符换成真实仓库
if ($RepoUrl -and ($RepoUrl -match 'github\.com[:/](?<owner>[^/]+)/(?<repo>[^/\s]+?)(?:\.git)?/?$')) {
    $slug = "$($Matches['owner'])/$($Matches['repo'])"
    $readme = Join-Path $root 'README.md'
    if ((Test-Path $readme) -and $slug) {
        $txt = [IO.File]::ReadAllText($readme, [Text.Encoding]::UTF8)
        if ($txt.Contains('USER/txt-file-manager')) {
            $txt = $txt.Replace('USER/txt-file-manager', $slug)
            [IO.File]::WriteAllText($readme, $txt, (New-Object Text.UTF8Encoding $false))
            Info "README 徽章已指向 $slug"
        }
    }
}

# --------------------------------------------------------------- 5. 提交
Step '提交全部文件'
& git add -A
$staged = (& git diff --cached --name-only)
if (-not $staged) {
    Info '没有需要提交的改动'
} else {
    Info "共 $((($staged -split "`n") | Where-Object { $_.Trim() }).Count) 个文件"
    & git commit -m 'chore: 首次提交 TXT 文件管理器 v1.0.0' | Out-Null
    Info '已提交'
}

# --------------------------------------------------------------- 6. 推送
if (-not $RepoUrl) {
    Write-Host ""
    Write-Host "已完成本地提交。之后可用下面两条命令推送：" -ForegroundColor Yellow
    Write-Host "    git remote add origin <仓库地址>" -ForegroundColor Yellow
    Write-Host "    git push -u origin $Branch" -ForegroundColor Yellow
    exit 0
}

Step '推送到 GitHub'
& git push -u origin $Branch
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "推送失败。常见原因：" -ForegroundColor Yellow
    Write-Host "  - 远端仓库不是空的（新建仓库时不要勾选 README/License），可先 git pull --rebase origin $Branch 再重试" -ForegroundColor Yellow
    Write-Host "  - 需要登录：GitHub 已不支持密码，请用 Personal Access Token 作密码，或用 GitHub Desktop / SSH" -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "发布完成。" -ForegroundColor Green
Write-Host "仓库地址：$RepoUrl" -ForegroundColor Green
Write-Host "建议接着做两件事：" -ForegroundColor Cyan
Write-Host "  1. 在仓库 Settings 里把 About 填上：全盘检索本机 .txt，查看详情并就地编辑（Windows 原生单文件 exe）"
Write-Host "  2. 打 tag 自动发 Release（.github/workflows/build.yml 会自动编译并附上 exe）："
Write-Host "         git tag v1.0.0 && git push origin v1.0.0"
