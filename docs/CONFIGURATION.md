# 配置要求与配置文件

## 普通用户需要准备什么

- Windows 10 / 11，x64；建议至少 4 GB 内存、3 GB 可用磁盘空间。
- .NET Framework 4.7.2+、Windows PowerShell 5.1。
- Microsoft Edge WebView2 Runtime 和 Visual C++ x64 Runtime；缺少时安装流程会尝试准备官方组件。
- 可写的独立英文安装路径，例如 `E:\Qiye`；支持空格，不支持中文路径。
- 网络能够访问学习通，以及首次准备时使用的 GitHub / Adoptium、MySQL 和 Microsoft 官方下载地址。

首次安装自动下载 Java 8 和 MySQL 8.0.44，校验后初始化本地数据库，不要求普通用户手动安装开发工具或部署 Nacos、Dubbo、RabbitMQ 等远程服务。固定版本、来源和散列在 `scripts/runtime-lock.json`。

## 哪份文件可以改

源码中的 `config/application-local.example.yml` 是公开模板，包含 `__API_PORT__` 等占位符，不能直接作为已完成的个人配置使用。首次安装会生成真正的 `config/application-local.yml`，填入本机端口、随机数据库密码和安装标识。

修改步骤：

1. 关闭 App，运行安装目录的 `Stop-Services.cmd`。
2. 在本机备份 `config/application-local.yml`。
3. 只修改需要的字段，保持 YAML 缩进；已有的 `tiku`、`settings` 等节点不要重复添加。
4. 从安装目录的 `Qiye.exe` 重新启动。

不要上传真实配置。日常使用一般无需改端口、数据库连接、密码、实例标识或服务监听地址。

## 播放配置：必须为 1 倍速

```yaml
study:
  chaoxing:
    speed: 1
```

**一定不要自行添加、修改或开启倍速功能。开启倍速会导致学习通显示“数据异常”，必须保持 `speed: 1`。** 也不要修改网页视频速率，或使用外部脚本、插件强制加速。此字段不是供使用者尝试加速的选项。

## 在线及本地题库

默认配置如下，修改时应编辑原有节点：

```yaml
tiku:
  settings:
    provider: cxmooc
    local-bank-file: ''
    cxmooc-url: https://cx.icodef.com/v2/answer?platform=cx
    buguake-enabled: true
    buguake-url: https://easylearn.baidu.com/edu-web-go/bgk/searchlist
```

| 字段 | 用途 |
| --- | --- |
| `provider` | 主在线查询来源，默认 `cxmooc` |
| `local-bank-file` | 自备本地 JSON 的绝对路径，默认空字符串，表示关闭本地库 |
| `cxmooc-url` | cxmooc 查询接口地址；网页主页地址不能替代接口地址 |
| `cxmooc-token` | 可选的个人服务令牌，默认空；若服务要求令牌，按该服务说明填写，仅保存在本机 |
| `buguake-enabled` | 主查询失败或没有可匹配答案时补查；支持单选、多选和判断 |
| `buguake-url` | 不挂科补查接口地址 |

查询先尝试已配置的本地库；未命中后查询主在线来源，再按支持的题型补查。不保证第三方服务持续可用或答案正确。

本地库示例：

```yaml
tiku:
  settings:
    local-bank-file: 'E:/Qiye/data/question-banks/custom.json'
```

将路径换成自己文件的位置，其他题库设置保留。一个文件可包含整理合并后的多份资料，程序不会自动扫描文件夹内的其他文件。格式、大小限制及匹配规则见 [本地题库说明](LOCAL-QUESTION-BANK.md)。公开包不附带个人合并题库，需要使用者自备文件。

## 本机服务与开发环境

后台仅监听 `127.0.0.1`。首次安装自动在本机选择可用端口，默认候选为后台 `3030–3099`、数据库 `3307–3399`。客户端从安装目录的 `data/service-endpoint.json` 读取实际端口，并核对安装标识。不要硬编码连接另一份安装的端口。

从源码构建额外需要 JDK 8、Maven 3.9+ 和系统 .NET Framework C# 编译器；Java/Maven 路径可通过构建脚本参数指定。详见 [开发说明](DEVELOPMENT.md)。
