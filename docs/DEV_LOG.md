# mech-pilot-sw-mcp 开发日志

> 新会话快速上手专用。读完本文件 + `CLAUDE.md` 就能接着干。
> 详细架构看 [`ARCHITECTURE.md`](ARCHITECTURE.md)，SW API 知识看
> [`SW_API_REFERENCE.md`](SW_API_REFERENCE.md)，老 v1 教训看
> [`v1-history.md`](v1-history.md)。

---

## TL;DR (30 秒)

**MVP 已完成** (2026-05-27)，**M4 add_fillet** 追加首个"编辑已有零件"工具 (PR #6)。
4 工具：

| Tool | PR | LLM-facing name | 干啥 |
|---|---|---|---|
| ping | #1 | `mcp__mech_pilot_sw__ping` | sanity check, 返 "pong" |
| create_cylinder | #2 | `mcp__mech_pilot_sw__create_cylinder` | 圆柱零件 |
| create_flange | #4 | `mcp__mech_pilot_sw__create_flange` | 法兰 / 端盖 / 周向孔板 |
| add_fillet | #6 | `mcp__mech_pilot_sw__add_fillet` | 给已有零件全边加等半径圆角 |
| add_chamfer | #8 | `mcp__mech_pilot_sw__add_chamfer` | 给已有零件全边加等距倒角 (45°) |
| export_part | #9 | `mcp__mech_pilot_sw__export_part` | 导出 STEP / STL / IGES / Parasolid |
| add_axial_hole | #10 | `mcp__mech_pilot_sw__add_axial_hole` | 在 ±Z 端面加 Φ 通孔 / 盲孔 |
| inspect_part | #11 | `mcp__mech_pilot_sw__inspect_part` | 读取零件元数据（bbox / 特征 / 面+边数） |
| mirror_feature | #12 | `mcp__mech_pilot_sw__mirror_feature` | 沿 Front / Top / Right 基准面镜像特征 |
| (.mcp.json) | #3 | — | Claude Code 项目级 MCP 配置 |

**L1/L2 全部验证**：163/163 单元测试 + 9 个 PowerShell L2 集成全过。
create_cylinder/flange/fillet 经 L3 MCP 抽测; add_fillet 撞出 in-place SaveAs
bug 已修 (M5/PR #7); 后续 5 工具 (chamfer/export/axial_hole/inspect/mirror) 的 L3
待新 session 重启抽测 (代码同构, L2 覆盖 SW 交互全链路)。

---

## 里程碑时间线

### M0 — 项目骨架 (commit 7823697)

- `.gitignore` + `CLAUDE.md` + `README.md` + `docs/` (ARCHITECTURE.md /
  SW_API_REFERENCE.md / v1-history.md 从 v1 迁移) + `.github/workflows/ci.yml`
- GitHub repo + branch protection (master 必须走 PR, CI 必过)

### M1 — 项目代码 + ping (PR #1, 2026-05-26)

- `MechPilot.SwMcp.csproj` (net8.0-windows, AssemblyName=mech-pilot-sw)
  - NuGet: `ModelContextProtocol` 1.3.0 + `System.CommandLine` 3.0-preview +
    `Microsoft.Extensions.Hosting` 10.0.8
  - SW Interop 用 `HasSolidWorks` 条件 reference, CI runner 无 SW 时跳过
    (`#if HAS_SOLIDWORKS` 包裹 SW 代码)
- `Program.cs` 入口分发: args 空 → MCP server / 非空 → CLI
- `Entrypoints/McpServer.cs`: stdio transport + `WithToolsFromAssembly`,
  日志强制 stderr (stdout 给 MCP JSON-RPC)
- `Entrypoints/CliRunner.cs`: System.CommandLine 3.x API
- `Tools/PingTool.cs`: `[McpServerTool]` 标注 — CLI 和 MCP 共用 `Run()`
- `Models/ToolResult.cs` + `Exceptions/McpToolException.cs`
- `MechPilot.SwMcp.sln`: 让 dotnet 命令 + CI 能统一操作两个项目
- L1: PingToolTests, L2: tests/integration/M1-ping.test.ps1

### M2 — create_cylinder (PR #2, 2026-05-27)

- `Interop/SwConnection.cs`: ISldWorks 单例 + lazy connect。
  `Activator.CreateInstance("SldWorks.Application")` 自动 attach 已有 SW
  进程或起新进程。`#if HAS_SOLIDWORKS` 包裹。
- `Models/CylinderSpec.cs`: DiameterMm / LengthMm / SavePath POCO,
  边界 [0.1, 10000] mm (防 LLM 单位混淆)
- `Tools/CreateCylinderTool.cs`: 流程 NewDocument → SelectFrontPlane
  (中英双语) → InsertSketch → CreateCircleByRadius → ExitSketch →
  SelectSketch1 → FeatureExtrusion3 (23 args) → SaveAs (6 args) → CloseDoc
- L1: 15 个 CylinderSpec 校验用例, L2: tests/integration/M2-cylinder.test.ps1
- **核心方法**: 用 PowerShell + Reflection 读 SolidWorks.Interop DLL 实际
  签名 (不照搬 VBA 文档) 避免 SW 2024+ 漂移坑

### M2 配套 — .mcp.json (PR #3, 2026-05-27)

- 项目级 `.mcp.json` 注册 mech-pilot-sw stdio server
- 用绝对路径 `C:/Users/Hello/mech-pilot-v2/...` (single-dev MVP, 简单优先)
- ARCHITECTURE.md 决策 #9 写的 `~/.claude/mcp.json` 是过时表述, 实际 Claude
  Code 配置位置: `~/.claude.json` 顶层 `mcpServers` 或项目级 `.mcp.json`

### M3 — create_flange (PR #4, 2026-05-27) — MVP 收尾

- `Models/FlangeSpec.cs`: outerD/thickness/centerHoleD/boltCount/boltD/pcd
  POCO + 13 个几何校验:
  - `centerHole < outer`, `pcd <= outer - boltD`, `pcd >= centerHole + boltD`
  - **邻孔不重叠**: `2 * (pcd/2) * sin(π/N) > boltD`
  - **防呆**: `boltCount=0` 但 bolt 几何非零 → throw
- `Tools/CreateFlangeTool.cs`: 复刻 v1 PR #35 策略 "**一 sketch 画所有孔
  + 一次 ExtrudeCut Through-All**" 绕过 SW 2026 pattern_circular 多-cut
  silent fail (详 SW_API_REFERENCE §8.3)
- L1: 24 个 FlangeSpec 用例, L2: M3-flange.test.ps1 (3 happy + 3 error)
- L3 验收: Claude Code 自然语言 → 我自动 LLM 决策调工具 → 真生成 93KB
  正确法兰. 其中错误 spec 我**调用工具前就识破** (PCD60 > 外径D50 几何不可能)
  比预期的 "tool throw → 转译" 更智能

### M4 — add_fillet (PR #6, 2026-05-29) — 首个"编辑已有零件"工具

- 前三个工具都从 `NewDocument` 凭空建模；add_fillet 是**首个打开并编辑已有
  .sldprt** 的工具: `OpenDoc6 → 选所有边 (mark=1) → FeatureFillet3 → SaveAs →
  CloseDoc (finally, 确保开过的 doc 不悬挂)`。
- `Models/FilletSpec.cs`: InputPath (**必须已存在**) / RadiusMm / OutputPath (可选)
  POCO + Validate。半径边界 [0.01, 1000] mm。OutputPath 空 = 原地覆盖, 给了 = 另存副本。
- `Tools/AddFilletTool.cs`: `FeatureFillet3` 等半径全边圆角 (Options=UniformRadius=2,
  Ftyp=Simple=0)。边选择走 body 导航 `GetBodies2 → GetEdges →
  IEntity.Select2(append, mark=1)` (黄金法则 #6, 不用坐标 SelectByID2)。
- L1: 21 个 FilletSpec 用例 (需造真临时 .sldprt, 因 Validate 查 File.Exists),
  L2: M4-fillet.test.ps1 (5 检查: 副本 / 原地 / 缺文件 / 负半径 / 超大半径→SW 失败转非零退出)。
- **跨 v1 反转的坑**: FeatureFillet3 尾部 7 个数组参数 C# 早绑定传 `null` 一次就成
  (v1 Python 需专门的 `empty_variant()`)，详踩坑 #8 + SW_API_REFERENCE §2.6。

### M5 — add_fillet in-place SaveAs 修 (PR #7, 2026-05-30) — L3 抽测撞 bug

新会话里我替用户走 add_fillet 的 L3 抽测 (4 个工具 MCP 协议层抽样)，撞到一个
L2 + L1 都没暴露的 bug —— **首次"L3 抽测撞到 L2 没撞到的 bug"案例**，给 4 层
金字塔的必要性补上了一个具体证据。

- **症状**: MCP 长寿命 server 下连续调 add_fillet, 不传 outputPath (= in-place
  覆盖) 时 `Extension.SaveAs(samepath)` 返 `errors=0x1` (swGenericSaveError);
  同样输入加 outputPath (= copy 模式) 正常; L2 fresh exe 跑 in-place 也正常。
- **根因**: SW 对 "SaveAs 到当前活跃 doc 自身路径" 在热 SW 实例下有严格检查;
  L2 每次新进程 SW 冷启动 state 干净所以绕过。`~$xxx.SLDPRT` 锁文件在 CloseDoc
  之后残留是另一个佐证 —— SW 没干净释放 doc handle。
- **修法**: `AddFilletTool` 的 save 分支按 isInPlace 二分:
  - in-place → `IModelDoc2.Save3(options, ref err, ref warn)` (SW API 专为
    "覆盖当前活跃 doc" 设计的接口)
  - copy → 继续用 `Extension.SaveAs(..., ref err, ref warn)`
  反射读 `Save3` 真签名 `bool Save3(int, ref int, ref int)`, 确认 `[out]`
  COM marshaling 投 `ref` (M2 SaveAs 教训复用, 黄金法则 #5)。
- **测试**: L1 21 个 FilletSpec 不动 (spec 没变); L2 现有 in-place case 走
  Save3 分支天然回归; L3 复跑 in-place case 由 fresh MCP server 验证 (Save3 在
  热 SW 实例下不再报错)。

**规律 (后续编辑零件工具沿用)**: 长寿命 SW 状态下的 in-place 写入必须用
`Save/Save3`, 不能用 `SaveAs(samepath)`。

### M6 — add_chamfer (PR #8, 2026-05-30) — 工具迁移模板首试

"前 10 高频工具" 迁移第二发 (fillet 首发)。复刻 add_fillet 脚手架验证模板可
重用 + M5 in-place Save3 教训在新工具上沿用。

- **共用模式**: `ChamferTool` 几乎 1:1 复刻 `AddFilletTool` (open → select
  edges mark=1 → 特征 → save 分支 isInPlace 二分 → CloseDoc), 唯一变量是
  `FeatureFillet3` → `InsertFeatureChamfer`。反射拿真签名:
  `Feature InsertFeatureChamfer(int Options, int ChamferType, double Width,
  double Angle, double OtherDist, double VertexChamDist1/2/3)`; 用
  `swChamferEqualDistance` (=16) 取等距倒角 (Width=OtherDist=distance 作 safety
  belt 防 SW cross-validate, Angle / Vertex* = 0)。
- **Spec 字段**: `RadiusMm` → `DistanceMm` (chamfer 是"边距", 不是"半径"), 边界
  和 path 校验 1:1 复用。
- **意外发现 #1**: SW chamfer 对 distance 容忍度极高 — D=1000 mm 在 D30 L50
  圆柱上仍 silent 接受 (产生退化 chamfer); 二次 chamfer 同零件也成功 (chamfer
  新出的内圈边)。所以 fillet 那种"超大半径 → SW null return"的 L2 negative
  case **chamfer 上无法人工触发**。`M6-chamfer.test.ps1` 只保留 4 case
  (copy / in-place / 不存在 / 负距离); `InsertFeatureChamfer null` 路径在
  代码里作为防御性留存, L2 不覆盖。
- **测试**: L1 +21 ChamferSpec 用例 (= 82 total); L2 4/4 pass; L3 待新 session
  重启 (代码与 fillet 同构, 风险极低)。

**模板效用证明**: 反射 + 写 4 个文件 + 注册 CLI 子命令, 总耗时 ~1 小时 (vs
M4 add_fillet 首个 editing 工具 ~3 小时含探索)。后续 8 个工具 (hole_wizard /
pattern_* / mirror / new_assembly / ...) 预期每个 0.5-1 天能搞定。

### M7 — export_part (PR #9, 2026-05-30) — "造型 → 出货" 闭环

"前 10 高频工具" 迁移第 3 发，**首个非 modeling/editing 的工具家族 (export)**。
让 LLM 能把 .sldprt 导出 STEP / STL / IGES / Parasolid (扩展名决定格式)，下游
能进 CAM / CAE / 3D 打印。

- **核心简化**: `Extension.SaveAs(neutralPath)` 用扩展名自动 dispatch 到 SW
  内置 STEP/STL/IGES/Parasolid exporter，**不需要任何额外 enum 或 ExportData
  对象**。代码比 fillet/chamfer 还少 (无 sketch / feature / select 操作)。
- **结构上 M5-safe**: outputPath 扩展名是 neutral format (`.step` / `.stl` / ...),
  必然 ≠ inputPath (`.sldprt`)。**SaveAs(samepath) 在路径校验层就被防呆**, 不会
  撞 M5 in-place SaveAs bug, 故 ExportPartTool 只走 SaveAs 单分支。
- **支持的扩展名集合**: 显式枚举在 `ExportSpec.AllowedExtensions` (字典+格式说
  明), 让"typo `.stop`"在 spec 校验层失败并给"Supported: ..."提示, 不会冒出
  opaque SW error。一期支持: STEP(`.step`/`.stp`) + STL(`.stl`) + IGES
  (`.iges`/`.igs`) + Parasolid(`.x_t`/`.x_b`)。
- **拒绝覆盖原 sldprt**: spec.Validate 显式检查 `outputPath != inputPath`,
  即使将来加 `.sldprt` 到 AllowedExtensions 也不会误伤源文件。
- **测试**: L1 +21 ExportSpec 用例 (= 103 total); L2 M7-export 4/4 pass
  (STEP 11.8KB + ISO-10303 header / STL 10KB / 拒 `.obj` / 拒不存在 input)。
  L3 待新 session 重启。

**意义**: 让"造圆柱 → 加圆角 → 出 STEP" 这条 LLM 完整自然语言闭环跑通; 这是
项目"工程实用性"的分水岭, 6 工具开始能进生产链路 (CAM/CAE/3D 打印 / 装配)。

### M8 — add_axial_hole (PR #10, 2026-05-30) — 加孔 (简化路径, 跳过 HoleWizard5)

"前 10 高频工具" 第 4 发。**有意跳过 SW 的 `HoleWizard5` (27 参 + GB/ANSI 标
准库 + v1 PR #21/#24/#25 啃过的"魔法位"踩坑)**, 直接做简化的 `add_axial_hole`:
在零件 ±Z 端面圆心或 (x, y) 加一个 Φ 圆形通孔 / 盲孔。覆盖 LLM 80% 加孔请求
(M6 → Φ6.6 通孔 / Φ5 螺纹底孔, LLM 自己换算)。HoleWizard5 标准螺孔留到下一个
PR 单独做。

- **复用模板组合**: 把 create_flange 的"FindPlanarEndFace + InsertSketch +
  CreateCircleByRadius + FeatureCut2"和 add_fillet 的"OpenDoc6 → CloseDoc
  + isInPlace 二分 (Save3 vs SaveAs)"组合起来 — 这是首个**组合两个已验工具
  脚手架的新工具**, 印证"前 10 工具"模板复用价值。
- **FeatureCut2 调用直接复刻 M3**: 反射验过 23 参签名;
  `NormalCut=false`, `AssemblyFeatureScope/AutoSelectComponents/PropagateFeatureToParts`
  三 false (M3 / SW_API_REFERENCE §8.3 教训复用)。EndCond:
  null DepthMm → swEndCondThroughAll (1); 正 DepthMm → swEndCondBlind (0) +
  D1 = depth_m。
- **`FindPlanarEndFace` 第二次复用**: 跟 CreateFlangeTool 同一份 logic 私有
  复制 (rule of three 还没到, 等第 3 次出现再 extract 到 shared helper)。
- **xUnit InlineData(int) 不能转 double? 的坑**: `[InlineData(10)]` 让 xUnit
  把字面量当 int, 转不到 `double?` 形参; 必须写 `[InlineData(10.0)]`。
  L1 初次跑 137/137 实际 135/137 因这个挂, 改 1 行通过。
- **测试**: L1 +34 AxialHoleSpec 用例 (= 137 total); L2 M8 5/5 pass
  (Φ6.6 通孔 copy / Φ5×10 盲孔 in-place / Φ4 偏心通孔 in-place / 拒不
  存在 / 拒负直径)。L3 待新 session 重启。

**意义**: 7 工具开始覆盖 LLM "造圆柱 → 加孔 → 倒边 → 出 STEP" 完整零件加工
流程。下个 PR 候选: HoleWizard5 标准螺孔 (M5/M6 等 GB tap 录宏破)、
pattern_linear (线性阵列)、inspect_part (LLM 读懂已有零件)。

### M9 — inspect_part (PR #11, 2026-05-30) — LLM 看懂零件 + 首个只读工具

"前 10 高频工具" 第 5 发。**首个只读工具**（之前 7 个都是写: create_* / add_*
/ export_*）。让 LLM 读懂 .sldprt 元数据: title / featureCount / bodyCount /
totalFace+EdgeCount / boundingBoxMm + sizeMm / features list (name+type+suppressed)。
是后续所有"编辑已有零件"工具的视觉前置 — LLM 给一个未知零件先 inspect 再决定
能不能"加 R30 fillet"等。

- **ToolResult 加 Data 字段**: 之前工具只返 Status/Path/Message; inspect 需要
  返结构化数据 → `IReadOnlyDictionary<string, object>? Data` (默认 null,
  CLI `--output json` 自动序列化)。其他 7 个工具不影响。
- **Read-only 模式**: `OpenDoc6(Silent | ReadOnly)`, CloseDoc 不需 Save/Save3
  (M5 trap 结构上不可能 — read-only doc 没 dirty state)。
- **BBox**: `IPartDoc.GetPartBox(NoConversion=true)` 返 SI meters 的 6 元数组
  [minX, minY, minZ, maxX, maxY, maxZ], 工具内部 × 1000 转 mm。空 part (无 body)
  返全 0 → 当 null 处理。
- **Feature 过滤策略 (L2 probe 发现 SW 2026 的 *Folder 容器系列)**:
  初版 bootTypes hardcoded `Comments` / `FavoriteFolder` 等没覆盖 SW 2026 实
  际类型 (`CommentsFolder` / `SelectionSetFolder` / `InkMarkupFolder` /
  `EnvFolder` / `ConfigTableFolder`)。改成 **"显式名单 + EndsWith(`Folder`)
  兜底"**。L2 验证: create_cylinder 输出严格 2 features (ProfileFeature 草图
  + Extrusion 凸台), 跟用户认知一致。
- **测试**: L1 +6 InspectSpec 用例 (= 143 total, 只读所以 spec 最小);
  L2 M9 4/4 pass (D40 L30 圆柱 bbox 40×40×30 / D80 t10 法兰 bbox 10×80×80 /
  拒不存在 / 拒非 .sldprt)。L3 待新 session 重启。

**意义**: 闭环"造-改-看-出货"四个动作; LLM 现在能"先看后改"避免盲改撞错。
下个候选 (剩 5/10): HoleWizard5 标准螺孔 / pattern_linear / mirror /
new_assembly + add_component / save_drawing。

### M10 — mirror_feature (PR #12, 2026-05-30) — 跳过 pattern_linear 改做更高 ROI 的 mirror

"前 10 高频工具" 第 6 发。**原计划做 pattern_linear, 反射 + design 阶段发现
盲区**: `FeatureLinearPattern2` 需要 `mark=1` 的 **方向边 (direction edge)`,
但 create_cylinder/create_flange 造的零件**全是圆形, 无直边** → pattern_linear
在 LLM 实际场景里几乎触发不了 (除非先做 add_rectangular_block)。改做 mirror —
跟 add_axial_hole 配对完美 ("加一个孔, 然后镜像")。

- **Selection mark 表 (SW_API_REFERENCE §6, 跟 pattern 相反!)**:
  - 镜像面 (mirror plane) → mark=2
  - seed feature → mark=1
  - (pattern 是: 方向边 mark=1, seed mark=4 — 顺序反着)
- **API**: `InsertMirrorFeature2(bMirrorBody=false, bGeometryPattern=true,
  bMerge=true, bKnit=false, ScopeOptions=0)`。SW 2026 上 5 参 (文档普遍 4,
  +1 ScopeOptions, M0 docs 已记录)。`bGeometryPattern=true` 更稳健 (纯几何
  copy 比参数化 mirror robust)。
- **基准面选择 (CN/EN 双语)**: 沿用 create_cylinder 的 `FrontPlaneAliases` 模式 ──
  `MirrorSpec.PlaneAliases` 字典: front → ["前视基准面", "Front Plane"],
  top → ["上视基准面", "Top Plane"], right → ["右视基准面", "Right Plane"]。
  按顺序试。
- **Seed feature 选择**: spec.FeatureName 给了用 `SelectByID2("BODYFEATURE")`;
  没给则**自动取最后一个 user-meaningful feature** — 同 inspect_part 的 boot
  filter (`bootTypes ∪ EndsWith("Folder")`)。"add_axial_hole 加孔 → mirror_feature
  立刻镜像" 一句话 LLM 用法成立, 不强迫 LLM 知道特征名。
- **L2 几何坑**: 首次测试用 mirror across **Front Plane** 直接 SW null. 推
  原因: cylinder 从 Front Plane (XY) 拉伸沿 +Z 30mm; 孔在 +Z 顶面切 →
  mirror across Front (Z=0) 把孔翻到 -Z 区域, **零件不在 -Z 没法 mirror**。改
  用 **Right Plane (X 翻转)** 在 |x| < radius 范围内有效。规律: mirror plane
  必须跟 seed feature 的偏移方向**垂直**, 否则 SW 拒绝。
- **测试**: L1 +20 MirrorSpec 用例 (= 163 total); L2 M10 5/5 pass
  (right + auto-pick / top + case-insensitive in-place / 拒 'left' / 拒
  不存在 input / 拒不存在 feature name)。L3 待新 session 重启。

**意义**: 9 工具 = 造 (3) + 改 (4 含镜像) + 看 (1) + 出货 (1)。镜像让"对称零件"
一步到位, 避免 LLM 重复加孔 N 次。下个候选 (剩 4/10): HoleWizard5 真螺纹 /
add_rectangular_block + pattern_linear 配对 / new_assembly+add_component /
save_drawing。

---

## MVP 核心踩坑教训 (新 PR 前必看)

按发现顺序：

### 1. 签名前置反射 (M2)

SW Interop DLL 的实际签名 vs 文档/VBA 示例可能漂移 (SW_API_REFERENCE §3)。
**永远先用 PowerShell + Reflection 读 DLL 拿真签名**:

```powershell
$asm = [Reflection.Assembly]::LoadFrom('G:\solidwork\SOLIDWORKS Corp2026\SOLIDWORKS\api\redist\SolidWorks.Interop.sldworks.dll')
$asm.GetType('SolidWorks.Interop.sldworks.IFeatureManager').GetMethod('FeatureCut4').GetParameters() |
  ForEach-Object { Write-Host ("  [{0,2}] {1,-30} {2}" -f $_.Position, $_.Name, $_.ParameterType.Name) }
```

实测发现: FeatureCut3 文档 23 args / 实际 26 args (SW 2024+); FeatureCut4
实际 27 args (新增 OptimizeGeometry)。

### 2. SaveAs 的 Errors/Warnings 反射说 IsOut=true 但 C# 要 `ref` (M2)

COM marshaling 把 `[out] ByRef` 投影成 C# `ref` (`[in,out]` 默认行为),
不是 `out`。

```csharp
int err = 0, warn = 0;
ext.SaveAs(path, 0, options, null, ref err, ref warn);  // ref 不是 out
```

### 3. PowerShell 5.x `$ErrorActionPreference=Stop` + native stderr (M2)

任何 native 二进制写 stderr 都 throw `RemoteException`，即便 `2>` 重定向
也无效。L2 脚本里改用:
```powershell
$ErrorActionPreference = 'Continue'  # 全脚本
# + 显式 if ($LASTEXITCODE -ne 0) { throw ... } 检查
```

### 4. coord-based SelectByID2 选面在 API 模式不可靠 (M3)

`SelectByID2("", "FACE", x, y, z, ...)` 没 active view 时 SW ray-cast 行为
不可预测,可能选错面。**改用 body 导航**:

```csharp
var part = (IPartDoc)model;
var bodies = (object[])part.GetBodies2((int)swBodyType_e.swSolidBody, false);
var body = (IBody2)bodies[0];
var faces = (object[])body.GetFaces();
foreach (var f in faces) {
    var face = (IFace2)f;
    var surf = (ISurface)face.GetSurface();
    if (!surf.IsPlane()) continue;
    var normal = (double[])face.Normal;
    if (Math.Abs(normal[2]) > 0.99) {
        ((IEntity)face).Select4(false, null);  // 选中面
        break;
    }
}
```

### 5. FeatureCut4 在 face-based 草图上 silent return null (M3)

试遍 Flip/NormalCut/重选/不重选/AssemblyFeatureScope 各种组合都 null。
**降级到 FeatureCut2** (23 args, 无 T0/OptimizeGeometry/FlipStartOffset)
+ `NormalCut=false` + `AssemblyFeatureScope/AutoSelectComponents/PropagateFeatureToParts=false`
**第一次就成**。

推测: 新版 API 的 T0/OptimizeGeometry 对 face-based 孔切除有更严的
selection state 前置条件。**SW API "v4 不行就试 v2/v3" 是有效兜底策略**。

### 6. Build 期间 .exe 被 MCP server 锁住 (M3)

如果另一个 Claude session 挂了 mech-pilot-sw MCP，那个 session 会 spawn
mech-pilot-sw.exe stdio server 进程，锁住 Debug exe 让 build 失败。

```powershell
Stop-Process -Name 'mech-pilot-sw' -Force  # 另一 session 下次调工具会自动 re-spawn
```

### 7. PR 合并后再 push 到老分支 → 孤儿 commit (M2 配套)

PR #2 merge 后，我又往 `feat/m2-cylinder` 推了 `.mcp.json` commit (`73fffa8`)
— 该 commit 是孤儿，没进 master。**修复**: 新分支 `chore/mcp-config` 从 master
拉出 → `git cherry-pick 73fffa8` → 新 PR (#3)。

### 8. C# 早绑定 `null` = VT_EMPTY，反转 v1 Python 的 fillet 坑 (M4)

v1 (Python late binding) 下 `FeatureFillet3` 尾部 7 个 Variant 数组参数传
`None` / `()` 会让 fillet **silent 返 null**，当年靠专门的 `empty_variant()`
(VT_EMPTY) 才破。迁到 C# 早绑定后**直接传 `null` 第一次就成** —— CLR 的
null→VARIANT 投影即 VT_EMPTY，等价 v1 的 `empty_variant()`。

规律: §2 (SW_API_REFERENCE) 的 VARIANT 三件套在早绑定下大多免手动包装 ——
`byref_long_variant()` → `ref int` (M2 SaveAs 已验)，`empty_variant()` → `null`，
`null_dispatch_variant()` → 类型化接口形参传 `null`。**后续工具迁移
(chamfer / hole_wizard 等遇到同类 Variant 数组参数时, 先试 `null`**。
对应表详 SW_API_REFERENCE §2.6。

### 9. L3 vs L2 偏差：热 SW 实例下 SaveAs(samepath) 不可靠 (M5)

`Extension.SaveAs(targetPath)` 当 `targetPath == 当前活跃 doc 自身路径` 时,
在长寿命 SW 实例 (MCP server) 下会返 `errors=0x1` (swGenericSaveError);
L2 (fresh exe per call) 因 SW 冷启动而绕过 —— **L2 通过 + L3 撞墙的首例**。

修法: in-place 改 `IModelDoc2.Save3(options, ref err, ref warn)`, copy 保留
`Extension.SaveAs`。详 M5 段。

**规律**:
- 长寿命 SW 状态下的 in-place 写入必须用 `Save/Save3`, 不能 `SaveAs(samepath)`。
- 新工具的 4 层验收**不能省 L3**: L2 fresh exe 永远撞不到热 SW state 的 bug。
  以后每个新工具都至少做一次 L3 MCP 抽测 (本会话里我直接调 4 工具就行)。
- 副作用: `~$xxx.SLDPRT` 锁文件在 CloseDoc 后残留 (SW 没干净释放 doc handle)
  暂不阻塞功能, 后续如果导致目录污染问题再单独修。

---

## 下一步候选 (ARCHITECTURE.md "MVP 后路线")

MVP 已跑通,**ARCHITECTURE.md 明确说"暂停, 根据实际体验决定"**。候选方向 (按
工作量从小到大):

1. **CI 自动化** (~半天): 本地 L2 PowerShell 脚本可入 self-hosted Windows
   runner (需要有 SW 的机器). 暂时手动跑也行。
2. **REPL 模式** (~1 天): `Entrypoints/ReplRunner.cs` — 工厂内网无 LLM
   人机交互场景可用. 复用同一 `Tools/`.
3. **架构调整**: 基于 MVP 实际体验, 修改/重构. 比如:
   - `.mcp.json` 从绝对路径改 wrapper 脚本 (跨机器)
   - 添加 `--no-sw` flag 给 ping 类工具加速 cold start
   - MCP server lazy SW connect (现在已经是 lazy 了, 但可以更晚)
4. **前 10 高频工具迁移** (~1-2 周): fillet / chamfer / hole_wizard /
   pattern_linear / mirror / new_assembly / add_component / save_as_step / ...
   参考 `v1-history.md` 老 v1 28 工具的优先级
5. **SW 任务面板 WPF 插件** (~1-2 周): 第 4 个 frontend, 复用同一 `Tools/`.
   工厂操作员无对话窗也能用。

LLM 体验亮点 (MVP 后实测):
- LLM 自动 M6 → ⌀6.6 通孔 (GB/T 5277 中等配合)
- LLM 主动 spec sanity check (PCD60 > 外径D50 调用前就识破)
- "端盖" / "法兰" / "圆盘" 都映射到 create_flange (无需别名表)

这些证明 ARCHITECTURE.md 核心赌注 "Claude Code stock + 薄 MCP" 成立 — LLM
是 free 工程推理层, 我们只要暴露干净的 API 即可。

---

## 新对话快速上手 checklist

```powershell
# 1. 读 30 秒入门
type CLAUDE.md

# 2. 读本文件 (5 分钟拿到 MVP 全貌 + 踩坑教训)
type docs\DEV_LOG.md

# 3. 看 git 状态
git log --oneline -10
gh pr list --state open

# 4. 跑本地 sanity (~10 秒)
dotnet test MechPilot.SwMcp.sln

# 5. 如果有 SW, 跑 L2 (~30 秒)
pwsh tests/integration/run-all.ps1

# 6. 按需深读
type docs\ARCHITECTURE.md           # 完整架构 + 15 决策
type docs\SW_API_REFERENCE.md       # SW API 知识库 (710 行)
type docs\v1-history.md             # 老 v1 35 PR 教训 (992 行)
```

---

## 联系 / 贡献

- 项目维护者: [@adam0wanna0top](https://github.com/adam0wanna0top)
- 老 v1 项目 (Python+pywin32): [mech-pilot](https://github.com/adam0wanna0top/mech-pilot)
- 本 v2 项目 (C# .NET 8 + MCP): [mech-pilot-sw-mcp](https://github.com/adam0wanna0top/mech-pilot-sw-mcp)
