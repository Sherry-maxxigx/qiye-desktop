# 第三方组件

| 组件 | 用途 | 许可与来源 |
| --- | --- | --- |
| SuperAutoStudy | 上游 Java 项目 | Apache 2.0；[原仓库](https://github.com/DuanInnovator/SuperAutoStudy)，许可见根目录 LICENSE，原始说明保存在 docs/UPSTREAM-README.md |
| Microsoft WebView2 SDK 1.0.2903.40 | 官方网页内嵌 | Microsoft 软件许可，见 desktop/WebView2-LICENSE.txt、third-party/WebView2-NOTICE.txt；发布包包含 SDK 的三个可再发行 DLL |
| Spring Boot 等 Java 依赖 | 本机后台 | 版本见 backend/pom.xml；各依赖 JAR 的 META-INF 中保留其许可证及声明，随后台包分发 |
| Eclipse Temurin JDK 8u504-b01 | Java 运行时 | GPLv2 with Classpath Exception；首次安装从 [Adoptium 官方发布](https://github.com/adoptium/temurin8-binaries/releases/tag/jdk8u504-b01)下载；LICENSE、NOTICE、THIRD_PARTY_README 与 src.zip 保留在安装目录 tools/jdk8u504-b01 |
| MySQL Community 8.0.44 | 独立本地数据库 | GPLv2；首次安装从 [MySQL 官方站点](https://dev.mysql.com/downloads/mysql/)下载；完整 LICENSE、README 保留在 tools/mysql-8.0.44-winx64；[对应版本源码下载](https://downloads.mysql.com/archives/community/) |
| Edge WebView2 Runtime / Visual C++ Runtime | Windows 运行组件 | 从 Microsoft 官方链接下载并验证 Microsoft 数字签名，按各自安装程序的条款安装 |

发布的安装程序和便携压缩包不捆绑 Java/MySQL 运行时压缩包；首次运行由使用者直接从官方来源取得，固定版本与 SHA-256 见 scripts/runtime-lock.json。

题库接口为独立第三方网络服务。此项目包含接口适配代码，不包含他人的题库数据库，也不承诺接口持续可用。cxmooc-tools 的研究副本未纳入本项目的源码或二进制发布包。
