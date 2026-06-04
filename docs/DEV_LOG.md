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
| create_rectangular_block | #13 | `mcp__mech_pilot_sw__create_rectangular_block` | 长方体零件 (L×W×H 居中) |
| pattern_linear | #14 | `mcp__mech_pilot_sw__pattern_linear` | 1D / 2D 线性阵列特征 |
| add_threaded_hole | #15 | `mcp__mech_pilot_sw__add_threaded_hole` | GB 螺纹孔 M3-M12 (真螺纹特征) |
| add_counterbore | #16 | `mcp__mech_pilot_sw__add_counterbore` | GB/T 152.3 柱形沉头孔 M3-M12 |
| add_countersink | #16 | `mcp__mech_pilot_sw__add_countersink` | GB/T 152.2 锥形沉头孔 M6-M12 |
| new_assembly | #18 | `mcp__mech_pilot_sw__new_assembly` | 创建空装配体 (.sldasm) |
| add_component | #18 | `mcp__mech_pilot_sw__add_component` | 把零件/子装配体插入装配体 |
| inspect_assembly | #19 | `mcp__mech_pilot_sw__inspect_assembly` | 读取装配体组件列表（实例名 / 位置） |
| add_mate_coincident | #20 | `mcp__mech_pilot_sw__add_mate_coincident` | 两组件 reference plane 重合配合 |
| add_mate_distance | #21 | `mcp__mech_pilot_sw__add_mate_distance` | 两组件 reference plane 间距 N mm 配合 |
| add_mate_concentric | #23 | `mcp__mech_pilot_sw__add_mate_concentric` | 两组件轴向 ±Z 圆柱面同轴配合 |
| (.mcp.json) | #3 | — | Claude Code 项目级 MCP 配置 |

**L1/L2/L3 全部验证**：298/298 单元测试 + 13 个 PowerShell L2 集成 +
**L3 全 13 工具抽测 zero bug** (M15/PR #17 沉淀)。add_fillet 撞出 in-place
SaveAs bug 已修 (M5/PR #7)。**M14 抽出 `Tools/Internal/PartGeometryHelpers`**
给 8 工具共用 (`FindPlanarEndFace` + `FindLastUserFeature` + `IsBootFeature`)。

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

### M11 — add_rectangular_block (PR #13, 2026-05-31) — 长方体造型 + pattern_linear 前置

"前 10 高频工具" 第 7 发。**为 pattern_linear 铺路** — M10 design 阶段发现
cylinder/flange 全是圆形无直边, FeatureLinearPattern2 的 mark=1 方向边没东西
选。**先造长方体补全直边**, 然后 M12 pattern_linear 才有 seed。

- **复刻 cylinder 模板, 唯一差异是 sketch 原语**:
  `CreateCenterRectangle(x1,y1,z1, x2,y2,z2)` (6 args), 中心 + 一角的语义。
  Center 在原点 (0,0,0); 角点 (L/2, W/2, 0) 米。pipeline 跟 cylinder 完全一样:
  NewDocument → SelectFrontPlane → InsertSketch → CreateCenterRectangle →
  ExitSketch → SelectSketch → FeatureExtrusion3 → SaveAs → CloseDoc。
- **三维独立**: LengthMm (X) / WidthMm (Y) / HeightMm (Z 拉伸深度), 每个独立
  bound `[0.1, 10000]`, 同 CylinderSpec sanity。
- **测试反向验证**: M11-block L2 跑完 create-rectangular-block 之后**立刻调
  inspect-part** 验 bbox sorted = (20, 50, 100), 即"造完真长这么大"——
  inspect_part 作为基础设施开始发挥跨工具回归价值。
- **测试**: L1 +24 RectangularBlockSpec 用例 (= 187 total); L2 M11 4/4 pass
  (bracket 100×50×20 / cube 30³ / 拒负 length / 拒过大 width)。L3 待新 session
  重启。

**模板成熟度沉淀**: 这是第 4 个 "modeling from-scratch" 工具 (cylinder /
flange / 现在的 block)。脚手架已经稳到 1:1 复刻 cylinder ~30 分钟出工具,
唯一变量是 sketch 原语 (Circle / 多 circle + cut / CenterRectangle)。后续如果
做 hexagonal block / triangular block 等都是这个模板。

**意义**: 10 工具 = 造 (4: 圆柱/法兰/方块/球未来) + 改 (4) + 看 (1) +
出货 (1)。10 工具里程碑达成 (虽然不是原计划的"前 10 高频", 但覆盖度更高 —
跳过 pattern_linear 改做 mirror + block 反而更贴 LLM 实际场景)。下个候选
(剩 3/10): **pattern_linear (现在 block 有直边)** / HoleWizard5 真螺纹 /
new_assembly+add_component / save_drawing。

### M12 — pattern_linear (PR #14, 2026-05-31) — 1D/2D 线性阵列 + axis 关键字策略

"前 10 高频工具" 第 8 发。**M11 block 铺好直边后, 立刻做 M10 跳过的
pattern_linear**。LLM "3×5 阵列" 一句话可达。

- **`FeatureLinearPattern2` (9 args 最简版)**: Num1, Spacing1, Num2, Spacing2,
  FlipDir1=false, FlipDir2=false, DName1="", DName2="", GeometryPattern=true。
  GeometryPattern=true 是稳健默认 (纯几何 copy, 避免参数化阵列在 multi-cut
  场景挂, 跟 v1 PR #32 pattern_circular silent fail 经验呼应)。
- **关键 LLM 设计: axis 关键字 (x/y/z) 而非 edge name**:
  LLM 不可能知道 SW 内部 edge name; 工具内部用 **body 导航 + IsLine +
  vertex 差值** 找第一条沿 ±axis 的直边 (cos similarity > 0.99)。具体:
  `IPartDoc.GetBodies2 → IBody2.GetEdges → IEdge.GetCurve.IsLine →
  endVertex.GetPoint - startVertex.GetPoint → normalize → 检查 axis 分量`。
  比 ICurve.get_LineParams 内部布局更可靠 (M2 教训: 不要猜 Variant 布局)。
- **Selection mark 三件套 (SW_API_REFERENCE §6)**:
  - direction edge 1 → mark=1
  - direction edge 2 → mark=2 (可选, 仅 CountDir2 > 1)
  - seed feature → mark=4
- **Seed feature 选择**: 同 mirror_feature 模式 — FeatureName 给了用
  SelectByID2("BODYFEATURE"); 没给则自动取最后一个 user feature
  (boot filter 现在第 3 次复用 — `bootTypes ∪ EndsWith("Folder")`, **下次出现
  应该 extract 到 shared helper, rule of three 已满**)。
- **Cylinder rejection 是 feature 不是 bug**: cylinder/flange 没直边, 工具检测
  到给清晰错误 "use create_rectangular_block as seed part" 而不是 silent fail。
  这是 M10 design 阶段发现的盲区**在工具里固化为 LLM 友好的引导**。
- **PowerShell 5.x × 编码坑 (L2)**: tool 输出 message "3×2 grid", PowerShell 5.x
  捕获 stdout 时 × (U+00D7) 渲染成 "?" 乱码, 导致正则 `'3×2'` match 失败。
  L2 case 改用 ASCII 关键字 `'grid'` 匹配, 添加 comment 说明。tool 本身输出
  没问题, 只是 L2 测试编码问题。
- **测试**: L1 +31 LinearPatternSpec 用例 (= 218 total); L2 M12 5/5 pass
  (1D x×3 spacing 20 / 2D 3×2 (x:25, y:15) / cylinder 拒 (无直边) / 拒
  axis 'w' / 拒 count=1)。L3 待新 session 重启。

**意义**: 11 工具 = 造 (4) + 改 (4) + **阵列 (1)** + 看 (1) + 出货 (1)。
LLM 业务 80%+ 模式覆盖到位 (造任意形 → 改 → 阵列 → 镜像 → 看 → 出货)。
下个候选 (剩 2): HoleWizard5 真螺纹 / new_assembly+add_component / save_drawing。

### M13 — add_threaded_hole (PR #15, 2026-05-31) — v1 PR #24 "魔法位"一次过

"前 10 高频工具" 第 9 发。**HoleWizard5 GB 螺纹孔 — v1 PR #24 啃过的最硬骨头,
踩坑沉淀的最强证据**: 1:1 复刻 v1 录宏破得的 27 参 + 4 个魔法位, C# 早绑定
**一次跑通 L2 5/5 pass**, 没有 silent fail 探测阶段。

- **HoleWizard5 27 参 (反射确认)**: GenericHoleType / StandardIndex /
  FastenerTypeIndex / SSize / EndType / Diameter / Depth / Length / Value1-12
  / ThreadClass / 6 个 bool。
- **GB tap 4 大魔法常量 (v1 PR #24 录宏破)**:
  - FastenerType = **359** (CHM 不公开, swStandardGBFastenerTypes_e 枚举 int 值未文档化)
  - Value7 = Value8 = **1.0** (feature enable flag)
  - Value11 = Value12 = **-1.0** (SW 默认占位 sentinel)
  - Value3 = **π/1.8 ≈ 1.7453** (沉头角默认 100°; tap 没沉但 SW 内部仍要)
- **GB_TAP_TABLE 7 规格 (GB/T 196-2003)**: M3 (drill 2.5, pitch 0.5), M4 (3.3,
  0.7), M5 (4.2, 0.8), M6 (5.0, 1.0), M8 (6.8, 1.25), M10 (8.5, 1.5),
  M12 (10.2, 1.75)。tap drill 直径 ≠ 螺纹标称直径。
- **Position 简化**: 跟 v1 一致, **固定 face 中心**, 不接受 (x, y)。多孔需求
  用 pattern_linear 组合 (single 螺纹孔 + pattern), 或 create_flange (PCD 圆周
  孔, 但那是 clearance 不是 thread)。Off-center HoleWizard 是 future PR。
- **复用 +Z 面选择 helper**: `FindPlanarEndFace` 第 3 次出现 (create_flange,
  add_axial_hole 已用)。**Rule of three 已满, 跟 boot filter 一起标记为下次新
  工具的 refactor 起点**。
- **测试**: L1 +34 ThreadedHoleSpec 用例 (= 252 total, GbTapTable 7 entry +
  thread/depth/path); L2 M13 5/5 pass (M6 through copy / M4 blind 5mm in-place /
  inspect 确认特征加上 / 拒 M7 / 拒不存在)。L3 待新 session 重启。

**踩坑沉淀价值证明**: 这是项目首个**完全靠 docs 知识库一次过**的工具 — 没有
反射多个版本签名探测, 没有"v1 fail → v2 试试 → v3 终于成"的迭代, 没有
silent fail 调试。直接 1:1 复刻 SW_API_REFERENCE §6 + v1-history PR #24 段
落里写好的模板, 跑通。**v1 35 PR 教训库的 ROI 在这次最高**。

**意义**: 12 工具 = 造 (4) + 改 (4 含螺纹孔) + 阵列 (1) + 看 (1) + 出货 (1)。
LLM 现在能"加 GB M6 螺纹孔"真螺纹特征 (不再用 add_axial_hole + LLM 自算 Φ5
螺纹底孔的间接路径)。进度 9/10, 剩 1: new_assembly+add_component (装配家族) /
save_drawing (工程图) / pattern_circular (圆周阵列)。

### M14 — refactor + add_counterbore + add_countersink (PR #16, 2026-05-31) — **10/10 完成**

"前 10 高频工具" **第 10 发, 项目原计划闭环达成**。三件套 PR:
1. **Refactor**: 抽 `Tools/Internal/PartGeometryHelpers` 给 8 工具共用
   - `FindPlanarEndFace(model)` — 之前 CreateFlange / AddAxialHole /
     AddThreadedHole 三处重复 (rule of three 已满)
   - `FindLastUserFeature(model)` — 之前 MirrorFeature / PatternLinear 两处
     +InspectPart 内联一份, 合并到 helper
   - `IsBootFeature(typeName)` — boot filter (`bootTypes ∪ EndsWith("Folder")`)
     的判别函数, InspectPart 也复用
2. **add_counterbore** (M3-M12) - GB/T 152.3 柱形沉头孔, 内六角圆柱头螺钉用
3. **add_countersink** (M6-M12) - GB/T 152.2 锥形沉头孔, 沉头螺钉 90° 角用
   (M3/M4/M5 SW 内部 DB 缺失, spec 拒绝)

**HoleWizard5 三兄弟 Value 模板对比 (per-hole-type, 不通用!)**:
| Hole type | FastenerType | 关键 Value 位 |
|---|---|---|
| GB Tap (M13) | 359 | Value3=π/1.8, Value7=Value8=1.0, Value11=Value12=-1.0; Value1=depth, Value2=pitch |
| GB CounterBore (M14) | 361 | Value1=cb_dia, Value2=cb_depth, Value4=1.0, Value6=cb_dia+0.05mm, Value7=π/1.8 |
| GB CounterSink (M14) | 363 | Value1=cs_dia, Value2=π/2 (90°), Value4=1.0, Value10=Value11=Value12=-1.0 |

**v1 PR #25 模板复刻 zero-试错** (跟 M13 一样): L2 5/5 pass 一次过。
**v1 35 PR 教训库的累计 ROI 再次验证** — 3 个 HoleWizard5 路径全用 docs 一次复
刻成功, 项目首次出现 "工具 N+1 比工具 N 还快"的反向递增曲线。

- **测试**:
  - L1 +46 (= 298 total): CounterboreSpec 18, CountersinkSpec 23, refactor 不
    影响既有 252。
  - L2 M14-sinks 5/5 pass: M6 CB through copy / M4 CB blind in-place / M8 CSK
    through in-place / 拒 M3 CSK (SW DB 缺) / 拒 M7 CB。
  - L2 M9 inspect + M10 mirror 回归 pass (验证 refactor 不破 helper 调用方)。
  - L3 待新 session 重启。

**意义**: 14 工具 = 造 (4) + 改 (6 含 3 路径 HoleWizard5) + 阵列 (1) + 看 (1) +
出货 (1)。LLM 现在能全套表达 "M6 内六角沉头螺钉孔" / "M8 沉头螺钉孔" 真特征,
不再用 add_axial_hole + LLM 自算的间接路径。**10/10 高频工具完成**, 项目从
"建模工具集"完整过渡到 "LLM-friendly SW 操作链路"。

下一步候选: new_assembly + add_component (装配家族) / save_drawing (工程图 PDF) /
pattern_circular (圆周阵列, v1 PR #32 修过 silent fail) / L3 全 10 工具抽测 /
CI self-hosted runner。

### M15 — L3 全 13 工具抽测 zero bug (PR #17, 2026-05-31) — 工具链质量曲线验证

**PR #16 合入后 (10 工具 L3 积压验证), 新会话一次性抽测 13 工具 zero bug。**
对应 M5 那次 L3 撞 in-place SaveAs bug 之后, 工具链质量首次"完整链路通过"的
节点性事件。

- **抽测覆盖**: 13 次 MCP 工具调用, 涵盖 14 工具中除 create_flange (M3 已 L3
  验过) 之外的全集:
  - ping, create_cylinder, create_rectangular_block (×5: 5 个独立 block 避
    hole 冲突), add_chamfer, add_axial_hole (×3), add_threaded_hole,
    add_counterbore, add_countersink, mirror_feature, pattern_linear,
    inspect_part (×3 跨工具回归), export_part
- **关键观察 (vs M5 撞 bug 那次)**:
  | | M5 L3 抽测 (PR #7 前) | M15 L3 全 10 工具 |
  |---|---|---|
  | 调用次数 | 6 | **13** |
  | 撞到 bug | 1 (in-place SaveAs 0x1) | **0** |
  | `~$xxx.SLDPRT` 锁文件残留 | 2 | **0** |
  | SW 进程内存 (终态) | n/a | **680MB** (健康) |
  | M9/M10/M14 refactor 后回归 | n/a | **L2 + L3 都不破** |
- **HoleWizard5 三兄弟 (M13/M14) L3 验证**: GB Tap M5 / GB CB M6 / GB CSK M8
  全过, 真特征出 (drill+pitch / clearance+CB / clearance+CSK 数值符合 GB 表)。
  **v1 PR #24/#25 模板的 zero-试错复刻在 L3 长寿命 server 上也成立** (不只是
  L2 fresh exe)。
- **M14 refactor 不破 helper 调用方**: 8 工具调用
  `PartGeometryHelpers.FindPlanarEndFace` / `FindLastUserFeature` /
  `IsBootFeature` 后, L3 行为跟 refactor 前一致。inspect featureCount /
  feature typeName / bbox 全准。
- **锁文件残留消失**: M5 段记录的 "CloseDoc 后 `~$xxx.SLDPRT` 残留" 现象本次
  **未复现**。原因推测: M14 helper 抽取后调用更紧凑 / SW 工具间隔合理 /
  finally CloseDoc 全工具覆盖。该现象暂时降级为 "偶发不阻塞"。

**启示**:
1. **L3 不是"可选"而是"质量收口"**: M5 撞到的 in-place SaveAs bug 在 L2 fresh
   exe 永远撞不到, 必须 L3 长寿命 server 验。本次 zero bug 是工具链质量曲线
   显著提升的硬证据。
2. **v1 知识库 ROI 持续**: M13/M14 三个 HoleWizard5 路径 zero-试错复刻 → L3
   也 zero-bug 通过, 验证 docs 沉淀的复利效应。
3. **新工具 L3 抽测应进 CLAUDE.md 黄金法则**: 工作流第 4 步 "MCP 抽测" 应该
   从 "L3/L4 待抽测 (代码同构略过)" 升级为 "每个新工具 L3 必抽 1 次"。本 PR
   附带把这条规律写进 CLAUDE.md。

下个候选: new_assembly+add_component (装配家族) / save_drawing (工程图) /
pattern_circular / CI self-hosted runner。

### M16 — new_assembly + add_component (PR #18, 2026-05-31) — 装配家族开张

**项目从"单零件建模"扩到"装配体组装"** — 14 工具 → 16 工具, 新增装配工具家族
(2 个工具)。v1 PR #9 经验复刻 zero-试错 L2 一次过。

- **NewAssemblyTool**: NewDocument(asmdot, ...) → SaveAs(.sldasm) → CloseDoc。
  跟 CreateCylinderTool 同模板, 只差模板路径 (`swDefaultTemplateAssembly = 8`
  vs `swDefaultTemplatePart = 9`)。
- **AddComponentTool** — **v1 PR #9 关键教训**: `IAssemblyDoc.AddComponent5`
  **不会自动加载组件文件**, 直接调返回 null。**Workaround**: 先 `OpenDoc6`
  预加载零件到 SW 内存, 再 AddComponent5。Pipeline:
  1. OpenDoc6 assembly (Silent, R/W)
  2. **OpenDoc6 component 预加载** (Silent; .sldprt → swDocPART, .sldasm → swDocASSEMBLY)
  3. `swApp.ActivateDoc3(asm.GetTitle(), ...)` 重新激活装配 (component
     OpenDoc6 会切到 component 当 active)
  4. `asmDoc.AddComponent5(componentPath, 0, "", false, "", x_m, y_m, z_m)`
  5. `asmModel.Save3(...)` (M5 lesson: in-place 用 Save3)
  6. CloseDoc(component) + CloseDoc(asm) 在 finally
- **`AddComponent5` 签名 (反射确认)**:
  `Component2 AddComponent5(string CompName, int ConfigOption, string NewConfigName,
   bool UseConfigForPartReferences, string ExistingConfigName, double X, double Y, double Z)`
  ConfigOption=0 = "use default config", 其他 string + bool 留默认。
- **L2 .sldasm 文件大小非线性观察**: empty asm 35KB, 加 cyl 后 72KB (+37KB),
  加 block 后 67KB (-5KB)。**SW 的 .sldasm 是内部二进制压缩格式, 加组件后
  整体可能重新打包**。L2 断言改成 "stays > empty" 而非 "strict growth"。
- **测试**:
  - L1 +25 (= 323 total): NewAssemblySpec 5, AddComponentSpec 20
  - L2 M16-assembly 5/5 pass: empty → +cyl → +block @ (50,0,0) → 拒不存在 asm
    → 拒 .sldprt save-as-assembly
  - L3 待新 session 抽测 (黄金法则 #13)

**意义**: 16 工具 = 造 (4) + 改 (6) + 阵列 (1) + 看 (1) + 出货 (1) + **装配 (2)**
+ ping。LLM 现在能 "造零件 → 加孔 → 组装到装配体" 完整链路。装配家族开张, 下个 PR
候选: add_mate (距离/同轴/重合配合) / save_drawing (工程图) / pattern_circular。
v1 PR #20 在 mate 上有 "distance mate 经 AddMate5 而非 CreateMate" 的精确经验,
可继续复用 zero-试错复刻策略。

### M17 — inspect_assembly (PR #19, 2026-05-31) — 给 LLM 加 mate 前的"眼睛"

**Design pivot 跟 M10 pattern_linear 同款** — 原计划做 add_mate, 反射 + design 阶段
发现 LLM 盲区:

- `IAssemblyDoc.CreateMate` / `AddMate5` 需要 select 两个组件的 face/edge/plane
  作为 mate references。LLM 不知道 SW 内部 face name (跟 cylinder/flange 没直边
  导致 pattern_linear 用不上是同样盲区)。
- v1 PR #19 在 PR #20 add_mate **之前**先做了 `inspect_assembly`, 这是有意为之的
  顺序: 先让 LLM 看见组件实例名 (`hub-1` / `pin-2`) 和位置, 再 add_mate 用 LLM
  拿到的名字做 mate。**跳过 inspect_assembly 直接做 add_mate 会撞 M10 同样盲区**。
- 改做 M17 = inspect_assembly (只读, 复用 inspect_part 模式), M18 再做 add_mate。

实现 (复用 inspect_part 的 Open(ReadOnly) → walk → Close + ToolResult.Data 模式):
- `IAssemblyDoc.GetComponents(true)` 拿 top-level components (Component2[])
- 每个 `IComponent2`:
  - `get_Name2()` → 实例名 (e.g. "asm_cyl_1937631041-1", 带 SW 自动加的 -1 后缀)
  - `GetPathName()` → 源 .sldprt/.sldasm 绝对路径
  - `GetXform()` → 4×4 transform 矩阵 (16 doubles); [9..11] 是 translation X/Y/Z
    (米, × 1000 转 mm)
  - `IsSuppressed()` → bool

**关键发现 (positionMm 是 frame origin, 不是 centroid)**:
M17 L2 跑 `add_component(asm, cyl_L30, 0, 0, 0)` 后 inspect 看到 `positionMm.z = -15`
(不是 0)。**SW 的 AddComponent5 把组件几何中心 anchor 到指定位置**, 而组件 **frame
origin** 是零件的 sketch 原点 (Front Plane / z=0 端面), 跟几何中心差 height/2。
所以:
- cyl L30 → frame origin z = -15 (centroid 在 z=0)
- block H10 → frame origin z = -5

X / Y 直接匹配 add_component 输入 (SW placement 直接), 只有 Z (拉伸方向) 有这个偏移。
LLM 应理解 positionMm 是 frame origin, 不是 centroid。tool docstring 写清这个细节。

- **测试**: L1 +8 InspectAssemblySpec (= 331 total); L2 M17 5/5 pass:
  - 空 asm → 0 components ✓
  - 2-comp asm → 2 components, 含实例名 + sourcePath + positionMm ✓
  - 拒不存在
  - 拒 .sldprt + 提示 LLM 用 inspect_part
- L3 待新 session 抽测 (黄金法则 #13)

**意义**: 17 工具 = 造 (4) + 改 (6) + 阵列 (1) + 看 (**2**: inspect_part + inspect_assembly)
+ 出货 (1) + 装配 (2) + ping。M18 add_mate 现在有了"前置眼睛", LLM 可以 inspect_assembly
拿组件实例名 → add_mate 用这些名字 mate components。

### M18 — add_mate_coincident (PR #20, 2026-05-31) — 装配核心约束能力

**v1 PR #20 模板 zero-试错 L2 一次过 — 4 大魔法位规律继续验证**。组件间真正约束
起来 (不只摆位置), LLM "底面贴合 / 端面对齐" 一句话可达。

- **Scope 简化**: 只做 coincident-of-reference-planes (最简、最高频, 覆盖 ~80%
  LLM 装配请求)。concentric / distance / parallel 下个 PR 扩展。
- **v1 PR #20 模板 (AddMate5 路径)**:
  - `IAssemblyDoc.AddMate5` 15 args (反射确认)
  - 4 大魔法位 — 全设 0 会让 AddMate5 silent fail (v1 PR #20 录宏破得):
    - `GearRatioNumerator = GearRatioDenominator = 0.001` (非零)
    - `AngleAbsUpperLimit = AngleAbsLowerLimit = π/6` (≈30°, 非零)
  - Reference plane selection: mark=0 (v1: "distance / AddMate5 路径用 mark=0,
    CreateMate 路径用 mark=1")
  - Plane selection name 格式: `"{PlaneAlias}@{ComponentInstance}@{AsmTitle}"` —
    e.g. `"Front Plane@cyl-1@asm_42"` (asm title 须 strip ".SLDASM" ext)
- **新 micro-lesson: AddMate5.ErrorStatus 用 `out` (不是 `ref`)**:
  Build error CS1620 直接强制告诉我。规律 (vs M2 SaveAs 教训):
  - COM `[in, out]` 参数 (如 SaveAs.Errors) → C# `ref`
  - COM `[out]` only 参数 (如 AddMate5.ErrorStatus) → C# `out`
  以前所有 SW Interop ref 都是 [in,out] (SaveAs/Save3/FeatureCut2 末位
  errors/warnings 都是)。AddMate5 是首个 `out` only 的。**新工具反射看到
  Int32& 后 build 让 compiler 报错确认 ref/out**。
- **Plane 重合 + alignment 三选 (alignmnet = aligned/anti-aligned/closest)**:
  swMateAlign_e 枚举映射。LLM 用 keyword 不用 enum 数值。
- **拒 self-mate (同组件做 mate)**: spec.Validate 显式检查
  `component1Name != component2Name`, 防 LLM 误传。
- **测试**: L1 +26 CoincidentMateSpec 用例 (= 357 total); L2 M18 5/5 pass
  (front@cyl ↔ top@block aligned in-place / 拒 self-mate / SW 层拒不存在组件 /
  拒 'bottom' 关键字)。L3 待新 session 抽测。

**意义**: 18 工具 = 造 (4) + 改 (6) + 阵列 (1) + 看 (2) + 出货 (1) + **装配 (3:
new_assembly + add_component + add_mate_coincident)** + ping。LLM 现在能完整
表达 "造零件 → 加孔 → 组装 → mate 约束", 装配能力闭环达成。

下个候选: add_mate_concentric / add_mate_distance (扩展 mate 家族) /
save_drawing (工程图 PDF) / pattern_circular。

### M19 — add_mate_distance (PR #21, 2026-05-31) — mate 家族扩展 (距离配合)

**复刻 M18 模板 zero-试错 L2 一次过**。新增第 2 类 mate, mate 家族开始覆盖
LLM 装配高频场景。**v1 PR #20 的 AddMate5 + 4 大魔法位经验在本 PR 第 2 次
复利验证** (M18 是首次)。

- **跟 M18 唯一差异**: spec 加 `DistanceMm` 字段; tool 把
  `MateTypeFromEnum=COINCIDENT` 改成 `DISTANCE`, Distance / Upper / Lower
  Limit 三个字段都设为 distance_m (锁定单值, 不留范围 — LLM 用单一距离)。
- **共享 helpers**: PlaneAliases + AlignmentKeywords 直接用 CoincidentMateSpec 的
  静态字典 (rule of three 还没满 — 等 M20 concentric 出来如果也要用 plane 关键字
  就 extract; 否则 mate 家族 plane keyword 表只有 coincident + distance 2 处)。
- **alignment = 'closest' 在 L2 用**: 对已经放好位置的组件做 distance mate,
  'aligned' 可能让 SW 想把组件翻过来导致冲突; 'closest' 让 SW 选不需要翻转的
  那侧, 鲁棒性最好。Tool docstring 写明这条对 LLM 的引导。
- **测试**: L1 +16 DistanceMateSpec (= 373 total); L2 M19 5/5 pass:
  - 25mm distance top@cyl ↔ top@block (closest) in-place ✓
  - 拒 distance ≤ 0
  - 拒 self-mate
  - 拒 'bottom' plane 关键字
  - 1 sanity
- L3 待新 session 抽测 (黄金法则 #13)

**意义**: 19 工具 = 造 (4) + 改 (6) + 阵列 (1) + 看 (2) + 出货 (1) + **装配 (4:
new_assembly + add_component + add_mate_coincident + add_mate_distance)** + ping。
LLM "底面贴合" + "距离 25mm" 两种最常 mate 类型全覆盖。

**v1 PR #20 经验复利 — 6 个连续 PR zero-试错** (M13/M14×2/M16/M18/M19), 项目首
次 mate 家族出现 "复刻成本极低" 的递增曲线。

下个候选: add_mate_concentric (需要 cylindrical face selection, 不再是 plane,
设计上有新难点) / save_drawing / pattern_circular。

### M20 — add_component path-separator fix (PR #22, 2026-06-03) — **L3 抽测撞 bug**

**M16 add_component 第二次撞 bug 修复** — 跟 M5 模式同款 (L3 长寿命 server 撞,
L2 fresh exe 用 PowerShell Join-Path 路径过)。本次新增 path separator 维度,
是项目首个 "OS-canonical path normalization" 类教训。

- **撞 bug 现场**: L3 抽测装配家族, 用 forward-slash 路径调
  `add_component(asm=".../asm.sldasm", component=".../cyl.sldprt")`,
  MCP 返 "An error occurred", CLI 复现 `AddComponent5 returned null`。
- **诊断路径**:
  1. 怀疑 ActivateDoc3 hot SW 失效, 颠倒 OpenDoc6 顺序 — 没修好
  2. 用 PowerShell Join-Path 生成 backslash 路径再试 — **成功**
  3. forward-slash + 同 sequence → 失败
  → 锁定 path separator 是真因
- **根因**: `AddComponent5(CompName)` 第 1 参对 SW 内部 doc-table key **字符串
  精确比较**。SW 内部把 OpenDoc6 加载的 doc path **标准化成 OS-canonical 形式
  (Windows = `\`) 后 store**。LLM/MCP 自然用 `/` 路径 → OpenDoc6 成功 (SW
  接受) → 但 SW 内部 store 成 `\` → AddComponent5 找 `/` 字符串找不到 → 返 null。
- **修法**: tool 入口 `Path.GetFullPath(path)` 一次性 normalize 到
  OS-canonical 形式 (Windows 上把 mixed slashes → 全部 `\`, 不碰 filesystem)。
  传给 OpenDoc6 + AddComponent5 + Save3 + 错误消息 都用 normalized path。
- **L2 为啥 5/5 全过**: `M16-assembly.test.ps1` 用 `Join-Path $tmpDir "asm_xxx"`,
  PowerShell `Join-Path` 在 Windows 上**自动产 backslash 路径**。所以 L2 永远
  跑 backslash 测试, 永远撞不到 forward-slash 的 bug。**这是 v2 项目首次出现
  "L2 测试代码用法跟 LLM 用法不一致导致测试漏洞" 的教训**。
- **L3 抽测同步验证 (forward-slash 路径, 本分支从 PR #20 拉, M19 add_mate_distance
  在 rebase 后才进 master, 留下次 session 抽测)**:
  - new_assembly ✓
  - add_component ✓ 修后通过
  - inspect_assembly ✓
  - add_mate_coincident ✓ (复用 plane qualified name, 不受 path 分隔符影响)
  - add_mate_distance — rebase 后留下次抽测 (装配 5 工具中唯一未 L3 验过的)
- **测试**:
  - L1: 357/357 pass (spec 没动, normalize 在 tool 内部); rebase 后含 PR #21
    的 16 个 DistanceMateSpec 用例 = 373/373。
  - L2: M16-assembly 6/6 pass (回归不破 + **+1 forward-slash 回归 case 防退化**)
  - L3: 4 个装配工具实测通过 (用 forward-slash 路径)
  - dotnet format clean

**新规律 (黄金法则 #14)**: SW Interop API 若涉及"通过 path 字符串匹配已加载
doc" (典型如 AddComponent5 / AddComponents4 / 其他 path-based lookup), 工具
内部必须 `Path.GetFullPath()` normalize 输入路径。L2 应补一个 forward-slash 路
径的测试 case 防回归。

### M21 — add_mate_concentric (PR #23, 2026-06-03) — mate 家族最后一块

**v1 PR #20 经验复利 — 7 个连续 PR zero-试错** (M13/M14×2/M16/M18/M19/M20/M21)。
mate 家族补齐第 3 类 — coincident/distance/concentric 完整覆盖 LLM 95% 装配
mate 请求。

- **设计难点 (跟 M18/M19 不同)**: concentric 不是 plane-based mate, 而是
  **cylindrical face-based**。LLM 不知道 SW 内部 face name → 工具内部必须
  自动找轴向 ±Z 的圆柱面。
- **跨组件 cylindrical face 选择 (新模式)**:
  1. `IAssemblyDoc.GetComponents(true)` 找 component by Name2 (case-insensitive)
  2. `IComponent2.GetBody() → IBody2`
  3. `IBody2.GetFaces() → IFace2[]`
  4. 遍历: `IFace2.GetSurface() → ISurface`, 检查 `IsCylinder()`
  5. `ISurface.get_CylinderParams() → double[7]`:
     - [0..2] = root point on axis (m)
     - [3..5] = axis direction unit vector
     - [6]    = radius (m)
  6. 判断 `|cylinderParams[5]| > 0.99` (axis 沿 ±Z)
  7. 找到 → `IEntity.Select4(append, null)` + `IEntity.Select2(append, mark=0)`
- **AddMate5 路径同 M18/M19**: 4 大魔法位 (gear ratio 0.001, angle limits π/6)
  非零 + mark=0 + ErrorStatus out (M18 micro-lesson 复用)。MateType =
  `swMateCONCENTRIC`。
- **跟 LLM 友好的简化**: spec **只需 component1Name + component2Name**, 不需要
  face name / face index / axis 关键字。工具自动找。多 cylinder face 时 first
  one wins (假设单 component 通常只有 1 个 Z-axial 圆柱面)。**多 cylinder face
  选择留 future PR (faceIndex 字段)**。
- **测试**:
  - L1: +15 ConcentricMateSpec 用例 (= 388 total)
  - L2: M21 5/5 pass
    - 2 个 cylinder concentric (closest) in-place ✓
    - block (无 Z 轴 cylinder face) 正确拒绝 ✓
    - 拒 self-mate
    - SW 层拒不存在 component name
  - L3 待新 session 抽测 (黄金法则 #13)

**意义**: 20 工具 = 造 (4) + 改 (6) + 阵列 (1) + 看 (2) + 出货 (1) + **装配 (5:
new_assembly + add_component + add_mate_coincident + add_mate_distance +
**add_mate_concentric**)** + ping。**mate 家族完整**, LLM 装配能力达到 95%
business case 覆盖 (剩 parallel / tangent / lock 等小众类型)。

**v1 经验复利曲线**: 7 连击 zero-试错的统计 — 每个 v1 PR #20 复刻成本约 1 小时
(M18) → 40 分钟 (M19, 复刻成本最低) → 50 分钟 (M21, cylindrical face 新路径
但 spec/AddMate5/Save 板块全复用)。**项目首次形成"复利学习曲线"**。

下个候选: save_drawing (工程图 PDF/DXF) / pattern_circular / mate 家族 helper
refactor (SelectFirstPlane / MapAlignment / StripSldasmExt 在 M18+M19+M21 三处
复用 — rule of three 已满)。

### M22 — pattern_circular (PR #?, 2026-06-04) — v1 PR #32 真根因复刻 + 几何能力扩展开张

**继 PR #24 L3 收口后用户决策跳过 save_drawing, 优先做几何工具拓展** (用户问
"能不能画机械臂/电风扇" → 评估发现 90% 瓶颈在 MCP 工具层非 LLM 模型 → 选
pattern_circular / revolve 先做)。**复刻 v1 PR #32 真根因, 8 连击 zero-试错**
(M13/14×2/16/18/19/20/21/22)。

- **v1 PR #32 真根因 (回顾)**: `FeatureCircularPattern3.Spacing` 在
  `EqualSpacing=false` 时是**每 instance 间距**(不是总角度)。**正确公式**:
  `spacingRad = totalAngleRad / count`。v1 老代码传 `math.radians(360)`
  → 4 instance 全重叠原位置 → silent fail (老 L2 只验文件存在没数孔数所以一直
  误 PASS, 直到 trace 暴露)。`EqualSpacing=true` 让 feat 返 null, 必须 false。
- **Tool 设计 (LLM-friendly 简化)**:
  - Spec 极简: inputPath + count + totalAngleDeg(默认 360) + featureName? + outputPath?
    无 axis 关键字 — 所有 mech-pilot 拉伸件轴沿 ±Z, 工具自动找
  - Axis 自动选: 复用 M21 add_mate_concentric 的 `FindFirstAxialCylinderFace`
    模式 (CylinderParams[5] = axis.Z, |axis.Z| > 0.99) — **inline 一份, 第 2 次
    出现, 等第 3 次 (可能是 revolve / 角度 mate) 抽到 PartGeometryHelpers**
  - PR #35 multi-cut limitation 在 LLM-facing description 明确写: "若零件已
    有多个 cut features stacked → silent fail → 用 create_flange 代替"
- **API 路径 (反射验过)**:
  `FeatureCircularPattern3(Number, Spacing, FlipDirection, DName, GeometryPattern, EqualSpacing)`
  6 参; 用 mark=1 (axis face) + mark=4 (seed) 选择
- **L2 意外发现 (block + axial_hole 行为)**: 长方体钻孔后**有 axis-Z cylindrical
  face = 孔内壁**。`FindFirstAxialCylinderFace` 找到它, 选作 axis → SW 拿
  "孔自己作 axis pattern 孔自己" → silent fail (退化)。L2 case 4 改成**纯长方体
  (无孔)** 才能真测 "FindFirstAxialCylinderFace null-return" 路径。**LLM 用法**:
  block + 单孔的 pattern_circular 会失败, 但这是 SW 退化行为, 工具 best-effort
  给体面错误消息即可。
- **测试**:
  - L1: +33 CircularPatternSpec 用例 (= 421 total)
  - L2: M22 5/5 pass
    - full-circle 6× D40 cyl + Φ5@(10,0) (in-place) ✓
    - 180° arc 3× D40 cyl + Φ4@(8,0) (copy) ✓
    - empty cylinder (无 seed) 拒绝 ✓
    - pure block (无 cylindrical face) 拒绝 ✓
    - count=1 spec validation ✓
  - L3 待新 session 抽测 (黄金法则 #13)
- **dotnet format clean, build 0 warnings 0 errors**

**意义**: 21 工具 = 造 (4) + 改 (6) + **阵列 (2: linear + circular)** + 看 (2) +
出货 (1) + 装配 (5) + ping。LLM 现在能"飞轮 / 车轮 / 散热环 / 多孔法兰盘"
(PCD bolt circle 一句话) — 但 multi-cut 场景 (cyl + 中心孔 + 偏心孔) 仍走
create_flange 一次包死的路径。**几何能力扩展开张**, 下个候选 M23 revolve
(球/锥/旋转件) 解锁电风扇底座 / 喇叭口 / 漏斗等"非 prismatic" 几何。

**v1 经验复利 8 连击曲线**: M13 ~3h → M14 ~3h → M16 ~2h → M18 ~1h →
M19 ~40min → M21 ~50min → M22 ~1h (含 L2 block 发现)。**v1 35 PR 教训库
ROI 持续放大**。

### M23 — create_hemisphere (PR #?, 2026-06-05) — 首个 revolve 几何 + v1 PR #5 复刻 + 自主 LLM-friendly 设计

**几何能力扩展第二步, 首个非 prismatic (非拉伸) 几何工具**。继 M22
pattern_circular 后再下一城, 解锁球壳/球关节/球阀/电风扇底圆顶/球冠罩等
"半球类"零件。**9 连击 zero-试错** (M13/14×2/16/18/19/20/21/22/23)。

- **设计哲学决策 (跟 v1 不同)**:
  - v1 PR #5 做 "通用 revolve" (`feature.revolve(angle, reverse)` + LLM
    用 `draw_line + draw_centerline` 画 sketch) — LLM 画 sketch 认知负载高,
    容易撞 silent fail
  - M23 改做**参数化 helper** `create_hemisphere(diameter, savePath)` —
    跟 create_cylinder/create_flange/create_rectangular_block 同款"LLM 给参数,
    工具内部画 sketch"哲学。LLM 不需要懂 sketch / centerline / revolve angle。
  - **首次出现"v1 知识 + 反射 + 自主设计 = 做 v1 没做过的形式"**: 复刻 v1
    FeatureRevolve2 API 路径 (20 参 / mark=0 sketch / centerline 自动作 axis),
    但 spec 设计是新的 (LLM-friendly diameter helper)
- **几何 + sketch 设计**:
  - Front Plane (XY) 画 1/4 圆 profile + centerline:
    - Line: (0,0,0) → (R,0,0)         底面半径线
    - Arc: center (0,0,0), start (R,0,0), end (0,R,0), direction=1 (CCW)
    - Line: (0,R,0) → (0,0,0)         纵向轴线 (闭合到 origin)
    - CenterLine: (0,-2R,0) → (0,2R,0)  沿 Y 轴 (axis of revolution)
  - FeatureRevolve2: SingleDir=true, IsSolid=true, IsCut=false,
    Dir1Type=0 (Blind), Dir1Angle=2π, Merge=true, 其他 0/false (20 参共 11 个
    非零, 9 个零/false)
  - **半球 axis = +Y (故意不跟 cylinder 的 +Z 一致)**:
    - 故意选 Front Plane 因为 sketch X=世界 X / sketch Y=世界 Y **无歧义**
    - Right/Top Plane 的 sketch 坐标 ↔ 世界轴映射涉及 SW 内部 handedness,
      反射看不出来 — 选 Front Plane 避坑
    - LLM 不在意半球 axis 方向, 文档明确写就行; 装配场景需要 +Z 朝上可
      add_mate 旋转
- **FeatureRevolve2 20 参 (vs 文档 15 参)**: v1 PR #5 教训复用 — 反射拿真签名
  (黄金法则 #5), SW 2026 多 5 个尾部 Variant (`UseFeatScope` / `UseAutoSelect`
  + 3 个 ThinType 相关)。文档 15 参版本会编译失败。
- **L2 几何验证 (M22 收尾确立的 pattern 类工具几何验证模板复用)**:
  - bbox 60×30×60 mm (X×Y×Z) — **Y=D/2=30 confirms hemisphere** ✓
  - featureCount=2 (1 sketch + 1 Revolution) ✓
  - features 含 `typeName="Revolution"` ✓
  防 silent 假成功 (sketch 闭合错误但 SW 不报错 / centerline 没被识别)
- **测试**:
  - L1: +23 HemisphereSpec 用例 (= 444 total): diameter [0.1, 10000] +
    path validation; 23 个比 cylinder 少因为没 length 字段, 同 sanity 模式。
  - L2: M23 5/5 pass (含 inspect-part 几何验证 step)
  - L3 待新 session 抽测 (黄金法则 #13)
- **build 0 warnings 0 errors, dotnet format clean**

**意义**: 22 工具 = 造 (**5**: 圆柱/法兰/方块/**半球**+ 装配) + 改 (6) + 阵列 (2) +
看 (2) + 出货 (1) + 装配 (5) + ping。**首个非 prismatic 几何**, 几何能力曲线从
"只能拉伸"扩到"能旋转生成"。下一步候选: **create_frustum (圆锥台, 复用 M23
sketch+revolve 框架, ~半天)** 解锁锥/漏斗/机械臂关节 taper; create_sphere (整球,
M23 mirror, 半天); save_drawing (1-2 天); pattern_circular 真 multi-cut limit 验证。

**v1 经验复利 9 连击曲线** (新增 M23 ~1h): M13 ~3h → M14 ~3h → M16 ~2h →
M18 ~1h → M19 ~40min → M21 ~50min → M22 ~1h → **M23 ~1h** (含 sketch 设计 +
反射验签名)。**首次"v1 没做过的形式 zero-试错"**, 证明项目设计能力开始独立于
v1 经验 (v1 是基础, 不是天花板)。

### M24 — create_frustum (PR #?, 2026-06-05) — 复用 M23 sketch+revolve 框架 + SW sketch precision 发现

**几何能力扩展第三步, 第二个 revolve 工具**。复刻 M23 框架 (Front Plane sketch
+ Y-axis centerline + FeatureRevolve2 20 参), 只换 sketch 原语 (4 line 梯形
profile 代替 1 line + arc + 1 line 四分之一圆)。**10 连击 zero-试错**
(M13/14×2/16/18/19/20/21/22/23/24)。

- **跟 M23 同款"参数化 helper"哲学**:
  - LLM use: `create_frustum baseDiameter=60 topDiameter=30 height=40`
  - 内部: 4 line 梯形 (base radius / slant / top radius / axis closure)
    + Y 轴 centerline + FeatureRevolve2(360°)
  - 解锁: 漏斗 / 喇叭口 / 机械臂关节 taper / 喷嘴 / 散热翅片底座 / 沙漏分段
- **设计 spec 决策 (3 个 cross-field constraints)**:
  - `topDiameter < baseDiameter` 严格 — 相等 → 引导 LLM 用 create_cylinder
    (错误消息明确写); 大于 → 倒置 frustum 暂不支持
  - 3 个尺寸独立 [0.1, 10000] mm sanity bound, 但**SW sketch precision 实测
    更紧** (见下面 educated finding)
- **Educated finding — SW sketch precision lower bound**:
  L2 case 3 初版用 `topD=1mm` 测 near-cone, 撞 `CreateLine (top radius)
  returned null`。topR=0.5mm 的 line (0.0005m) **SW ISketchManager 内部拒绝**
  (推测原因: SW 内部 vertex merge / "tiny edge" rejection 阈值)。
  - **沉淀**: FrustumSpec 的 docstring 加经验 lower bound "LLM 用 topD ≥ 2-3 mm
    安全; 真 cone (topD=0) 等 future create_cone tool 用 degenerate-vertex
    sketch 处理"
  - L2 case 3 改成 "机械臂关节 taper" baseD40/topD20/H15 (更贴近用户目标场景)
  - Spec sanity bound 暂不缩紧 (0.1mm 保持, 因为 baseD/heightMm 这些大尺寸字
    段不撞 precision; 只 topD < baseD 时小尺寸有风险, 让 docstring 引导 LLM)
- **代码复用率高**:
  - CreateFrustumTool.cs 几乎 1:1 复刻 CreateHemisphereTool.cs 框架 (NewDocument
    → Front Plane → InsertSketch → 4 line + centerline → ExitSketch → Select
    Sketch1 → FeatureRevolve2(20 参) → SaveAs → CloseDoc)
  - 唯一变量: sketch 原语 (line/arc/centerline 顺序 + count)
  - FeatureRevolve2 调用参数全相同 (20 参 educated defaults)
- **测试**:
  - L1: +24 FrustumSpec 用例 (= 468 total): 3 个尺寸 sanity + topD<baseD
    cross-field + path validation
  - L2: M24 6/6 pass (含 inspect-part 几何验证, baseD60/topD30/H40 → bbox
    60×40×60 + Revolution feature; 机械臂 taper baseD40/topD20/H15; 3 个
    validation case)
  - L3 待 PR #28 merge 后**批量收口 M23 + M24** (M21 收尾模式: PR #24 一次抽
    distance + concentric 两个 mate 同款)
- **build 0 warnings 0 errors, dotnet format clean**

**意义**: 23 工具 = 造 (**6**: 圆柱/法兰/方块/半球/**圆锥台**+ 装配) + 改 (6) +
阵列 (2) + 看 (2) + 出货 (1) + 装配 (5) + ping。**Revolve 家族 2 工具齐**, sketch
+revolve 框架"模板化"验证 (复用率 95%, 只改 sketch 原语)。下一步候选: add_mate_angle
(机械臂关节摆角, 让机械臂"能摆动", 0.5 天 mate 家族复刻) / shell (薄壁电机壳,
1 天) / create_sphere (整球, M23 mirror, 半天) / save_drawing (1-2 天).

**v1 经验复利 10 连击曲线**: M22 ~1h → M23 ~1h → **M24 ~45min** (复用 M23 框架,
最快迭代)。**几何工具系列"模板化"**: cylinder/flange/block (prismatic 模板) +
hemisphere/frustum (revolve 模板) — 后续相同类零件 0.5-1h 可加新工具。

### M22 收尾 — pattern_circular L3 抽测 zero bug + 几何验证 (2026-06-05)

**PR #25 merge 后, 新 session L3 抽测 pattern_circular, zero bug + 几何验证
通过** (黄金法则 #13 收口)。pattern 类工具的几何验证模板首次确立, 防 v1
PR #32 老 silent fail 模式 (API 返非 None feat 但实例全重叠在 seed 位置 —
featureCount 增加但 edge/face 不增)。

- **抽测序列 (全 forward-slash 路径, 4 个调用, 链式 in-place)**:
  - `create_cylinder` D40 L20 → `pcd_ring.sldprt`
  - `add_axial_hole` Φ5 @ (10, 0) — PCD20 起始孔 (in-place)
  - `pattern_circular` count=6 (默认 360° full circle, in-place)
    → message: "Patterned '切除-拉伸1' circularly around ±Z axis — full circle (6×)"
  - `inspect_part` → 几何验证

- **几何验证硬证据 (跟 v1 PR #32 老 silent fail 模式对比)**:
  | 指标 | 期望 | 实际 | 备注 |
  |---|---|---|---|
  | featureCount | 5 | **5** ✓ | sketch + extrude + sketch + cut + **CirPattern** |
  | 最后 feature.typeName | `CirPattern` | **`CirPattern`** ✓ | M22 真生成 |
  | totalEdgeCount | 14 | **14** ✓ | cyl 2 + hole 2 + 5 副本 ×2 = 14 |
  | totalFaceCount | 9 | **9** ✓ | 2 端面 + 1 外侧 + 6 孔内壁 = 9 |

  edge/face count 跟"6 孔分散布置"完美匹配 — 这是**真 6 孔生成**而非 silent
  实例全重叠的硬证据。v1 PR #32 老 bug 是 featureCount=5 但 edge/face 仍按
  "1 孔"算 (实例重叠原位置)。

- **新规律 (pattern 类工具 L3 必验几何)**: pattern_*/mirror_* 等"批量复制"
  工具特别容易撞 v1 PR #32 模式 (API 返 ok 但几何只 1 个 instance), L3 抽测
  必须**数 edge/face 真增加**, 不能只看 featureCount。本次确立的几何验证
  模板可复用于 future revolve / pattern_* / mirror_* 工具收口。

- **dotnet format clean** (本 PR 纯 docs 不动代码)。

**意义**: 21 工具全部至少 L3 抽测 1 次, 几何能力扩展阶段质量曲线收口。**v1 PR #32
真根因复刻在 L3 长寿命 server 上也成立** (不只 L2 fresh exe), 跟 M15 (后 10 工具
L3 zero-bug) / M21 收尾 (装配家族) 同款质量收口节点。下一步 M23 选定后开干
(用户能力评估推荐 revolve)。

### M21 收尾 — 装配 mate 家族 L3 抽测 zero bug (2026-06-04)

**PR #23 (add_mate_concentric) merge 后, 新 session L3 抽测 distance + concentric
两个 mate, zero bug + 几何验证生效。** 一次性收口 M19/M20/M21 三处遗留的
"L3 待新 session 抽测" (黄金法则 #13)。

- **为啥是两个 mate**: M20 段记录 add_mate_distance 在那次 L3 session 因本分支
  rebase 时序"留下次抽测"(装配 5 工具中唯一未 L3 验过的); M21 concentric 本身也
  待抽。本 session 一并收口。
- **抽测序列 (全程 forward-slash 路径, 顺带第 3 次验 M20 path-normalize fix)**:
  - create_cylinder ×2 (cyl_a D30×L40, cyl_b D20×L50)
  - new_assembly ×2 (asm_conc / asm_dist)
  - add_component ×4 (forward-slash assembly + component 双路径,
    **M20 fix 在热 server 上确认: 不再 silent null**; inspect 返回的 sourcePath
    显示为 `\` = SW 内部 canonical, 正是 M20 根因, 工具已正确 normalize)
  - inspect_assembly ×4 (frame-origin Z = -height/2 复验 M17: cyl_a z=-20 /
    cyl_b z=-25)
  - **add_mate_concentric** (cyl_a-1 ↔ cyl_b-1, closest, in-place) — 几何验证:
    cyl_b x 50→0, 两圆柱轴真共线
  - **add_mate_distance** (top@cyl_a-1 ↔ top@cyl_b-1, 25 mm, closest, in-place) —
    几何验证: cyl_b y 0→25
- **几何验证 (不只 "API 返 ok")**: 每个 mate 后 re-inspect 确认组件真被约束移动,
  排除 "AddMate5 返 ok 但 mate 没生效" 的 silent 假成功。这是 mate 类工具
  L3 抽测应固化的额外一步 (vs 建模工具只看 status=ok)。
- **装配 5 工具完整 zero-bug 闭环达成**: new_assembly + add_component +
  add_mate_coincident (M20 session ✓) + add_mate_distance (本 session ✓) +
  add_mate_concentric (本 session ✓); inspect_assembly ×4 跨工具回归。
- **v1 PR #20 复刻 7 连击在 L3 长寿命 server 上也成立** (不只 L2 fresh exe),
  跟 M15 (后 10 工具 L3 zero-bug) 同款质量收口节点。

**意义**: 20 工具全部至少 L3 抽测 1 次, 装配家族质量曲线收口。下一步进入 M22
功能开发 (save_drawing / pattern_circular / mate helpers refactor 三选一)。

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
