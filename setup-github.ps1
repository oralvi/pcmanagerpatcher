# PCManagerPatcher - 一键 GitHub 推送脚本
# 使用方法: 修改下面的参数，然后运行此脚本

param(
    [Parameter(Mandatory = $true)]
    [string]$GitHubUsername,
    
    [Parameter(Mandatory = $false)]
    [string]$GitHubToken = "",  # 可选：如果需要 HTTPS 认证
    
    [Parameter(Mandatory = $false)]
    [string]$RepoName = "pcmanagerpatcher"
)

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $projectRoot) {
    $projectRoot = (Get-Location).Path
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "PCManagerPatcher - GitHub Push Setup" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

Write-Host "项目路径: $projectRoot" -ForegroundColor Gray
Write-Host "GitHub 用户: $GitHubUsername" -ForegroundColor Gray
Write-Host "仓库名: $RepoName`n" -ForegroundColor Gray

Set-Location $projectRoot

# 1. 检查 git 状态
Write-Host "✓ 检查 git 状态..." -ForegroundColor Yellow
git status > $null 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ 错误：未初始化 git 仓库" -ForegroundColor Red
    exit 1
}

# 2. 配置 git 用户信息（如果未配置）
Write-Host "✓ 配置 git 用户信息..." -ForegroundColor Yellow
$userName = git config user.name
if (-not $userName) {
    git config user.name "PC Manager Patcher" | Out-Null
    git config user.email "patcher@localhost" | Out-Null
}

# 3. 检查是否有未提交的更改
$status = git status --porcelain
if ($status) {
    Write-Host "✓ 存在未提交的更改，正在提交..." -ForegroundColor Yellow
    git add .
    git commit -m "Pre-release updates" | Out-Null
}

# 4. 添加远程仓库
Write-Host "✓ 配置远程仓库..." -ForegroundColor Yellow
$remoteUrl = "https://github.com/$GitHubUsername/$RepoName.git"

$existingRemote = git remote get-url origin 2>$null
if ($existingRemote -and $existingRemote -ne "origin") {
    git remote remove origin 2>$null
}

git remote add origin $remoteUrl 2>$null
git remote set-url origin $remoteUrl

# 5. 改分支名为 main
Write-Host "✓ 确保分支为 main..." -ForegroundColor Yellow
$currentBranch = git rev-parse --abbrev-ref HEAD
if ($currentBranch -ne "main") {
    git branch -M main 2>$null
}

# 6. 推送到远程
Write-Host "✓ 推送代码到 GitHub..." -ForegroundColor Yellow
git push -u origin main --force 2>&1 | Write-Host -ForegroundColor Gray

if ($LASTEXITCODE -ne 0) {
    Write-Host "`n⚠ 推送失败。请确保：" -ForegroundColor Yellow
    Write-Host "  1. GitHub 账户已登录 (git credential 或 ssh key 已配置)" -ForegroundColor Gray
    Write-Host "  2. 仓库 '$RepoName' 已在 GitHub 上创建" -ForegroundColor Gray
    Write-Host "  3. 推送 URL: $remoteUrl" -ForegroundColor Gray
    exit 1
}

# 7. 创建并推送 release 标签
Write-Host "`n✓ 创建 release 标签 v1.0.0..." -ForegroundColor Yellow
git tag -d v1.0.0 2>$null  # 删除本地旧标签（如果存在）
git tag v1.0.0
git push origin v1.0.0 --force 2>&1 | Write-Host -ForegroundColor Gray

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "✓ 推送完成！" -ForegroundColor Green
Write-Host "========================================`n" -ForegroundColor Green

Write-Host "后续步骤：" -ForegroundColor Cyan
Write-Host "1. 前往 https://github.com/$GitHubUsername/$RepoName" -ForegroundColor Gray
Write-Host "2. 检查 'Releases' 页面，GitHub Actions 正在编译..." -ForegroundColor Gray
Write-Host "3. 编译完成后，PCManagerPatcher.exe 将自动上传到 Release" -ForegroundColor Gray
Write-Host "`n提示:" -ForegroundColor Cyan
Write-Host "- 后续更新时运行: git add . && git commit && git push && git tag v1.0.X && git push origin v1.0.X" -ForegroundColor Gray
Write-Host "- 或重新运行此脚本: .\setup-github.ps1 -GitHubUsername $GitHubUsername" -ForegroundColor Gray
