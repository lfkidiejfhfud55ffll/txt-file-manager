# 发布清单（复制粘贴用）

## 一、建仓库时填的东西

**Repository name**

```
txt-file-manager
```

**Description**（GitHub 仓库页右上 About）

```
全盘检索本机所有 .txt 文件，查看大小/时间/编码等详情并就地编辑。Windows 原生单文件 exe，不复制、不联网、不写注册表。
```

**Topics**（Settings → Topics，逗号分隔）

```
windows txt winforms csharp dotnet-framework file-manager desktop-app text-editor no-install single-exe
```

**初始化选项**：全部不勾（README / .gitignore / LICENSE 本仓库已有）。

## 二、推送

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\publish.ps1 -RepoUrl https://github.com/你的名字/txt-file-manager.git
```

如果没装 Git，也可以走网页版：在仓库页 `Add file → Upload files`，把整个目录（**不要**带 `dist/`）拖进去，提交信息写 `chore: 首次提交 TXT 文件管理器 v1.0.0`。

## 三、发第一个 Release

打 tag 后 CI 会自动编译并附上 exe（`.github/workflows/build.yml`）：

```powershell
git tag v1.0.0
git push origin v1.0.0
```

Release 标题与说明可直接用：

**Title**

```
v1.0.0 — 首个版本
```

**Description**

```markdown
第一个可用版本。

### 功能
- 全盘扫描所有固定磁盘的 `.txt` 文件（多线程增量推送，6k+ 文件约 12 秒）
- 虚拟列表：文件名 / 大小 / 修改时间 / 创建时间 / 所在目录，表头排序
- 详情面板：完整路径、精确字节数、编码、创建/修改/访问时间、行数、属性、磁盘剩余空间
- 内容预览（前 256 KB）
- 就地编辑：保持原编码（UTF-8 / BOM / GB18030 / UTF-16 LE）与原换行符（CRLF / LF）
- 搜索框实时过滤，`Ctrl+F` / `F5` / `Enter` / `Ctrl+S` 快捷键

### 特性
- 单个 exe，约 350 KB，无安装、无依赖（仅需系统自带 .NET Framework 4.8）
- 不复制、不移动、不重命名用户文件；不联网、无遥测、不写注册表

### 已知限制
- 仅索引 `.txt`
- 预览上限 256 KB，>4 MB 的文件编辑前会二次确认
- 仅 Windows
```

## 四、建议顺手开的设置

- Settings → General → 勾上 **Releases**、**Packages**（不需要可关）。
- Settings → Actions → General → Workflow permissions 选 **Read and write**（打 tag 自动发 Release 需要）。
- 仓库首页 `Edit repository details` 里贴好 About 与 Website（可留空）。
