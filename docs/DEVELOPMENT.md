# 开发与验证

## 工具

Windows x64、JDK 8、Maven 3.9+、.NET Framework 4.7.2+。C# 使用系统 Framework64 下的 `csc.exe`；桌面 SDK DLL 已随源码提供，其许可证一同保留。Java 构建依赖通过 Maven Central 下载。

```powershell
.\scripts\Build-Release.ps1 -Version 1.1.5
```

仅构建后台：`mvn -f backend/pom.xml package`。仅编译桌面客户端：`desktop/build-desktop.ps1 -OutputPath <输出exe绝对路径>`。桌面客户端的资源脚本在编译时嵌入 EXE，修改 JS 后需要重新编译。

## 安装布局

Release 中的启动器 `Qiye.exe` 负责首次组件准备，再运行 `desktop/StudyDesktop.exe`。桌面客户端首次检查本机服务；缺少服务时调用根目录 `start-local.ps1`。

`Initialize-Environment.ps1` 校验并解压官方运行时，生成随机本机数据库密码、空数据库及端口配置。`Start-Services.ps1` 以当前用户启动独立进程，`Stop-Services.ps1` 仅匹配本安装路径和 PID 后停止。

`data/service-endpoint.json` 保存本机端口与安装实例 UUID；`/health` 返回相同实例标识。Native API 地址和题库地址均取自这个文件，不固定到某个开发者的目录或端口。不要将运行数据提交到 Git。

## 测试

后台 `mvn -f backend/pom.xml test` 为本地模拟测试，不要求真实学习通账号。构建默认执行测试，失败会阻止打包。

`LocalQuestionBankTest` 使用合成 JSON 验证同题同型和完整选项匹配、选项重排、否定词/运算符保护、判断题、无效条目跳过、冲突留空、缓存更新及本地/在线查题分流。外部课程题目数据不作为公开测试夹具；本地库格式见 [本地题库说明](LOCAL-QUESTION-BANK.md)。

`scripts/Test-Homework.ps1` 在独立测试浏览器配置中运行合成作业，验证官方课程主页导航、延迟绑定的菜单、带数量的菜单、整张卡片点击、多层 iframe、异步空列表、登录/访问错误，以及用户主动查题、选择/填空、未命中留空、原答案保护、停止与导航取消、不自动提交。需要已安装 WebView2，结果写入本机 temp，测试不访问真实作业或外部题库。

`scripts/Test-Release.ps1 -Files <便携zip>,<源码zip>` 检查归档是否含运行数据、凭据和越界路径。构建脚本自动调用它。公开源码包使用明确的目录和文件清单生成，避免复制整个开发工作目录。

发布前应从安装程序提取到一个未使用过的目录，完整验证组件下载/校验、数据库初始化、服务启动、空账号状态、客户端打开、服务关闭和再次启动。可用 `-ArchiveCache <目录>` 给 `start-local.ps1` 指定离线测试缓存，缓存文件名及散列必须与 runtime-lock.json 一致。`-SkipSystemComponents` 仅供已具备 Microsoft 运行组件的测试机使用，正常用户无需这些参数。

播放器改动使用本地视频、模拟页面和模拟测验验证手动暂停、多视频、导航失败等情况。不要把真实课程账号或真实测验作为公开测试夹具。

## 配置

可编辑配置模板 `config/application-local.example.yml`。首次安装生成的 `application-local.yml` 保留在本机，后续启动不覆盖手工修改。默认只监听 127.0.0.1，关闭 Nacos/Dubbo 外部注册及 RabbitMQ 自动配置。

保持 `study.chaoxing.speed: 1`；不要新增倍速按钮、修改播放速率或开发强制加速功能，开启倍速会导致学习通显示“数据异常”。详细配置见 [配置说明](CONFIGURATION.md)，功能使用限制见 [注意事项](NOTES.md)。

更新程序文件时保留使用者的 `data/` 和 `config/`，先停止旧程序及服务。不得用开发者的运行目录充当发布源。
