# CLAUDE.md — mech-pilot-sw-mcp 项目级 AI 助手指引

> **新对话窗口必读** —— 30 秒了解项目 + 下一步该做什么。
> 完整架构看 [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)，SW API 知识看
> [`docs/SW_API_REFERENCE.md`](docs/SW_API_REFERENCE.md)，
> 老 v1 经验看 [`docs/v1-history.md`](docs/v1-history.md)。

## 这是什么

**mech-pilot-sw-mcp** = SolidWorks MCP server + CLI。让 LLM Agent (Claude Code / Cursor / 任何 MCP
客户端) 通过自然语言操作 SolidWorks 完成参数化建模。

```
用户自然语言 → Claude Code → MCP stdio (JSON-RPC) → mech-pilot-sw → SW.Interop → SolidWorks
                                                        ↑
                                       人也可直接 CLI: mech-pilot-sw create-flange ...
```

**新架构核心赌注** (vs 老 mech-pilot v1)：
- Claude Code stock 当 agent，不再自研 agent 框架
- C# .NET 8 + 官方 SW.Interop DLL，告别 pywin32 late binding 痛苦
- MCP 协议解耦，agent 与 SW 完全独立

---

## 当前阶段

🚧 **MVP 开发中**：`create_cylinder` + `create_flange` 2 个工具。

详见 [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) "MVP 三阶段里程碑"。

---

## 核心约束（必读）

| 项 | 值 | 备注 |
|---|---|---|
| OS | **Windows 原生** | SW COM API 必须本机；不能 WSL |
| .NET | **8.0 (target `net8.0-windows`)** | 机器装的 9 SDK + 8 runtime 可跑 |
| SW 版本 | **SolidWorks 2026 SP02.1** | API 签名比文档漂移 (详 SW_API_REFERENCE) |
| SW UI 语言 | **中文简体** | "前视基准面" 等；代码处理中英文别名 |
| SW Interop DLL | **官方预编译** `G:/solidwork/SOLIDWORKS Corp2026/SOLIDWORKS/api/redist/*.dll` | 直接 `Reference + HintPath`，比 COMReference 稳 |
| Agent | **Claude Code stock** | 不改 (借 Anthropic 全部迭代) |
| MCP server NuGet | **`ModelContextProtocol`** | 官方实现 |
| CLI 入口 | **多入口设计**: MCP server (默认) + CLI 子命令 (debug/CI) | 业务逻辑放 `Tools/` 共用 |
| 主分支名 | `master` | branch protection 开启，**必须走 PR** |

---

## 工作流（每次开发都遵循）

### 1. 拉新分支

```powershell
git checkout master
git pull origin master
git checkout -b feat/<scope>-<short-name>
# 例: feat/cylinder-tool / fix/flange-validation
```

### 2. 实现 + 测试

按 ARCHITECTURE.md "测试方案" 的 4 层金字塔：

| Layer | 测什么 | 跑法 |
|---|---|---|
| **L1** 单元 | C# spec 校验 / helper 纯函数 | `dotnet test` (CI 必跑) |
| **L2** CLI 集成 | CLI → Tools → SW.Interop | PowerShell 脚本 `tests/integration/*.test.ps1` |
| **L3** MCP 协议 | MCP server stdio | 重启 Claude session 后跑 `mcp__mech_pilot_sw__*` |
| **L4** E2E 体验 | LLM 决策全链路 | 你说"画 D80 法兰" 看 LLM 行为 |

**主战场**：L1 + L2。改 SW Interop bug 在 L2 验证（不烧 LLM token）。

### 3. 提 PR

```powershell
git push -u origin feat/<name>
gh pr create --base master --title "feat(<scope>): <subject>" --body "..."
```

**PR 必须**：
- ✅ CI 通过 (dotnet build + dotnet test + dotnet format)
- ✅ 至少 1 个 L2 集成测试（如果改了 Tool 业务逻辑）
- ✅ 简明 description (改了什么 / 为什么 / 怎么测)

### 4. 合并

CI 通过 + (本人 / 协作者) review → merge。**不能直接 push master**。

---

## 项目级 "黄金法则"

1. **业务核心 vs 入口分离**：所有 SW 业务逻辑放 `Tools/`，MCP 入口和 CLI 入口都调
   同一个 Tool 类的同一个方法。**写一份，多处用**。

2. **多入口都要验证**：L2 集成测试 (CLI) + L3/L4 MCP 抽测，不能只跑一个。
   两个入口行为分歧 = 业务核心放错位置。

3. **错误处理统一走 `McpToolException`**：业务核心 throw 这个异常，CLI 模式打印
   `[error] xxx` + 退出码 ≠ 0，MCP 模式自动转 `isError=true`。

4. **早期绑定 (Early Binding) 优先**：用 `SolidWorks.Interop.sldworks` 程序集
   直接 reference (`EmbedInteropTypes=true`)，享受 IntelliSense + 类型检查 + 枚举常量。
   避免重蹈 v1 late binding 覆辙 (详 `docs/v1-history.md` PR #35)。

5. **录的宏 ≠ reliable code**：SW 录的 .swp 宏只是参考起点，复杂特征 (pattern / mirror /
   hole_wizard) 单独跑很可能 silent fail (v1 经验)。**先 VBA IDE 里手动跑通宏**再
   1:1 复刻到 C#。

6. **绕过限制思维**：某个 SW API 多 stage 探针仍 silent fail (如 v1 PR #35
   pattern_circular)，**绕过比硬刚强**：加一个粗粒度一键工具
   (如 `create_flange` 一次画所有孔 + 一次 cut，根本不调 pattern)。

7. **commit message 用 conventional commits**：
   - `feat(scope): subject` — 新功能
   - `fix(scope): subject` — bug 修复
   - `docs:` / `chore:` / `test:` / `refactor:`

---

## 关键命令速查

```powershell
# Build
cd MechPilot.SwMcp
dotnet build

# 单元测试 (L1)
dotnet test

# CLI 直接调用 (L2, 不依赖 LLM)
.\bin\Debug\net8.0-windows\mech-pilot-sw.exe ping
.\bin\Debug\net8.0-windows\mech-pilot-sw.exe create-cylinder --diameter 30 --length 50 --out C:/tmp/cyl.sldprt
.\bin\Debug\net8.0-windows\mech-pilot-sw.exe --help

# 跑 L2 集成测试套件
.\tests\integration\run-all.ps1

# Lint / format check
dotnet format --verify-no-changes

# 通过 Claude Code 验证 MCP (重启对话窗后)
# 你跟我说: "画一个 D80 的法兰，中心 D30 + 4 个 M6 在 PCD 55"
# 我自动调 mcp__mech_pilot_sw__create_flange
```

---

## 进入新对话先做这 3 件事

1. **读 [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)** → 了解架构 + 当前 MVP 阶段
2. **跑 `gh pr list --state open`** → 看是否有未合 PR 影响本次开发
3. **跑 `git log --oneline -5`** → 看最近 commits 状态

---

## 目录结构

```
mech-pilot-sw-mcp/                  ← 项目根 (= GitHub repo)
├── README.md                       # 对外说明
├── CLAUDE.md                       # 本文件 (AI 助手项目级指引)
├── .gitignore                      # C# .NET + IDE + Windows
├── .github/
│   └── workflows/
│       └── ci.yml                  # GitHub Actions (dotnet build/test/format)
├── MechPilot.SwMcp/                # C# 主项目
│   ├── MechPilot.SwMcp.csproj
│   ├── Program.cs                  # 入口分发
│   ├── Entrypoints/
│   │   ├── McpServer.cs            # MCP stdio 入口
│   │   └── CliRunner.cs            # CLI 子命令入口
│   ├── Tools/                      # 业务核心 (所有入口共用)
│   │   ├── PingTool.cs
│   │   ├── CreateCylinderTool.cs   # MVP M2
│   │   └── CreateFlangeTool.cs     # MVP M3
│   ├── Models/                     # POCO 数据类型
│   │   ├── CylinderSpec.cs
│   │   ├── FlangeSpec.cs
│   │   └── ToolResult.cs
│   ├── Interop/
│   │   ├── SwConnection.cs         # ISldWorks 单例 + retry
│   │   └── SketchHelpers.cs        # 草图通用 helper
│   └── Exceptions/
│       └── McpToolException.cs     # 业务异常 → MCP isError / CLI [error]
├── MechPilot.SwMcp.Tests/          # L1 xUnit 单元测试
│   └── ...
├── tests/
│   └── integration/                # L2 CLI 集成测试 (PowerShell)
│       ├── M1-ping.test.ps1
│       ├── M2-cylinder.test.ps1
│       ├── M3-flange.test.ps1
│       └── run-all.ps1
├── docs/
│   ├── ARCHITECTURE.md             # 架构方案 (605 行，必读)
│   ├── SW_API_REFERENCE.md         # SW API 知识库 (从 v1 迁移)
│   └── v1-history.md               # 老 mech-pilot 35 PR 教训
└── scripts/
    └── start-mcp-session.ps1       # 一键 build + 提示重启 Claude
```

---

## 联系

- 项目维护者：[@adam0wanna0top](https://github.com/adam0wanna0top)
- 老 v1 项目：[mech-pilot](https://github.com/adam0wanna0top/mech-pilot)
