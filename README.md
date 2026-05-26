# mech-pilot-sw-mcp

**对话式 SolidWorks 建模 — MCP server (C# .NET 8) + 多入口（MCP / CLI）。**

让 LLM Agent (Claude Code / Cursor / 任何 MCP 客户端) 通过自然语言操作 SolidWorks 完成参数化建模任务。

```
用户自然语言 → Claude Code → MCP stdio → mech-pilot-sw → SW.Interop → SolidWorks
```

---

## 为什么有这个项目

老 [mech-pilot](https://github.com/adam0wanna0top/mech-pilot) (Python + pywin32 + Pydantic AI, 35 PR) 在 SW 自动化领域已积累 28 个 LLM 工具 + 完整知识库，但暴露 3 个根本性技术债：

1. **pywin32 late binding 痛苦**：SW IDispatch 不暴露 TypeInfo，IDispatch 类型 setter silent ignored
   ([PR #35 12 stage 全 fail 证据链](https://github.com/adam0wanna0top/mech-pilot/pull/35))。
   SW 官方推荐 .NET / VBA / C++，几乎无 Python 示例。
2. **agent 框架同质化**：自研 Pydantic AI 不及 Claude Code 成熟 (后者有 Bash/File/Glob
   通用工具 + 完善 MCP 客户端 + 上下文管理)。
3. **跨工具复用难**：mech-pilot 工具绑死自家 agent；MCP 协议化后可被任何 MCP 客户端使用。

**新架构核心赌注**：
- ✅ **Claude Code 当通用 agent core**（stock 不改，借 Anthropic 全部迭代）
- ✅ **C# .NET 8 MCP server 做 SW 适配层**（用 SW 文档最丰富的语言 + early binding）
- ✅ **MCP stdio 协议解耦**（agent 与 SW 完全独立，将来换 agent 容易）

---

## 项目状态

🚧 **MVP 开发中** — 目标：`create_cylinder` + `create_flange` 2 个工具跑通 MCP + CLI 双入口。

详见 [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)。

---

## 技术栈

| 层 | 技术 |
|---|---|
| Agent core | [Claude Code](https://github.com/anthropics/claude-code) (stock) |
| 通信协议 | [MCP](https://modelcontextprotocol.io) stdio (JSON-RPC) |
| MCP server | C# .NET 8 + `ModelContextProtocol` NuGet |
| SW Interop | `SolidWorks.Interop.sldworks` (官方预编译 .NET DLL, early binding) |
| CLI parsing | `System.CommandLine` |
| 测试 | xUnit (单元) + PowerShell (CLI 集成) |
| SW 版本 | SOLIDWORKS 2026 SP02.1 (Windows only) |

---

## 快速开始

### 前置条件

- Windows 10/11
- .NET 8 SDK (或 9 SDK 兼容)
- SolidWorks 2026 已安装且能跑
- Claude Code (npm 官方版 / leaked source build / 或任何 MCP 客户端)

### 1. Clone + Build

```powershell
git clone https://github.com/adam0wanna0top/mech-pilot-sw-mcp.git
cd mech-pilot-sw-mcp/MechPilot.SwMcp
dotnet build
```

### 2. 配 MCP server

编辑 `~/.claude/mcp.json` 加：

```json
{
  "mcpServers": {
    "mech_pilot_sw": {
      "type": "stdio",
      "command": "C:/path/to/mech-pilot-sw-mcp/MechPilot.SwMcp/bin/Debug/net8.0-windows/mech-pilot-sw.exe"
    }
  }
}
```

### 3. 直接 CLI 调用（不需要 LLM）

```powershell
mech-pilot-sw create-flange `
    --outer 80 --thickness 10 --center-hole 30 `
    --bolt-count 4 --bolt-d 6 --pcd 55 `
    --out C:/tmp/flange.sldprt
✅ Created flange: C:/tmp/flange.sldprt
```

### 4. 通过 Claude Code 自然语言

```
$ claude
> 帮我画一个外径 80 厚度 10 的法兰，中心 30mm 通孔 + 4 个 M6 均布在 PCD 55
🤖 已生成法兰：C:/tmp/flange.sldprt
```

---

## 开发约定

| 项 | 约定 |
|---|---|
| 主分支 | `master` (branch protection: 禁直接 push) |
| 工作流 | feature branch → PR → CI 通过 → review → merge |
| Commit message | conventional commits (`feat:` / `fix:` / `docs:` / `chore:`) |
| C# 代码风格 | `dotnet format` (CI 强制) |
| 测试 | L1 单元 + L2 CLI 集成必须过 (详 `docs/ARCHITECTURE.md` §测试方案) |

详见 [`CLAUDE.md`](CLAUDE.md) 项目级开发指引。

---

## 文档

- 📐 [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — 架构方案 / 决策 / MVP 里程碑 / 风险
- 📚 [`docs/SW_API_REFERENCE.md`](docs/SW_API_REFERENCE.md) — SW API 知识库（从 v1 迁移）
- 📖 [`docs/v1-history.md`](docs/v1-history.md) — 老 mech-pilot 35 PR 经验教训

---

## 兼容客户端

任何支持 MCP stdio 的客户端：

- [Claude Code](https://github.com/anthropics/claude-code) ✅ 主要测试目标
- [Cursor](https://cursor.com) ✅ MCP 支持
- [Cline](https://github.com/cline/cline) ✅
- [Anthropic Claude Desktop](https://claude.ai/download) ✅
- 自研 MCP 客户端 ✅

---

## License

待定（项目早期，暂未选定）

---

## 关联项目

- [mech-pilot (v1)](https://github.com/adam0wanna0top/mech-pilot) — Python + pywin32 老版本，留作知识参考
