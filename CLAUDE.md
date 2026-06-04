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

✅ **MVP 已完成** (2026-05-27)。**「前 10 高频工具」迁移 10/10 完成** (M14 提前
做完, 因 PR #25 沉头/锥头沉孔与同 helper refactor 一起做)。M22 起进入"几何能力
拓展"阶段 (revolve / pattern_circular / angle mate / shell 等)。**21 工具**:

| Tool | LLM-facing name | 用途 |
|---|---|---|
| ping | `mcp__mech_pilot_sw__ping` | sanity check |
| create_cylinder | `mcp__mech_pilot_sw__create_cylinder` | 圆柱零件 |
| create_flange | `mcp__mech_pilot_sw__create_flange` | 法兰 / 端盖 / 周向孔板 |
| create_rectangular_block | `mcp__mech_pilot_sw__create_rectangular_block` | 长方体零件 (L×W×H 居中) |
| add_fillet | `mcp__mech_pilot_sw__add_fillet` | 给已有零件全边加等半径圆角 |
| add_chamfer | `mcp__mech_pilot_sw__add_chamfer` | 给已有零件全边加等距倒角 (45°) |
| add_axial_hole | `mcp__mech_pilot_sw__add_axial_hole` | 在 ±Z 端面加 Φ 通孔 / 盲孔 |
| add_threaded_hole | `mcp__mech_pilot_sw__add_threaded_hole` | GB 螺纹孔 M3-M12 (真螺纹特征) |
| add_counterbore | `mcp__mech_pilot_sw__add_counterbore` | GB/T 152.3 柱形沉头孔 (内六角圆柱头螺钉) M3-M12 |
| add_countersink | `mcp__mech_pilot_sw__add_countersink` | GB/T 152.2 锥形沉头孔 (沉头螺钉, 90°) M6-M12 |
| mirror_feature | `mcp__mech_pilot_sw__mirror_feature` | 沿 Front / Top / Right 基准面镜像特征 |
| pattern_linear | `mcp__mech_pilot_sw__pattern_linear` | 1D / 2D 线性阵列特征 |
| pattern_circular | `mcp__mech_pilot_sw__pattern_circular` | 绕 ±Z 轴圆周阵列特征 (PCD 孔环等) |
| inspect_part | `mcp__mech_pilot_sw__inspect_part` | 读取零件元数据（bbox / 特征 / 面+边数） |
| export_part | `mcp__mech_pilot_sw__export_part` | 导出 STEP / STL / IGES / Parasolid |
| new_assembly | `mcp__mech_pilot_sw__new_assembly` | 创建空装配体 (.sldasm) |
| add_component | `mcp__mech_pilot_sw__add_component` | 把零件/子装配体插入装配体 (x,y,z) 位置 |
| inspect_assembly | `mcp__mech_pilot_sw__inspect_assembly` | 读取装配体组件列表（实例名 / 源路径 / 位置） |
| add_mate_coincident | `mcp__mech_pilot_sw__add_mate_coincident` | 两组件 reference plane 重合配合 |
| add_mate_distance | `mcp__mech_pilot_sw__add_mate_distance` | 两组件 reference plane 间距 N mm 配合 |
| add_mate_concentric | `mcp__mech_pilot_sw__add_mate_concentric` | 两组件轴向 ±Z 圆柱面同轴配合 |

**L1 / L2 验证通过** (421/421 单元测试 + 19 个 PowerShell L2 集成); 后 10 工具
+ create_flange L3 抽测 zero bug (M15); **装配家族全 6 工具 (new_assembly +
add_component + inspect_assembly + add_mate_coincident + add_mate_distance +
add_mate_concentric) L3 全过 zero bug** (distance + concentric 于 2026-06-04
session 收口, 几何验证生效)。
M5 in-place SaveAs / M20 path-separator bug 都已修。v1 PR #32 真根因 (FCP3 spacing
公式) 在 M22 pattern_circular 复刻一次过 (**8 连击 zero-试错**)。
**`Tools/Internal/PartGeometryHelpers`** 抽出共用 `FindPlanarEndFace` +
`FindLastUserFeature` + `IsBootFeature` 给 8 工具用。

**下一步候选**: revolve (球壳/锥/旋转件) / add_mate_angle (机械臂关节摆角) /
shell (薄壁壳) / save_drawing 工程图。pattern_circular L3 已抽测 zero bug
(2026-06-05 含 featureCount/edgeCount 几何验证, M22 收尾段)。详见
[`docs/DEV_LOG.md`](docs/DEV_LOG.md) "下一步候选" 段。

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

> MVP 实战累积的 12 条铁律. 新工具 PR 前对照检查.

1. **业务核心 vs 入口分离**：所有 SW 业务逻辑放 `Tools/`，MCP 入口和 CLI 入口都调
   同一个 Tool 类的同一个方法。**写一份，多处用**。

2. **多入口都要验证**：L2 集成测试 (CLI) + L3/L4 MCP 抽测，不能只跑一个。
   两个入口行为分歧 = 业务核心放错位置。

3. **错误处理统一走 `McpToolException`**：业务核心 throw 这个异常，CLI 模式打印
   `[error] xxx` + 退出码 ≠ 0，MCP 模式自动转 `isError=true`。

4. **早期绑定 (Early Binding) 优先**：用 `SolidWorks.Interop.sldworks` 程序集
   直接 reference (`EmbedInteropTypes=true`)，享受 IntelliSense + 类型检查 + 枚举常量。
   避免重蹈 v1 late binding 覆辙 (详 `docs/v1-history.md` PR #35)。

5. **签名前置反射** (M2 教训): 调任何新 SW API 前**先 PowerShell + Reflection
   读 DLL 拿真签名**, 不照搬 VBA 文档. SW 2024+ 部分 API 尾部加了 Variant 占位
   (详 `docs/SW_API_REFERENCE.md` §3)。
   ```powershell
   $asm = [Reflection.Assembly]::LoadFrom('G:\solidwork\SOLIDWORKS Corp2026\SOLIDWORKS\api\redist\SolidWorks.Interop.sldworks.dll')
   $asm.GetType('SolidWorks.Interop.sldworks.IFeatureManager').GetMethod('FeatureXxx').GetParameters() |
     ForEach-Object { Write-Host ("  [{0,2}] {1,-30} {2}" -f $_.Position, $_.Name, $_.ParameterType.Name) }
   ```

6. **`SelectByID2 + empty Name + coord` 在 API 模式不可靠** (M3 教训): 没 active
   view 时 SW ray-cast 行为不可预测. 选面用 body 导航:
   `GetBodies2 → GetFaces → IsPlane → Normal.Z → IEntity.Select4`.

7. **新 SW API 不工作就降级到老版本** (M3 教训): `FeatureCut4` 在 face-based 草图
   silent return null, 切到 `FeatureCut2` 第一次就成. 表面积小的版本往往绕过新版
   严苛的 selection state 前置条件.

8. **录的宏 ≠ reliable code**：SW 录的 .swp 宏只是参考起点，复杂特征 (pattern /
   mirror / hole_wizard) 单独跑很可能 silent fail (v1 经验)。**先 VBA IDE 里手
   动跑通宏**再 1:1 复刻到 C#。

9. **绕过限制思维**：某个 SW API 多 stage 探针仍 silent fail (如 v1 PR #35
   pattern_circular)，**绕过比硬刚强**：加一个粗粒度一键工具
   (如 `create_flange` 一次画所有孔 + 一次 cut，根本不调 pattern)。

10. **PR merge 后别再 push 老分支** (M2 配套教训): 老分支上的新 commit 是孤儿,
    没进 master. 修复: 新分支从 master 拉 + `git cherry-pick <commit>` + 新 PR.

11. **build 期间 .exe 可能被 MCP server 锁住** (M3 教训): 另一个 Claude session
    挂了 mech-pilot-sw MCP 时, 那个进程占住 Debug exe.
    `Stop-Process -Name mech-pilot-sw -Force` 即可 (另一 session 下次调工具自动
    re-spawn).

12. **commit message 用 conventional commits**：
    - `feat(scope): subject` — 新功能
    - `fix(scope): subject` — bug 修复
    - `docs:` / `chore:` / `test:` / `refactor:`

13. **新工具 L3 必抽 1 次** (M15 教训): L2 fresh exe 永远撞不到 MCP 长寿命
    server 上的热 SW 状态 bug (典型如 M5 in-place SaveAs `errors=0x1`)。新
    工具 PR 合入后必须在**当前 / 新 session 用 MCP 协议层调用 1 次**, 验证:
    - 工具确实出现在 server 工具列表里 (注解注册成功)
    - 长寿命 server 调用不挂 (vs fresh exe)
    - 与既有工具组合调用不挂 (跨工具状态污染)

    沉淀方式: PR description 加 "L3: 待新 session 重启抽测", 后续 session 跑
    至少一次, 撞 bug 单独 PR 修 (M5 模式), 不撞就在 DEV_LOG 记 zero-bug。

14. **path 分隔符 normalize** (M20 教训): SW Interop API 若涉及"通过 path
    字符串匹配已加载 doc" (典型如 `AddComponent5(CompName)` / 其他 path-based
    lookup), 工具内部必须 `Path.GetFullPath()` normalize 输入路径到
    OS-canonical 形式 (Windows = `\`)。不然 LLM / MCP 用 `/` 路径 → OpenDoc6
    成功但 SW 内部 store 成 `\` → 后续 path 比对找不到 → silent null。
    **L2 PowerShell `Join-Path` 自动产 `\`, 撞不到此 bug — 必须显式加一个
    forward-slash 路径的 L2 case 防回归。**

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

## 进入新对话先做这 4 件事

1. **读 [`docs/DEV_LOG.md`](docs/DEV_LOG.md)** → 5 分钟拿到 MVP 全貌 + 8 条核心
   踩坑教训 + 下一步候选 (本文件是 30 秒入门, DEV_LOG 是开发上下文全貌)
2. **跑 `gh pr list --state open`** → 看是否有未合 PR 影响本次开发
3. **跑 `git log --oneline -10`** → 看最近 commits 状态
4. **按需深读** [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) (完整架构 + 15 决策),
   [`docs/SW_API_REFERENCE.md`](docs/SW_API_REFERENCE.md) (SW API 知识库 710 行),
   [`docs/v1-history.md`](docs/v1-history.md) (老 v1 35 PR 教训 992 行)

---

## 目录结构

```
mech-pilot-sw-mcp/                  ← 项目根 (= GitHub repo)
├── README.md                       # 对外说明
├── CLAUDE.md                       # 本文件 (AI 助手项目级指引)
├── .gitignore                      # C# .NET + IDE + Windows
├── .mcp.json                       # 项目级 Claude Code MCP 配置 (PR #3)
├── MechPilot.SwMcp.sln             # solution: 让 dotnet/CI 统一操作两个 csproj
├── .github/
│   └── workflows/
│       └── ci.yml                  # GitHub Actions (dotnet build/test/format)
├── MechPilot.SwMcp/                # C# 主项目 (net8.0-windows)
│   ├── MechPilot.SwMcp.csproj      # AssemblyName=mech-pilot-sw + HasSolidWorks 条件
│   ├── Program.cs                  # 入口分发 (args 空 → MCP / 非空 → CLI)
│   ├── Entrypoints/
│   │   ├── McpServer.cs            # MCP stdio 入口
│   │   └── CliRunner.cs            # CLI 子命令入口 (ping / create-cylinder / create-flange / add-fillet)
│   ├── Tools/                      # 业务核心 (CLI + MCP + L1 测试共用)
│   │   ├── PingTool.cs             # M1
│   │   ├── CreateCylinderTool.cs   # M2
│   │   ├── CreateFlangeTool.cs     # M3
│   │   └── AddFilletTool.cs        # M4 (首个"编辑已有零件"工具)
│   ├── Models/                     # POCO 数据类型
│   │   ├── ToolResult.cs
│   │   ├── CylinderSpec.cs         # + Validate()
│   │   ├── FlangeSpec.cs           # + Validate() (13 个几何约束)
│   │   └── FilletSpec.cs           # + Validate() (InputPath 必须已存在)
│   ├── Interop/
│   │   └── SwConnection.cs         # ISldWorks 单例 + lazy connect (#if HAS_SOLIDWORKS)
│   └── Exceptions/
│       └── McpToolException.cs     # 业务异常 → MCP isError / CLI [error]
├── MechPilot.SwMcp.Tests/          # L1 xUnit 单元测试 (61/61 passed)
│   ├── MechPilot.SwMcp.Tests.csproj
│   ├── PingToolTests.cs
│   ├── CylinderSpecTests.cs        # 15 个用例
│   ├── FlangeSpecTests.cs          # 24 个用例
│   └── FilletSpecTests.cs          # 21 个用例
├── tests/
│   └── integration/                # L2 PowerShell 集成测试 (需本机有 SW)
│       ├── M1-ping.test.ps1
│       ├── M2-cylinder.test.ps1
│       ├── M3-flange.test.ps1
│       ├── M4-fillet.test.ps1
│       └── run-all.ps1
└── docs/
    ├── DEV_LOG.md                  # **新会话先读** — MVP 全貌 + 踩坑教训 + 下一步
    ├── ARCHITECTURE.md             # 架构方案 (605 行, 按需深读)
    ├── SW_API_REFERENCE.md         # SW API 知识库 (710 行, 从 v1 迁移)
    └── v1-history.md               # 老 mech-pilot 35 PR 教训 (992 行)
```

---

## 联系

- 项目维护者：[@adam0wanna0top](https://github.com/adam0wanna0top)
- 老 v1 项目：[mech-pilot](https://github.com/adam0wanna0top/mech-pilot)
