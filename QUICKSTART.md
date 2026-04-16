# 快速开始指南

## 第一步：在 GitHub 上创建仓库

1. 登录 [github.com](https://github.com)
2. 点击右上角 **+** → **New repository**
3. 仓库名：`pcmanagerpatcher`
4. 描述：`Precise .NET IL patcher for Xiaomi PC Manager`
5. 选择 **Public**（开源）
6. **不需要** 勾选 "Initialize with README" (我们已经有了)
7. 点击 **Create repository**

## 第二步：运行自动推送脚本

```powershell
cd d:\projects\reverse-analysis-workspace\projects\pcmanagerdll

# 运行推送脚本（替换为你的 GitHub 用户名）
.\setup-github.ps1 -GitHubUsername your_github_username
```

**例如：**
```powershell
.\setup-github.ps1 -GitHubUsername john_doe
```

脚本会自动：
- ✓ 配置 git 用户信息
- ✓ 添加远程仓库
- ✓ 推送所有代码到 main 分支
- ✓ 创建 `v1.0.0` 标签
- ✓ 触发 GitHub Actions 自动编译

## 第三步：等待自动编译完成

1. 前往 https://github.com/your_github_username/pcmanagerpatcher
2. 点击 **Actions** 标签
3. 等待 "Build and Release" 工作流完成（通常 3-5 分钟）
4. 完成后，点击 **Releases** 即可下载 `PCManagerPatcher.exe`

## 后续更新发版

```powershell
cd d:\projects\reverse-analysis-workspace\projects\pcmanagerdll

# 作出代码修改...

# 提交并发新版本
git add .
git commit -m "描述你的改动"
git push origin main

git tag v1.0.1
git push origin v1.0.1
```

GitHub Actions 会自动编译新版本。

---

**常见问题**

**Q: 推送失败 "fatal: could not read Username"**  
A: 需要配置 git 认证。选一个方案：
- 安装 [GitHub CLI](https://cli.github.com/)：`gh auth login`
- 或配置 SSH Key：[生成 SSH 密钥](https://docs.github.com/en/authentication/connecting-to-github-with-ssh/generating-a-new-ssh-public-and-private-key)

**Q: Actions 编译失败**  
A: 检查 GitHub 账户的 Actions 权限未被禁用。前往设置 → Actions → General，确保 "Allow all actions" 已勾选。

**Q: 怎样更改版本号?**  
A: 编辑 `.github/workflows/release.yml` 中的版本，或直接用不同的标签号 `git tag v1.1.0` 等。

---

需要帮助吗？提交 Issue 到仓库的 Issues 页面。
