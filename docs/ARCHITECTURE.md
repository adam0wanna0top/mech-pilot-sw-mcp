# mech-pilot-v2 架构重构方案

## 环境 probe 结果 (✅ 全部就绪)

| 检查项 | 结果 | 备注 |
|---|---|---|
| .NET SDK | ✅ 9.0.308 | 能跑 .NET 8 项目 (multi-targeting) |
| .NET 8 / 9 runtime | ✅ 都装 | `Microsoft.NETCore.App` + `WindowsDesktop.App` |
| Visual Studio 2022 | ✅ 装 | IDE / 调试器 |
| SW 2026 安装目录 | ✅ `G:/solidwork/SOLIDWORKS Corp2026/SOLIDWORKS/` | |
| SW Type Library `.tlb` | ✅ `.../SOLIDWORKS/sldworks.tlb` | |
| **SW 官方预编译 .NET Interop DLL** | ✅ `.../api/redist/SolidWorks.Interop.*.dll` | 核心: `sldworks.dll`, `swconst.dll`, `swcommands.dll`, `swdocumentmgr.dll` |
| SW COM ProgID `SldWorks.Application` | ✅ 注册表已注册 | |
| 当前 Claude Code session MCP 客户端 | ✅ 已挂 7+ MCP servers (`ccd_session` / `scheduled-tasks` / `Claude_in_Chrome` / ...) | 直接验证 mech-pilot-sw 用 |

---

## Context

老 mech-pilot (Python + pywin32 + Pydantic AI) 经过 35 个 PR 积累了 28 个 LLM 工具
+ 完整 SW 知识库 (`docs/SW_API_REFERENCE.md`)，但暴露了 3 个根本性技术债：

1. **pywin32 late binding 痛苦**：SW IDispatch 不暴露 TypeInfo，所有 setter 走运行时
   推断 → IDispatch 类型 setter (如 `fdef.Axis = obj`) silent ignored (PR #35 12 stage
   全 fail 证据链)。SW 官方推荐 .NET / VBA / C++，几乎无 Python 示例。
2. **agent 框架同质化**：自研 Pydantic AI 整合不及 Claude Code 成熟 (后者有 Bash/File/Glob
   通用工具 + MCP 客户端 + 完善的 UI / 上下文管理)。
3. **跨工具复用难**：mech-pilot 工具粒度好但绑死自家 agent；MCP 协议化后可被任何
   MCP 兼容客户端 (Cursor / Cline / Claude Desktop) 使用。

**新架构核心赌注**：

- **Claude Code 当通用 agent core**（stock 不改，借 Anthropic 全部迭代）
- **C# .NET 8 MCP server 做 SW 适配层**（用 SW 文档最丰富的语言 + early binding）
- **MCP stdio 协议解耦**（agent 与 SW 完全独立，将来换 agent 容易）

---

## 架构图

```
╔════════════════════════════════════════════════════════╗
║  阶段一 (MVP，本次工程目标)                              ║
║                                                         ║
║  人类用户                                                ║
║  • 通过 Claude Code 对话窗用自然语言                    ║
║  • 也可直接 CLI 命令: mech-pilot-sw create-flange ...   ║
║  └─┬───────────────────────────────┬───────────────────╜
│    │ 自然语言对话                  │ 直接 CLI 命令
│    ↓                               ↓
│  ┌─────────────────────────────┐  ┌─────────────────┐
│  │  Claude Code = 当前 session │  │ 直接调 CLI 入口 │
│  │  (本对话的 AI 助手即是)     │  │ (人 debug / CI) │
│  │  • 已挂多个 MCP server      │  └────────┬────────┘
│  │  • 读 ~/.claude/mcp.json    │           │
│  │  • LLM 决定调哪个工具       │           │
│  └──────────────┬──────────────┘           │
│                 ↓ JSON-RPC stdio           │
│                 │ (MCP protocol)           │
│                 ↓                          ↓
│  ┌─────────────────────────────────────────────────────┐
│  │  mech-pilot-sw.exe (C# .NET 8 console, 多入口)      │
│  │  ──────────────────────────────────────────────     │
│  │  Entrypoints/ (入口分发，根据 args 决定模式)        │
│  │    ├─ McpServer    ← 默认 (无 args)，Claude 用      │
│  │    ├─ CliRunner    ← 子命令模式 (人 / CI 用)        │
│  │    └─ ReplRunner   ← 交互 REPL (M4+ 加，可选)       │
│  │  ──────────────────────────────────────────────     │
│  │  Tools/ ← 业务核心 (3 入口共用同一份代码)           │
│  │    • CreateCylinderTool / CreateFlangeTool / ...    │
│  │  Interop/ ← SolidWorks.Interop.sldworks (早期绑定)   │
│  └─────────────────┬───────────────────────────────────┘
│                    ↓ COM Interop (.NET RCW)
│  ┌─────────────────────────────────────────────────────┐
│  │  SolidWorks 2026 SP02.1 (sldworks.exe)              │
│  └─────────────────────────────────────────────────────┘
╚═════════════════════════════════════════════════════════╝

╔═════════════════════════════════════════════════════════╗
║  阶段二 (MVP 后评估，可选)                                ║
║                                                          ║
║  切换到独立 Claude Code 实例 (脱离当前对话)              ║
║  ├─ A: npm install -g @anthropic-ai/claude-code (官方)   ║
║  └─ B: build leaked source (旧几月，需自行 build)        ║
║                ↓ 同样 MCP stdio                          ║
║  mech-pilot-sw.exe (零改动)                              ║
║                                                          ║
║  动机：定制 system prompt / 删 Bash/File 通用工具 /      ║
║        UI 词汇 SW 化 / 长期做 SW 专用 fork               ║
╚══════════════════════════════════════════════════════════╝
```

**关键设计：单二进制多入口 (composable CLI / multi-frontend)**：
- `mech-pilot-sw.exe`          → MCP server (stdio, Claude Code 默认 spawn)
- `mech-pilot-sw.exe create-flange --outer 80 --pcd 55 --out C:/tmp/x.sldprt` → CLI 单次命令
- `mech-pilot-sw.exe list-tools` → 列工具
- `mech-pilot-sw.exe --help`     → 帮助

3 个入口共用 `Tools/` 业务核心，写一份多个 frontend 用 (UNIX 哲学)。

**关键洞察：当前对话的 Claude Code session 就是 MCP client**。
工具列表里 `mcp__ccd_session__mark_chapter` / `mcp__scheduled-tasks__create_scheduled_task`
等说明本 session 已成功挂 7+ 个 MCP server，证明架构通。MVP 阶段直接复用，
不必 build leaked source。

---

## 13 个核心决策汇总

| # | 决策点 | 选择 |
|---|---|---|
| 1 | Agent core | Claude Code stock 不改 + MCP server 暴露 SW 能力 |
| 2 | MCP server 语言 | C# .NET 8 (target `net8.0-windows`) |
| 3 | 通信协议 | MCP stdio |
| 4 | MVP 范围 | `create_cylinder` + `create_flange` 2 个工具 |
| 5 | 老 mech-pilot 代码 | 不动 (`C:\Users\Hello\ws`)，新 repo 独立 |
| 6 | **Claude Code 来源** | **MVP: 复用当前 Claude Code session (即与你对话的我)，不 build 任何 leaked source**。阶段二可选切换 (见 #13) |
| 7 | 项目位置 | `C:\Users\Hello\mech-pilot-v2\` |
| 8 | MVP 后路线 | 暂停验证效果再决定下一批迁移 |
| 9 | MCP 配置位置 | `~/.claude/mcp.json` (用户级) |
| 10 | 错误返回协议 | MCP `isError: true` 字符串 (标准协议，Claude Code 自动重试) |
| 11 | .NET 版本 | .NET 8 (机器已装 9 SDK + 8 runtime，target `net8.0-windows`) |
| 12 | **Server 入口形态** | **单二进制多入口** (MCP + CLI)，业务逻辑共享 `Tools/`，便于调试 / CI / 生产 fallback |
| 13 | **阶段二 Claude Code 切换** | **MVP 跑稳后再评估**。可切到 (A) npm 官方版 / (B) leaked source build。**mech-pilot-sw 零改动**，仅 mcp.json 重新指向。MCP 协议层稳定，切换风险极低 |
| 14 | **SW Interop 引用方式** | **直接 reference 官方预编译 DLL** (`G:/solidwork/SOLIDWORKS Corp2026/SOLIDWORKS/api/redist/SolidWorks.Interop.sldworks.dll` + `swconst.dll`)，比 `COMReference` 更简洁稳定 |
| 15 | **测试策略** | **L1 单元 + L2 CLI 集成为主**，L3/L4 MCP 端到端按需。详见 "测试方案" 章节 |

---

## MVP 三阶段里程碑

**核心策略 (利用多入口设计降风险)**：
- 每个 milestone 先 **CLI 模式实现 + 直接验证业务核心** (不依赖 MCP / LLM / Claude Code)
- 业务核心验证 OK 后再加 MCP 入口暴露给 Claude Code (零业务风险)
- 这样 SW Interop bug 和 MCP 协议 bug **完全分开调试**

### M1: 项目骨架 + 双入口 + ping (~0.5 天)

**目标**：搭骨架，验证 CLI 入口 + MCP 入口都能跑，不动 SW。

**步骤**：
1. ✅ 环境 probe 完成（.NET 9 SDK / 8 runtime / VS 2022 / SW 2026 都齐）
2. 创建 `MechPilot.SwMcp.csproj` (target `net8.0-windows`) +
   `ModelContextProtocol` + `System.CommandLine` NuGet
3. `Program.cs` 入口分发：args 空 → MCP server / args 非空 → CLI subcommand
4. 实现 `Tools/PingTool.cs` 1 个工具返 `"pong"`
5. **CLI 验证**：`mech-pilot-sw.exe ping` → 输出 "pong"
6. **MCP 验证**：
   - 改 `~/.claude/mcp.json` 加 `mech_pilot_sw` server
   - **用户重启当前对话窗** (Claude Code session)
   - 新 session 我自动获得 `mcp__mech_pilot_sw__ping` 工具
   - 我直接调，看返 "pong"

**验收**：CLI 命令直接返 pong + Claude Code 通过 MCP 拿到 pong (两个入口都通)。

### M2: create_cylinder (~1-2 天)

**目标**：验证 SW.Interop 写对 + 双入口都能造出圆柱。

**步骤**：
1. `Interop/SwConnection.cs` 实现 `GetOrStartApp()` (用 `Marshal.GetActiveObject` + retry)
2. `Tools/CreateCylinderTool.cs` 实现业务核心：
   - 接 `CylinderSpec(diameter_mm, length_mm, save_path)` POCO
   - 内部：`PartDoc = swApp.NewDocument(...)` → `SelectPlane("Front Plane")` → `InsertSketch` →
     `CreateCircleByRadius` → `ExitSketch` → `FeatureExtrusion3` → `SaveAs`
   - 返 `ToolResult(status, path, message)`
3. `Entrypoints/CliRunner.cs` 加 `create-cylinder` subcommand
4. `Entrypoints/McpServer.cs` 用 `[McpServerTool]` 注解暴露同一个 Tool 类
5. **M2a CLI 验证**：`mech-pilot-sw.exe create-cylinder --diameter 30 --length 50 --out C:/tmp/cyl.sldprt`
   → 文件真生成 + SW UI 打开能看到圆柱
6. **M2b MCP 验证**：dotnet build 后**重启当前对话窗**，新 session 我跑：
   `mcp__mech_pilot_sw__create_cylinder({diameter: 30, length: 50, savePath: "C:/tmp/cyl.sldprt"})`
   → 同样文件生成；或者你自然语言 "画 D30 L50 圆柱到 C:/tmp/cyl.sldprt" 让我决策

**验收**：CLI 命令 5 秒生成 → 同一个 Tool 类被 MCP 调时也生成同样文件。

### M3: create_flange (~1-2 天)

**目标**：复刻老 mech-pilot PR #35 的 `create_flange`，验证多步几何 + spec 校验 + 错误路径。

**步骤**：
1. `Tools/CreateFlangeTool.cs` 接 `FlangeSpec` (outer_d, thickness, center_hole_d, bolt_count,
   bolt_d, pcd, save_path) POCO
2. Spec 校验 (pcd < outer / pcd > center_hole)，失败 throw `McpToolException` →
   CLI 模式打印 `[error]`，MCP 模式自动转 `isError=true`
3. 内部：cylinder + 前端面 1 个 sketch 同时画 (中心孔 + N 个偏心孔，三角函数算 360°/N 位置) +
   1 次 ExtrudeCut Through-All (绕过 pattern_circular limitation)
4. **M3a CLI 验证**：
   ```
   mech-pilot-sw.exe create-flange --outer 80 --thickness 10 --center-hole 30 \
       --bolt-count 4 --bolt-d 6 --pcd 55 --out C:/tmp/flange.sldprt
   ```
   → 8 条 M6 圆周边 + 2 条 D30 中心孔边都在
5. **M3b MCP 验证**：dotnet build 后**重启当前对话窗**，你跟我说
   "画 D80 t10 法兰，中心 D30 + 4 个 M6 在 PCD55" → 我自动调
   `mcp__mech_pilot_sw__create_flange`

**验收**：CLI + MCP 两路径都生成正确法兰；错误路径 (如 pcd > outer) 在两种入口下都返清晰错误。

---

## 项目目录结构

```
C:\Users\Hello\mech-pilot-v2\
├── MechPilot.SwMcp\                       # 单一 C# 项目，多入口
│   ├── MechPilot.SwMcp.csproj
│   ├── Program.cs                          # 入口分发 (args → MCP/CLI)
│   ├── Entrypoints\                        # 不同 frontend
│   │   ├── McpServer.cs                    # MCP stdio 模式
│   │   ├── CliRunner.cs                    # CLI 子命令模式
│   │   └── ReplRunner.cs                   # (M4+ 可选) 交互 REPL
│   ├── Tools\                              # 业务核心 (所有入口共用)
│   │   ├── PingTool.cs
│   │   ├── CreateCylinderTool.cs
│   │   └── CreateFlangeTool.cs
│   ├── Models\                             # POCO 数据类型
│   │   ├── CylinderSpec.cs
│   │   ├── FlangeSpec.cs
│   │   └── ToolResult.cs
│   ├── Interop\
│   │   ├── SwConnection.cs                 # ISldWorks 单例 + retry
│   │   └── SketchHelpers.cs                # 草图通用 helper
│   └── Exceptions\
│       └── McpToolException.cs             # 业务异常 → MCP isError
├── docs\
│   ├── ARCHITECTURE.md                     # 本文件
│   ├── SW_API_REFERENCE.md                 # 老 mech-pilot 知识沉淀
│   └── v1-history.md                       # 老 35 PR 教训
├── scripts\
│   └── start.ps1                           # 一键 build + 启 claude-code
└── README.md
```

---

## 关键文件 / 模式

### `MechPilot.SwMcp.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>mech-pilot-sw</AssemblyName>  <!-- 输出文件名: mech-pilot-sw.exe -->
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="ModelContextProtocol" Version="0.x" />
    <PackageReference Include="System.CommandLine" Version="2.0.0-beta4.*" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.*" />
  </ItemGroup>

  <!-- 直接 reference SW 官方预编译 .NET Interop DLL (决策 #14) -->
  <ItemGroup>
    <Reference Include="SolidWorks.Interop.sldworks">
      <HintPath>G:\solidwork\SOLIDWORKS Corp2026\SOLIDWORKS\api\redist\SolidWorks.Interop.sldworks.dll</HintPath>
      <EmbedInteropTypes>true</EmbedInteropTypes>
      <Private>false</Private>
    </Reference>
    <Reference Include="SolidWorks.Interop.swconst">
      <HintPath>G:\solidwork\SOLIDWORKS Corp2026\SOLIDWORKS\api\redist\SolidWorks.Interop.swconst.dll</HintPath>
      <EmbedInteropTypes>true</EmbedInteropTypes>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

**说明**：
- `EmbedInteropTypes=true` → Interop 类型嵌入到我们的 assembly，不用部署 Interop DLL
- `Private=false` → build 时不复制 DLL 到 output 目录（运行时直接用 SW 安装的 DLL）
- HintPath 写绝对路径**仅本机用**；如要给别人 share，改 `$(SolidWorksDir)\api\redist\...` +
  环境变量 `SolidWorksDir`

### `Program.cs` (多入口分发)

```csharp
using MechPilot.SwMcp.Entrypoints;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // 无参数 → MCP server 模式 (Claude Code spawn 时这样)
        if (args.Length == 0)
            return await McpServer.RunAsync();

        // 有参数 → CLI 子命令模式
        return await CliRunner.RunAsync(args);
    }
}
```

### `Entrypoints/McpServer.cs`

```csharp
using ModelContextProtocol.Server;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MechPilot.SwMcp.Interop;

public static class McpServer
{
    public static async Task<int> RunAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services
            .AddSingleton<SwConnection>()  // SW 连接单例，所有 tool 共用
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();      // 自动扫 [McpServerTool] 注解方法
        await builder.Build().RunAsync();
        return 0;
    }
}
```

### `Entrypoints/CliRunner.cs`

```csharp
using System.CommandLine;
using System.Text.Json;
using MechPilot.SwMcp.Tools;
using MechPilot.SwMcp.Models;

public static class CliRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        var root = new RootCommand("mech-pilot-sw: SolidWorks MCP server + CLI");

        // ─── ping 子命令 ───
        var ping = new Command("ping", "Sanity check, returns 'pong'");
        ping.SetHandler(() => Console.WriteLine(PingTool.Run()));
        root.AddCommand(ping);

        // ─── create-cylinder 子命令 ───
        var createCyl = new Command("create-cylinder", "Create a cylindrical part");
        var diameterOpt = new Option<double>("--diameter", "Diameter (mm)") { IsRequired = true };
        var lengthOpt   = new Option<double>("--length",   "Length (mm)")   { IsRequired = true };
        var outOpt      = new Option<string>("--out",      "Output .sldprt path") { IsRequired = true };
        var fmtOpt      = new Option<string>("--output",   () => "text", "Format: text | json");
        createCyl.AddOption(diameterOpt);
        createCyl.AddOption(lengthOpt);
        createCyl.AddOption(outOpt);
        createCyl.AddOption(fmtOpt);
        createCyl.SetHandler((d, l, o, fmt) =>
        {
            // ↓ 同一个 Tool 类被 MCP server 也调
            var result = CreateCylinderTool.Run(new CylinderSpec
            {
                DiameterMm = d, LengthMm = l, SavePath = o
            });
            Console.WriteLine(fmt == "json"
                ? JsonSerializer.Serialize(result)
                : $"✅ Created cylinder: {result.Path}");
        }, diameterOpt, lengthOpt, outOpt, fmtOpt);
        root.AddCommand(createCyl);

        // ─── create-flange / list-tools / ... 类似添加 ───

        return await root.InvokeAsync(args);
    }
}
```

### `Tools/CreateCylinderTool.cs` (业务核心 — 两个入口共用)

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using MechPilot.SwMcp.Models;
using MechPilot.SwMcp.Interop;

[McpServerToolType]
public static class CreateCylinderTool
{
    [McpServerTool, Description("Create a cylindrical part and save to disk")]
    public static ToolResult Run(CylinderSpec spec)
    {
        try
        {
            var swApp = SwConnection.Instance.GetApp();
            var model = (IModelDoc2)swApp.NewDocument(/* part template */, 0, 0, 0);
            // ... select plane, sketch circle, extrude, save ...
            return new ToolResult { Status = "ok", Path = spec.SavePath };
        }
        catch (Exception ex)
        {
            throw new McpToolException($"[error] {ex.Message}");
        }
    }
}
```

### `~/.claude/mcp.json` (用户级 MCP 注册)

```json
{
  "mcpServers": {
    "mech-pilot-sw": {
      "command": "C:/Users/Hello/mech-pilot-v2/MechPilot.SwMcp/bin/Debug/net8.0-windows/mech-pilot-sw.exe"
    }
  }
}
```

---

## 调用样例对比

### 同一个 `create_flange` 业务核心，两种调用方式

**Frontend A：人直接 CLI 调用** (调试 / CI / 生产 fallback)
```powershell
PS> mech-pilot-sw create-flange `
    --outer 80 --thickness 10 --center-hole 30 `
    --bolt-count 4 --bolt-d 6 --pcd 55 `
    --out C:/tmp/flange.sldprt
✅ Created flange: C:/tmp/flange.sldprt

# JSON 输出给 CI:
PS> mech-pilot-sw create-flange ... --output json
{"status":"ok","path":"C:/tmp/flange.sldprt","details":{...}}
```

**Frontend B：通过 Claude Code 自然语言** (终端用户)
```
$ claude-code
> 帮我画一个 D80 厚度 10 的法兰，中心 30 通孔 + 4 个 M6 均布在 PCD 55

[LLM 推理 → 决定调 create_flange tool]
🤖 已生成法兰：C:/tmp/flange.sldprt
```

两者底层调的**完全是同一个** `CreateFlangeTool.Run(spec)` 函数。

---

## 测试方案 (决策 #15)

### 4 层测试金字塔

| Layer | 测什么 | 依赖 | 跑法 | 进 CI? |
|---|---|---|---|---|
| **L1** 单元 | C# spec 校验、helper 函数纯逻辑 | 无 | `dotnet test` | ✅ 是 |
| **L2** CLI 集成 | CLI 入口 → Tools → SW.Interop 端到端 | SW | `mech-pilot-sw create-flange ...` + PowerShell 检验文件 | ⚠️ 仅有 SW 的机器 |
| **L3** MCP 协议 | MCP server stdio 握手 + tools/list + tools/call | SW | 当前 session 重启后我直接调 `mcp__mech_pilot_sw__*` | 🔴 否 (人工) |
| **L4** E2E 体验 | LLM 决策 + 全链路 | SW + Claude | 你对我说"画 D80 法兰" 我决策 | 🔴 否 (人工) |

### 主推策略 (决策 #15)

**MVP 阶段 L1 + L2 为主，L3/L4 按需手动验证**：

- **L1 单元测试** (`MechPilot.SwMcp.Tests/` 项目，xUnit)：
  - 所有 spec POCO 的校验逻辑 (e.g. `FlangeSpecValidation`)
  - 几何 helper 纯函数 (e.g. `BoltCirclePositions`)
  - `dotnet test` 必须每次都过，pre-commit hook 强制
- **L2 CLI 集成** (PowerShell 脚本)：
  - 每个 MVP 工具都有 `tests/integration/M*-*.test.ps1`
  - 验证业务核心 + SW Interop **完全独立 LLM / MCP**
  - 跑得快 (~5 秒/case)，调试 SW bug 主战场
- **L3 MCP 协议** (人工)：每次 dotnet build 后重启对话 1 次抽测
- **L4 E2E 体验** (人工)：MVP 完成 demo 时用

### L2 测试脚本范例

```powershell
# tests/integration/M2-cylinder.test.ps1
$out = "C:/tmp/test_cyl_$(Get-Random).sldprt"

# 跑 CLI
$json = & mech-pilot-sw create-cylinder `
    --diameter 30 --length 50 --out $out --output json
$result = $json | ConvertFrom-Json

# 验证返回
if ($result.status -ne "ok") { throw "CLI returned: $($result.status)" }

# 验证文件
if (-not (Test-Path $out)) { throw "File not created: $out" }
if ((Get-Item $out).Length -lt 1024) { throw "File too small" }

# 可选：用 SW Document Manager 进一步验证几何
# (M2 阶段不强求，L4 体验测试时人工 SW 打开看)

Remove-Item $out
Write-Host "✅ M2 cylinder OK"
```

### CI 策略

- **GitHub Actions Windows runner**: 跑 L1 + `dotnet build`（公共 runner 没 SW）
- **本地 pre-commit hook**: L1 dotnet test 必过
- **本地手动**: L2/L3/L4 在开发机上人工跑（开发机有 SW）

---

## 复用的老 mech-pilot 资源

- **`docs/SW_API_REFERENCE.md`** → 拷到 `mech-pilot-v2/docs/`，保留 §1-§9 全部知识沉淀
- **`docs/DEV_LOG.md`** → 拷到 `mech-pilot-v2/docs/v1-history.md`，保留 35 PR 教训作参考
- **`scripts/debug_pattern_v*.py`** → 不迁移 (Python 探针在 C# 不直接复用，但作为 SW 行为参考很值钱)
- **`config/e2e_cases.yaml`** → 37 个 E2E case 不直接迁移 (Claude Code 没等价 runner)，但 prompt 内容可借鉴

---

## 验证清单

执行完整 MVP 后，应能：

**基础**：
1. **Build OK**: `dotnet build` 无 error
2. **Static check**: `dotnet format --verify-no-changes` 通过
3. **L1 单元测试**: `dotnet test` 100% PASS
4. **入口分发**：`mech-pilot-sw.exe --help` 显示子命令列表 / `mech-pilot-sw.exe` (无参) 启 MCP server

**M1 (双入口 + ping)**：
5. **L2 CLI**: `mech-pilot-sw ping` → 终端打印 "pong"
6. **L3 MCP**: 重启当前对话窗后，我跑 `mcp__mech_pilot_sw__ping` → 终端显示 "pong"

**M2 (create_cylinder)**：
7. **L2 CLI**: `mech-pilot-sw create-cylinder --diameter 30 --length 50 --out C:/tmp/cyl.sldprt`
   → 文件真生成 + SW UI 能正确打开
8. **L2 集成脚本**: `tests/integration/M2-cylinder.test.ps1` 自动化跑过
9. **L3/L4 MCP**: 重启对话后我跑 `mcp__mech_pilot_sw__create_cylinder({...})` 或你说"画 D30 L50
   圆柱" → 同样文件生成

**M3 (create_flange)**：
10. **L2 CLI**: `mech-pilot-sw create-flange --outer 80 ... --out C:/tmp/flange.sldprt`
    → 8 条 M6 边 + 2 条 D30 中心孔边 + 2 条 D80 外圆边都在
11. **L2 集成脚本**: `tests/integration/M3-flange.test.ps1` 自动化跑过
12. **L4 E2E**: 你说"画 D80 t10 法兰 + 4 M6 PCD55" 我决策 + 几何正确
13. **错误路径 L2**: `mech-pilot-sw create-flange --outer 50 --pcd 60 ...`
    → 退出码 ≠ 0 + 打印 "[error] pcd must be < outer"
14. **错误路径 L3**: 你说"画 inner > outer 的管" → MCP isError → 我提示你修正

---

## 后续路线 (MVP 后)

MVP 跑通后**暂停**，根据实际体验决定：

- 如果 LLM 工具使用流畅 → 启动"前 10 高频工具"迁移 (1-2 周)
- 如果发现架构问题 → 调整后再扩
- 如果想做 SW 插件 UI → 评估 SW Add-in 框架 (C# SwAddin)，复用同一个 `Tools/` 业务核心
  (第 4 个 frontend：SW 任务面板 WPF 控件 → 同样调 `Tools/CreateFlangeTool.Run`)
- 如果想做 CI 自动化 → CLI 模式已经天然支持 (M2/M3 的 CLI 集成测试可直接进 CI pipeline)
- 如果想加 REPL 模式 → 加 `Entrypoints/ReplRunner.cs` (无 LLM 的人机交互，工厂内网环境可用)

---

## 风险 & Mitigation

### 阶段一 (MVP) 风险

| 风险 | Mitigation |
|---|---|
| **SW 官方预编译 Interop DLL 版本不匹配 SW 2026 SP02.1** | 实测 `G:\solidwork\SOLIDWORKS Corp2026\SOLIDWORKS\api\redist\` DLL 与运行的 SW 版本一致；fallback 用 `COMReference + GUID` |
| **MCP NuGet `ModelContextProtocol` 版本不稳定** | 锁版本号；备选 `Anthropic.ModelContextProtocol` 等社区实现 |
| **early binding 在 SW 上仍有怪行为** | 借 PR #35 经验：录 VBA 宏对比 C# 调用差异 (但 C# 不像 pywin32 有 VARIANT 三件套坑，预期问题更少) |
| **Claude Code 中文 prompt / 中文 SW UI 编码** | UTF-8 stdio 强制；SW 中文特征名 (如 "切除-拉伸1") 在 C# string 原生支持 |
| **MCP server 启动慢 (cold spawn SW)** | CLI 模式可加 `--no-sw` flag (ping 类不连 SW)；MCP 模式 lazy connect (第一个工具调用时再连) |
| **CLI / MCP 行为分歧** | 业务核心放 `Tools/` 强制共用；两个入口都用同一个 spec POCO + ToolResult；M1/M2/M3 验证清单要求两路径都过 |
| **MCP server 工具改完要重启 session** | 接受这个 cost；开发节奏 = 改 C# → `dotnet build` → 重启对话 → 测；改 prompt / docs 不影响 |

### 阶段二 (Claude Code 切换) 兼容性分析

| 项 | 当前 Claude Code | leaked source build | 兼容性 |
|---|---|---|---|
| MCP stdio 协议 | 实现完整 | 实现完整 (稳定协议) | ✅ 100% |
| `mcp.json` 格式 | 标准 | 标准 | ✅ 100% |
| 工具命名 `mcp__<server>__<tool>` | 标准 | 同 | ✅ 100% |
| `isError` 错误处理 | 标准 | 同 | ✅ 100% |
| system prompt 风格 | Anthropic 当下版本 | leaked 那时快照 | ⚠️ 95% |
| 通用工具 (Bash/File/Glob) | 最新 | 旧几个月 | ⚠️ 95% (不影响 SW 业务) |

**结论**：阶段二切换 mech-pilot-sw **零代码改动**，仅 `mcp.json` 重指向。切换风险极低。

### 阶段二切换风险点

| 风险 | 严重度 | Mitigation |
|---|---|---|
| 旧版 LLM prompt 工程不如最新 → 选错工具频率高 | 🟡 中 | 我们 server 的 `description` 写好就能补 |
| 缺新通用工具特性 | 🟢 低 | 不影响 SW 业务 |
| MCP 协议版本不匹配 | 🟢 极低 | MCP 协议向后兼容；检查 leaked source 的 MCP 实现版本 |
| 中文 prompt 表现差 | 🟡 中 | 实测决定，可换模型 |

**切换工作量**：1-3 天（含装 / build / 验证），可推迟到 MVP 跑稳后。

---

## 不在 MVP 范围

- ❌ SW 插件对话框 UI (用户明确说"暂时不弄")
- ❌ 28 工具全迁 (MVP 后再决定)
- ❌ E2E 自动化 runner (MVP 手动验证)
- ❌ CI/CD (Windows + SW 依赖，本地验证为主)
- ❌ 跨 OS 支持 (SW Windows only，无意义)
