# 发布到 GitHub

本目录已经按公开仓库布局整理；无需上传开发者原来的整个运行目录。

## 源码放仓库，App 放 Releases

仓库根目录应该直接看到 `README.md`、`LICENSE`、`NOTICE`、`desktop/`、`backend/`、`scripts/`、`config/`、`docs/` 和 `.github/`。不要在仓库里只放一个源码 ZIP，也不要把所有内容额外套在一层同名文件夹下面。

安装程序、便携 ZIP 和校验文件作为 Release 附件上传，其他人才可以直接下载 App；仅上传源码不会自动出现可用安装程序。发布包不含个人登录、数据库或个人合并题库。

系统要求、使用方式和注意事项已经写入 [README](../README.md)。发布说明也应保留这一条：**始终使用 1 倍速，不要自行增加或开启倍速功能，否则学习通会显示“数据异常”。**

## 首次上传源码

1. 在自己的 GitHub 账号下创建仓库，例如 `qiye-desktop`。
2. 将此源码目录作为仓库根目录，保留 LICENSE、NOTICE、第三方声明和 `.gitignore`。
3. 上传前确认 `git status` 中不包含 `data/`、真实配置、浏览器目录、数据库、日志、运行工具或缓存。

如果使用命令行，先在 GitHub 创建空仓库，再按其页面给出的真实远程地址执行：

```powershell
git init
git add .
git status
git commit -m "Initial open source release"
git branch -M main
git remote add origin <你创建的仓库HTTPS地址>
git push -u origin main
```

这里的远程地址需要替换成自己的真实仓库地址。不要沿用上游仓库地址作为你的发布目标。

建议创建空仓库时先不要让 GitHub 另外生成 README 或 LICENSE，直接使用项目已有文件。已有远程地址时先查看 `git remote -v`，不要重复执行 `git remote add origin`。

使用 GitHub 网页上传时，选择“Add file → Upload files”，将整理好的源码文件夹**里面的内容**上传至仓库根目录；文件较多时分批完成。注意保留 `.github/`、`.gitignore`、`.gitattributes`、`.editorconfig` 这些隐藏文件。上传完成后检查首页能否显示 README 和图片、Actions 是否能识别工作流。使用 Git 或 GitHub Desktop 通常更容易保持完整目录结构。

## 上传可下载 App

方式一：本地运行 `scripts/Build-Release.ps1 -Version 1.1.5`。在 GitHub 的 Releases 中创建 `v1.1.5`，附加 `dist/` 下以下四个文件：

- `Qiye-Setup-1.1.5-win-x64.exe`
- `Qiye-1.1.5-win-x64.zip`
- `Qiye-1.1.5-source.zip`
- `SHA256SUMS.txt`

本地已经构建好发布包时，可直接在网页选择“Releases → Draft a new release”，设置 `v1.1.5` 标签和标题，把上述四个文件作为附件上传，再发布。若同时使用标签触发的 Actions 自动发布，等待同一标签的构建结果，避免手工附件与工作流产物混用。

不要上传 `dist/stage-*`、`payload-*.zip` 或自己的运行目录。Release 说明应写明：Windows x64，首次需联网下载运行组件，安装包未签名，以及本版本具体改动和验证范围。

方式二：启用仓库 Actions，推送版本标签后自动构建并发布：

```powershell
git tag v1.1.5
git push origin v1.1.5
```

工作流会执行后台测试、编译和归档检查。也可在 Actions 中手动运行，只下载构建产物，不创建 Release。

## 上传前核对

- 保留上游 Apache 2.0 许可、NOTICE 和第三方声明，使用自己的仓库地址。
- 保留 1 倍速要求；不要把上游历史 README 中的旧功能宣传当作当前桌面版说明。
- 不包含 `data/`、`research/`、真实配置、浏览器登录目录、数据库、备份、运行组件或日志。
- Release 上传公开构建产物，不上传 `dist/stage-*`、`payload-*.zip` 或运行过的安装目录。
- 需要复核归档时，运行 `scripts/Test-Release.ps1 -Files <便携包路径>,<源码包路径>`，并核对 `SHA256SUMS.txt`。

源码首次上传和正式发布由仓库拥有者操作；本地准备发布包并不表示已经上传到 GitHub。
