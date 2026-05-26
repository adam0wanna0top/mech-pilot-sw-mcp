# 开发日志（DEV_LOG）

按时间倒序列已合并 PR、解锁的能力、踩过的坑。末尾是**当前能力覆盖率**和
**下一步待开发清单**。

每个 PR 项目录：
- 编号 / 标题 / 合并日期 / 头分支
- 新 API（公开签名）
- 关键工程发现
- 撤回 / 延后

---

## 已合并 PR（master 上）

### PR #35 — `feat(tools): create_flange L3 工具 + pattern_circular 多-cut limitation 标注`
- 合并：待定，头分支：`feat/multi-cut-pattern-record`
- **战略价值**：**绕过 SW permanent limitation** —— 12 个 stage 探针确认
  pattern_circular 在多 cut 零件（cylinder + 中心孔 + 偏心孔）上**任何路径都
  silent fail**（含 SW UI 录的宏自己重放也失败），是 SW 行为本身的限制，超出
  mech_pilot 控制范围。本 PR 加 ``create_flange`` 一键工具走"一个 sketch +
  一次 extrude_cut"路径，根本不依赖 pattern API → 法兰类零件 100% 可靠。
- 探针证据链（12 个 stage 完整记录）：
  - v1 (FCP3 + PR #32 spacing) → silent fail
  - v2 (CreateDefinition + setattr) → feat=None
  - v3 (+ fdef.Axis 显式 setter) → Axis 设后仍 None
  - v4 (+ AccessSelections) → COM 服务器异常
  - v5 (FCP3 + 宏精确参数 EqualSpacing=True/GeomPat=False/Spacing=2π) → feat=None
  - v6 (CreateDefinition + 完美 1:1 复刻宏 selection 7 步) → feat=None
  - v7 (+ 3 种 SetPatternFeatureArray) → A/B/C 全 fail
  - v8 (EnsureDispatch early binding) → Axis 仍 None
  - RunMacro2 v1 (swApp.RunMacro2 .swp) → errnum=22 module 找不到
  - RunMacro2 v2 (SW UI 手动跑修复后宏 + MsgBox 诊断) → "CreateFeature 返 Nothing"
  - **录宏重放实验** (用户用 SW UI 新录宏 + 立即重跑) → silent fail 无错误
  - 原录宏 retry → 不重现
- **根因（深层）**："录的宏 ≠ reliable code"。SW 录制器只记录鼠标点击对应的
  部分 API 入口，缺少 UI 内部隐式上下文（焦点窗口 / selection state machine /
  视图重绘 / 自动 axis 推断）。复杂特征受影响普遍（pattern / mirror /
  hole_wizard / sweep），但 pattern_circular 在多-cut 场景最严重。
- 新交付：
  - **L3 `create_flange(D_outer, thickness, center_hole_d, bolt_count, bolt_d,
    bolt_circle_d)`**：一键造法兰 / 端盖 / 周向孔类零件
    - 内部：cylinder + 1 个 sketch 画所有孔（中心孔 + N 个偏心孔用三角函数
      均布）+ 1 次 extrude_cut through_all
    - 全验证：D80×10 + ø30 中心 + 4×M6 PCD55 → 真生成 4+1 个孔
    - 灵活性：center_hole_d=0（实心法兰）/ bolt_count=0（仅中心孔）/ 3/6/8 个孔均布
  - **`pattern_circular_in_part.__doc__` 加 PR #35 limitation 警告**：明确说明
    多-cut 场景禁用 + 引导 LLM 改用 `create_flange` / `mirror_feature_in_part`
  - **`docs/SW_API_REFERENCE.md` 加 §8.3**：完整 12 stage 探针矩阵 + 根因分析
- 新文件：
  - `src/mech_pilot/tools/modeling_tools.py` 加 `FlangeSpec` + `create_flange`
  - `tests/test_create_flange_sw.py`：7 个集成测试（含 5 个 SW + 2 个错误路径）
  - `scripts/setup_multi_cut_pattern_record.py`：录宏脚手架（保留作为历史参考）
  - `scripts/debug_pattern_v2_createdef.py` ~ `v8_earlybind.py`：8 个探针脚本
  - `scripts/setup_pattern_macro_helper.py` + `debug_swfmcirpattern_int.py`：
    RunMacro2 + swFmCirPattern 枚举值探针
- 改动：
  - `src/mech_pilot/tools/pattern_tools.py`：pattern_circular_in_part docstring
    加 limitation 段
  - `tests/test_layer3_tools.py`：tool count 27 → 28
- L3: 27 → **28** 工具（modeling × 7 + editing × 8 + pattern × 6 + assembly × 3 +
  export × 2 + inspect × 2）
- 测试：7/7 create_flange 集成测试 PASS（28.96s）
- **LLM 用户体验巨大提升**：之前"画法兰" → pattern_circular 重试 3 次失败抛
  UnexpectedModelBehavior；现在 LLM 应识别"法兰" → 调 create_flange → 100% 成功

### PR #34 — `fix(solidworks): pattern_circular face-based axis + create_flange 部分尝试`
- 合并：2026-05-25，头分支：`feat/pattern-face-axis`（PR #34 实际只完成 face-axis L2 改造；create_flange 推迟到 PR #35）

### PR #33 — `fix(tools): L3 [error] 字符串 → raise ModelRetry（修 LLM hallucination）`
- 合并：待定，头分支：`feat/tool-error-raise`
- **战略价值**：修复 PR #32 法兰 bug 的**二级根因** —— LLM 看到 `[error]` 字符串
  仍可能报告 success（hallucination）。本 PR 改 L3 工具失败时 raise ModelRetry
  让 pydantic-ai 自动注入错误到下一轮 LLM 输入，**LLM 不能 silent ignore**。
- 根因：PR #32 trace 暴露的事实
  - LLM 调 pattern_circular 3 次连续失败（收到 [error] 字符串）
  - 但最终给用户答复 "已完成 4 个 M6 孔" —— **silent hallucination**
  - 老约定"工具返 [error] 请告诉用户失败"在 prompt 里有，但 LLM 不遵守
- 修复：
  - **新文件 `tools/_errors.py`**：`raise_tool_error(message)` raise ModelRetry +
    `expect_error_message(fn, *args)` 测试 helper（把 raise 转字符串）
  - **6 个 L3 工具模块全改造**：所有 `return f"[error] ..."` → `raise_tool_error(...)`
    - editing_tools / modeling_tools / assembly_tools / export_tools / inspect_tools / pattern_tools
    - 校验失败（路径不存在 / spec 校验 / 后缀错）→ raise
    - SW API 异常 → raise
  - **agent.py**：`Agent(tool_retries=3)` —— LLM 收到 ModelRetry 后最多 3 次重试
    机会调整参数 / 换策略；3 次仍失败彻底放弃并抛异常
  - **system prompt 更新**："绝不要假装成功；如工具最终失败必须如实报告"
    + "每次编辑零件后必须重新 inspect_part 拿最新坐标"
- 改动统计：
  - 改 30+ 个 `return f"[error] ..."` → `raise_tool_error(...)` 调用点
  - 改 27 个测试断言 `out = fn(spec); assert out.startswith("[error]")` →
    `out = expect_error_message(fn, spec); assert out.startswith("[error]")`
    （用 Python 脚本批量改 + 手动修 5 个不符 pattern 的 case）
- 新文件：
  - `src/mech_pilot/tools/_errors.py`：raise_tool_error + expect_error_message
  - `tests/test_tool_error_raise.py`：16 个单元测试（验证 raise / message / retry helper）
- **关键发现 1（LLM hallucination 根因）**：
  - pydantic-ai 把工具返回的字符串当**正常 success 结果**给 LLM
  - LLM 看到 `[ok] ...` 或 `[error] ...` 都是字符串，行为基于 prompt 规则
  - **行为级修复（prompt 改）** vs **机制级修复（raise）**：机制级强多了
- **关键发现 2（raise vs return 的 LLM 行为差异）**：
  - return [error] → pydantic-ai 把字符串塞 LLM 上下文，LLM 可选择 ignore
  - raise ModelRetry → pydantic-ai 强制 LLM 收到结构化"重试" 信号，不能直接答 "已完成"
  - retry budget 限定（3 次）→ 防止无限重试爆 token
- **关键发现 3（测试改造的批量脚本套路）**：
  - 6 个模块 30+ return 全改 raise，27 个测试断言 sed-style 批量
  - 用 Python regex 双行 pattern 替换 `out = X(Y); assert out.startswith("[error]")` →
    `out = expect_error_message(X, Y); assert out.startswith("[error]")`
  - 不符合 pattern 的（多行 spec / 注释插入）5 处手动改
- 测试：16/16 新 raise 测试 PASS + 全套 smoke PASS + 216 SW 集成测试回归 PASS
  （expect_error_message helper 让老测试无需大改语义）

### PR #32 — `fix(solidworks): pattern_circular silent fail 根因修复 + inspect_part 临时轴段`
- 合并：2026-05-24，头分支：`feat/pattern-axis-fix`
- **战略价值**：修复 **PR #29 以来一直存在的 silent fail bug** —— pattern_circular
  API 返非 None feat 对象但**实际只生成 1 个 instance**（其他 N-1 个重叠在原位置）。
  老测试只验证文件存在 / 大小，从未数过孔数，所以 8 个测试一直误 PASS。
  本 PR 也加 inspect_part 临时轴段防止 LLM 瞎猜轴坐标。
- 用户场景触发：用户让 agent 画法兰 "4 个 M6 孔均布在 D=55 圆周上"，agent 报告
  "已完成 4 个孔"但 SW 实际只有 1 个 → 复现脚本 `debug_flange_repro.py` 拿到
  完整 trace 暴露真相。
- 真根因（双重 bug）：
  1. **FeatureCircularPattern3 spacing 参数语义错** —— 老代码传
     `math.radians(total_angle_deg)` 当 spacing（如 360°），但 SW CHM 文档说
     `EqualSpacing=False` 时 spacing 是**每个 instance 之间的间距**（不是总角度）
     → 4 个 instance 间距 360° → 全重叠在原位置 → silent fail
  2. **add_simple_hole_to_part 复用过期 face 坐标** —— LLM 加完中心 D=30 孔后，
     `(0, 0, 10)` 这个面点已被孔挖掉，但 LLM 仍用旧坐标加偏心孔 → fail →
     "切除-拉伸2" 从未生成 → 后续 pattern 必败
- 改动：
  - `solidworks/feature.py`：`pattern_circular` 修 `spacing = total_angle / count`
    （弧度）—— **5 行修改解锁所有圆周阵列真实工作**
  - `solidworks/part.py`：加 `list_axes(model) -> [AxisInfo]` + `AxisInfo` dataclass
    （从 cylinder face 提取 CylinderParams → 报告 point_on_axis_mm + 方向标签 X/Y/Z）
  - `tools/inspect_tools.py`：报告新增 "临时轴 N 条" 段，列每条轴的方向 +
    point_on_axis_mm（直接复制喂 pattern_circular 的 axis_xyz_mm）
  - `tools/pattern_tools.py`：`pattern_circular_in_part.axis_xyz_mm` description
    推荐 "先调 inspect_part 拿 point_on_axis_mm"；明确说 mech-pilot 圆柱**轴沿 Z**
  - `tools/editing_tools.py`：`add_simple_hole_to_part.face_xyz_mm` description
    加 prominent 警告 "每次打孔后零件几何变了，必须重新 inspect_part 拿最新面"
- 新文件：
  - `scripts/debug_flange_repro.py`：完整 LLM bug 复现脚手架（拿完整 tool trace）
  - `scripts/debug_select_axis.py`：select_axis TEMPAXIS / AXIS / 不同坐标位置探针
  - `tests/test_pattern_axis_fix_sw.py`：5 个集成测试（list_axes / pattern 真生成 4 孔 /
    inspect 报告临时轴 / stale face 检测 / re-inspect 工作流）
- **关键发现 1（SW pattern API 间距语义）**：
  - SW CHM 文档 (`IFeatureManager.FeatureCircularPattern5`) 明确：
    `Spacing` = "间距" (per instance) when `EqualSpacing=False`，
    = "总角度" when `EqualSpacing=True`
  - 我们走 EqualSpacing=False 路径（True 让 feat 返 None），所以必须算
    `spacing = total_angle / count`
- **关键发现 2（silent fail 比 raise 更可怕）**：
  - API 返 None → 我们 raise → LLM 收到 [error] 可重试
  - API 返非 None feat 但 SW 内部错 → 我们以为 OK → LLM 报告 success → **用户得到错的零件**
  - 老测试只看 "saved file > 10KB" PASS，从未验证最终特征数 / 孔数 / 几何
  - **教训**：所有 SW pattern / mirror / cut 类测试必须**数最终几何**（如 edges
    数量、faces 数量），不能只看文件存在
- **关键发现 3（inspect_part 临时轴段是 LLM 工作流关键加固）**：
  - LLM 看到 inspect 报告的 "临时轴: point_on_axis_mm=[0,0,10] 方向=Z" 后，
    直接复制坐标喂 pattern，不用算几何
  - 老代码 description "圆柱轴沿 z 方向" 太抽象，LLM 试 (0,0,5)/(0,0,10)/(0,0,0)
    都失败（被 silent fail 误导）；新报告 = "标准答案"
- 测试：13/13 PASS（PR #32 新 5 个 + PR #29 老 8 个 pattern 测试仍 OK） + 216 个
  集成测试全套回归 PASS（spacing 修复完全向后兼容）
- **遗留 PR #33 处理**：LLM hallucination（看到 [error] 但仍报告 success）—— 走
  ToolError raise 机制，让 LLM 必须看到错误

### PR #31 — `feat(solidworks): 草图级 add_dimension 真破局 (ISketch.AutoDimension2)`
- 合并：待定，头分支：`feat/autodimension2`
- **战略价值**：**反转 PR #30 误判的 permanent limitation**。mech-pilot 现在
  能改**草图级尺寸**（矩形 length / width / 圆直径），与 PR #29 改的特征级
  尺寸（拉伸深度等）合体，完整覆盖工程师"改尺寸 → 重建"参数化工作流 100%。
- 破局路径（1 个 stage 直接全胜）：
  - PR #30 9-stage 探针漏了 **`ISketch.AutoDimension2`** 实例方法（不是
    `IModelDoc2` 上的，是草图对象自己的方法）
  - 签名：`AutoDimension2(EntitiesAll=1, Baseline=1, Above=1, Baseline=1, Right=1)`
    返回 swAutodimStatus_e (0=Success)
  - **不需要 X/Y/Z 放置坐标** —— SW 内部自动给草图所有支持实体加标准尺寸
  - 实测：``draw_rectangle(0,0,50,30) + AutoDimension2`` → 草图1 里多了
    ``D1@草图1=50`` + ``D2@草图1=30``，**直接被 modify_dimension 改**
- 新文件：
  - `scripts/debug_autodim2.py`：3 stage 探针（矩形 Baseline / 矩形 Chain / 圆）
  - `tests/test_auto_dimension_sw.py`：8 个集成测试（L2 happy + 错误路径 +
    **核心：create_block/cylinder 后 modify D1@草图1 改 length/直径** + cone/tube backward compat）
- 改动：
  - `solidworks/sketch.py`：加 `auto_dimension_sketch(model, *, scheme="baseline")`
    L2 API + 完整 swAutodim* 枚举常量 + status code 翻译；
    `add_smart_dimension_via_command` 标 deprecated（PR #30 silent placeholder）
  - `tools/modeling_tools.py`：`create_rectangular_block` / `create_cylinder` /
    `create_tube` 默认在 `exit_sketch` 前调 `auto_dimension_sketch`，让 LLM
    后续可用 modify_dimension_in_part 改草图尺寸
  - `config/e2e_cases.yaml`：加 `modify-sketch-length` E2E case（36 → 37）
  - `docs/SW_API_REFERENCE.md`：加 §8.2 "PR #31 ISketch.AutoDimension2 破局"
    + 反转 §8 表里 add_dimension 行 + 标 §8.1 为"历史失败矩阵（已被 PR #31 反转）"
- **关键发现 1（为何 PR #30 漏了）**：
  - PR #30 探针扫了 `IModelDoc2.*` / `IModelDocExtension.*` / `ISketchManager.*`
    上所有 Add*Dimension API，**唯独漏了 `ISketch` 实例方法**
  - `ISketch` 接口是从 `Sketch1` 特征经 `GetSpecificFeature2()` 或
    `SketchManager.ActiveSketch` 拿到的草图对象，方法相对隐蔽
  - 教训：CHM 文档查方法时应同时扫 `ISketch~Auto*` / `ISketch~Add*`，不只
    是顶层 `IModelDoc2`
- **关键发现 2（AutoDimension2 不崩 vs AddDimension2 崩 的根因）**：
  - AutoDimension2 = SW 内部"批量加标准尺寸"，**不需要 UI 鼠标放置事件**，
    外部 RPC 能完整完成
  - AddDimension2 = "用户点了一个 entity + 拖到位置放下"UI 事件路径，外部
    RPC 无 UI 上下文 → 调用挂起或崩
  - 这是"批量自动" vs "逐个 UI 模拟"的 API 设计区别 —— 前者天生 RPC 友好
- **关键发现 3（粗粒度建模工具默认 auto_dimension）**：
  - LLM 看不到"先建模再加尺寸再改"的复杂流程，希望"建好的零件能直接改"
  - `create_rectangular_block` / `create_cylinder` / `create_tube` 默认在
    草图里自动加 D1/D2 命名尺寸 → 用户后续"把 length 改成 80mm" 一步可达
  - 默认 enable 的成本：每个零件多 2 个 dimension 标记（影响微）；收益：
    全部用户 prompts "make a 50mm box then change to 80mm" 直接 work
- 测试：8/8 集成测试 PASS（22.40s） + 回归 26/26 PASS（PR #29 modify_dimension /
  PR #30 safe path / extrude modes / patterns 全部仍 OK）

### PR #30 — `docs(solidworks): 草图级 add_dimension permanent limitation 确认`
- 合并：2026-05-24（**已被 PR #31 反转，permanent limitation 撤销**），头分支：`feat/sketch-dimension`
- **战略性质**：**deliberate non-feature PR** —— PR #29 留下的草图级 add_dimension
  limitation 经系统性探针后**确认为 SW 2026 SP02.1 + pywin32 late binding 永久 limitation**。
  不再消耗后续 PR 周期重复尝试，把精力转向其他高价值能力。
- 探针过程：``scripts/debug_sketch_dimension.py``  9 个 stage (A-I) 系统扫描
  所有 Add*Dimension API + RunCommand 路径：
  - **5 条崩 SW 路径**：AddDimension2 / AddDim(Dir=0/1/2/3) / AddHorizontalDimension2 /
    AddSpecificDimension(HorLinear=11) / 同进程重复调用
  - **2 条 silent 不崩路径**：RunCommand(3244=InsertAutoDim) 返 True 不加；
    **RunCommand(38=SmartDim) + AddDimension2 silent None 不真加** —— 唯一"安全单次"
  - **关键对照** I3：去掉 RunCommand(38) → AddDimension2 直接调 → SW 崩；加上 → 不崩
- 改动（轻交付）：
  - `solidworks/sketch.py`：加 `add_smart_dimension_via_command(model, app, x, y, z)`
    包装唯一"安全单次"路径。silent 返 None + docstring 完整警告（不真加 / 同进程
    仅一次 / 不推荐 LLM 调用 / 走 modify_dimension 代替）
  - `solidworks/sketch.py`：删原 `# NOTE: add_dimension 暂未交付` 占位注释
  - `tests/test_smart_dimension_safe_sw.py`：1 个集成测试覆盖"安全单次不崩 + 保存
    + 重开"（不写"多次调用"测试，会污染 SW 后续测试）
  - `scripts/debug_sketch_dimension.py`：9-stage 完整探针，记录所有失败路径
  - `docs/SW_API_REFERENCE.md`：加 §8.1 "草图级 add_dimension 完整失败矩阵"，
    把 add_dimension 表行升级为 "三方探针确认 permanent limitation" + 根因猜测
- **关键发现 1（pywin32 vs VBA 上下文差异）**：
  - SW UI 录宏（VBA 上下文）跑 AddDimension2 **OK** —— VBA 在 SW 进程内有完整 UI 状态
  - pywin32 late binding（外部 RPC）跑 AddDimension2 **崩** —— 外部 RPC 不持有 UI 上下文
  - **RunCommand(38) 进入"智能尺寸工具模式"让 SW 内部有 UI 上下文** → AddDimension2 不崩
    但 silent（外部 RPC 无法触发"鼠标放置"事件 → 命令挂起返 None）
- **关键发现 2（pywin32 同进程 state 扰动）**：
  - 即使"安全路径"，**同进程内第 2 次调用必崩** —— SW 内部 sketch state 被首次
    silent 调用扰动，连续走破坏 RPC 链
  - 实战意义：测试套件不能放"多次 add_dimension 不崩"测试，会污染后续 SW 测试
- **未走的破局路径（留 future PR）**：
  - `ISldWorks.RunMacro2(.swp, mod, sub, opt, err)` 跳预录 VBA 宏：SW 进程内 VBA
    上下文 = AddDimension2 OK。但需要：(a) 用户录"参数化尺寸"宏 (b) 宏从 SW Custom
    Property 读 X/Y/Z 参数化。复杂度高 + 部署门槛重（依赖用户预录 .swp）。
- **PR 价值定位**：
  - 不增加 L3 工具数（保持 27 个）/ 不加 E2E case（保持 36 个）
  - 给 LLM 节省试错地狱：未来不再有人尝试草图级 add_dimension（文档已明示）
  - 留 1 个 silent 路径 `add_smart_dimension_via_command` 作为"破局基础" —— 若
    未来找到模拟"鼠标放置事件"的方法可在此基础上完成
- 测试：1/1 集成测试 PASS（8.07s，验证 SW 单次不崩 + 保存重开 OK）

### PR #29 — `feat(solidworks): 参数化迭代 — modify_dimension + list_dimensions`
- 合并：2026-05-24，头分支：`feat/add-dimension`
- **战略价值**：mech-pilot 从"对话式建模生成器"升级到"对话式设计工具"。
  LLM 现在能**改尺寸 / 迭代**已有零件（不只是从零生成）。这是工程师
  80% 工作流（不停改尺寸）的第一次覆盖。
- 破局路径：
  - **AddDimension2** 在 late binding 下仍崩 SW（PR #8 / PR #29 探针双重确认，
    跟 SW UI 录宏调用同款但 pywin32 触发 RPC_E_DISCONNECTED）
  - **modify_dimension（改已有尺寸）走 ``Part.Parameter().SystemValue =``** 安全
  - SW 创建特征时自动给 Parameter 命名（``D1@凸台-拉伸1`` 等）—— 已有零件
    本来就有可改参数，**不需要 AddDimension2 也能做参数化 80%+ 价值**
- 新文件：
  - `scripts/setup_dimension_record.py`：脚手架让用户录"加智能尺寸 + 改尺寸"宏
  - `scripts/debug_dimension.py`：复现 AddDimension2 崩溃 + 验证 InsertDimension 路径
  - `scripts/debug_modify_dimension.py`：验证 Parameter 路径可行（核心破局）
  - `tests/test_modify_dimension_sw.py`：8 集成测试（含中英文别名 + 持久化）
- 改动：
  - `solidworks/feature.py`：
    - 加 `modify_dimension(model, dim_name, *, value_mm | value_rad)`
      （Parameter.SystemValue + EditRebuild3）
    - 加 `list_dimensions(model)`（遍历特征，枚举 D1-D8 命名 Parameter）
    - 中英文别名自动兜底（D1@凸台-拉伸1 ↔ D1@Boss-Extrude1 等）
  - `tools/editing_tools.py`：加 `modify_dimension_in_part` + spec
    （L3 26 → **27 工具**）
  - `tools/inspect_tools.py`：`inspect_part` 报告新增"可改尺寸"段
    （LLM 调用前先看哪些 dim_name 可改）
  - `config/e2e_cases.yaml`：加 `modify-extrude-depth` E2E case（35 → 36）
- **关键发现 1（AddDimension2 late binding 真崩，SW UI 不崩）**：
  - 录宏跑同款 AddDimension2 调用安全，pywin32 跑就 RPC_E_DISCONNECTED
  - 推测：录宏鼠标点击触发了 SW 内部某种 selection state 激活
  - 解决方案：**绕开 AddDimension2，走 modify_dimension 路径**（SW 自动命名
    特征参数已经够用 80%+ 场景）
- **关键发现 2（特征级 vs 草图级参数化）**：
  - ✅ 特征级（拉伸深度 / 孔径 / 圆角半径 / 旋转角度）：SW 自动命名，**可改**
  - ❌ 草图级（矩形长宽 / 圆心位置）：需手动加智能尺寸（AddDimension2 崩），**不可改**
  - 大多数 LLM 改尺寸场景是特征级（"深度改成 15mm"），覆盖足够
- **关键发现 3（inspect_part 报告增强即 LLM 的"参数表"）**：
  - inspect_part 现在输出"可改尺寸 N 个"段，列每个特征的 D1-D8 当前值
  - LLM 不知道叫什么 dim_name 时先调 inspect_part 看清，再 modify
  - E2E case `modify-extrude-depth` 验证 LLM 能走"inspect → modify"两步流程
- E2E：DeepSeek v4-flash 3 calls / 26s PASS（LLM 走完整 inspect→modify 流程）

### PR #28 — `feat(solidworks): GB 六角头螺栓沉孔（add_counterbore_hole 加 screw_type）`
- 合并：2026-05-24，头分支：`feat/gb-fasteners-extend`
- 解锁：**六角头螺栓 GB/T 5780/5783 沉孔**（CounterBore hex_bolt 路径），
  补齐 PR #25 只有内六角圆柱头的盲点。机械装配最常见两种紧固件全覆盖。
- 破局路径：用户录"GB 六角头螺栓 M8 CounterBore"宏揭示
  ``fastener_type=378`` —— 不在 359/361/363 奇数序列里，**打破之前规律**。
- 改动：
  - `solidworks/feature.py`：
    - 加常量 `SW_FASTENER_GB_COUNTERBORE_HEX_BOLT=378`
    - 加 `GB_COUNTERBORE_HEX_BOLT_TABLE`（GB/T 152.3 hex bolt 沉孔尺寸）
    - `add_counterbore_hole` 加 keyword-only `screw_type` 参数
      （``"inner_hex"`` 默认 / ``"hex_bolt"``），按 type 选 fastener_type
      + 表 + Value 模板
  - `tools/editing_tools.py`：`CounterboreInPartSpec` 加 `screw_type`
    字段（LLM-friendly description 区分两种螺钉类型）
  - `tests/test_hex_bolt_cb_sw.py`：9 个集成测试（M8 happy path +
    M4/M6/M10/M12 参数化 + 错误路径 + inner_hex backward compat）9/9 通过
  - `config/e2e_cases.yaml`：加 `gb-hex-bolt-cb-m8` E2E case（34 → 35）
- **关键发现 1（fastener_type 序列破规律）**：
  - 之前发现 359/361/363 间隔 2 奇数，本 PR 揭示 hex_bolt=378 跨度大
  - 推测：357-369 是基础 hole 类型（tap / 内六角 CB / CSK），370+ 是衍生
    紧固件类型；每种 hole 类型有独立 fastener_type 系列
- **关键发现 2（每种 fastener_type 的 Value 模板不同）**：
  - inner_hex CB (361)：Value1=11, Value2=6.8, Value6=11.05, Value7=π/1.8
  - hex_bolt CB (378)：Value1=18, Value2=**0.5**（间隙常数），Value6=0, V7=0
  - **Value2 在 hex_bolt 下不是"沉孔深度"** 而是某种间隙/装配参数
- **设计决策（扩展现有 API vs 加新 API）**：
  - 选扩展 `add_counterbore_hole` 加 `screw_type` 参数（向后兼容 PR #25 默认）
  - 不加新 `add_hex_bolt_counterbore` 函数 —— 避免 DRY 重复，
    L3 工具列表保持 24/24（spec 字段加 1 个）
- E2E：DeepSeek v4-flash 2 calls / 24s，LLM 正确选 `screw_type='hex_bolt'`

### PR #27 — `feat(solidworks): sweep + loft 复杂曲面建模`
- 合并：2026-05-24，头分支：`feat/sweep-loft`
- 解锁：**扫描凸台（Sweep）+ 放样凸台（Loft）** —— 复杂曲面建模能力，
  覆盖管路 / 异形件 / 变径接头场景。L3 24 → **26 工具**。
- 破局路径：用户录 SW UI 宏（sweep + loft 两个），揭示**两种完全不同的 API
  入口**：
    - **Sweep**：走 `CreateDefinition(swFmSweep=17) + setattr + CreateFeature`
      （跟 PR #21 mirror/pattern 同款）。selection: profile mark=1, path mark=4
    - **Loft**：直接调 `IFeatureManager.InsertProtrusionBlend` 17 参一次性 API。
      selection: 所有 profile 都 mark=1（按选择顺序）
- 新文件：
  - `scripts/setup_sweep_loft_record.py`：脚手架 2 个零件让用户录两个宏
  - `scripts/debug_sweep_loft.py`：v2 套路扫 swFmSweep + 1:1 复刻两宏的探针
  - `tests/test_sweep_loft_sw.py`：6 个集成测试（L2 + L3 + 错误路径）6/6 通过
- 改动：
  - `solidworks/feature.py`：加 `add_sweep(model)` + `add_loft(model)` + 常量
    `SW_FM_SWEEP=17`（v2 探针扫得）
  - `tools/modeling_tools.py`：加 `create_swept_pipe` + `create_lofted_transition`
    粗粒度 L3 工具（含 `SweptPipeSpec` / `LoftedTransitionSpec`）。
    内部自动建 2 个 sketch + 选 mark + 调 L2 API
  - `tests/test_layer3_tools.py`：加 5 个 spec smoke
  - `config/e2e_cases.yaml`：加 `swept-pipe-basic` + `lofted-transition-basic`
    E2E case（32 → 34）
- **关键发现 1（Sweep 和 Loft 走完全不同 API 入口）**：
  - 之前 PR #21 mirror 也是录宏才发现 SW UI 走的是 `MirrorComponents3` 而不是
    `CreateDefinition(swFmMirrorComponent)`
  - 经验：**复杂特征类录宏比读 CHM 更快锁定真路径**
- **关键发现 2（swFmSweep=17）**：
  - CHM 列了 `swFmSweep` 枚举名但不公开 int 值（同 PR #21 套路）
  - v2 探针扫 1..300 + 按 `PathAlignmentType + TwistControlType` 属性识别
- **关键发现 3（粗粒度 L3 设计）**：
  - sweep / loft 不像 hole 那样原子（需要先建 N 个 sketch + 选 mark）
  - L3 不暴露 raw `add_sweep` / `add_loft`，而是包装成"一站式造完整零件"
    工具（`create_swept_pipe` / `create_lofted_transition`），LLM 不用关心
    sketch / mark 细节，更符合粗粒度原则
- E2E：DeepSeek v4-flash 1 call / 13-14s，LLM 一次选对工具

### PR #26 — `feat(solidworks): extrude 多模式 + select_vertex`
- 合并：2026-05-24，头分支：`feat/extrude-modes-and-vertex`
- 解锁：**extrude 三种 end_condition**（blind / through_all / mid_plane）+
  **flip 反向参数** + **select_vertex 顶点选择**。补齐 PR #21-#25 后剩余的
  最大建模能力盲点。
- 改动：
  - `solidworks/feature.py`：`extrude` 加 keyword-only `end_condition='blind'` /
    `flip=False` 参数；`depth_mm` 改为可选（through_all 不需要）；保留
    `both_dir` 旧参数，与 `end_condition='mid_plane'` 等价
  - `solidworks/sketch.py`：加 `select_vertex(model, x, y, z, *, mark, append)`，
    走 `_select_by_coords` + type="VERTEX"
- 测试：
  - `tests/test_extrude_modes_sw.py`：9 个集成测试（blind / mid_plane / flip /
    select_vertex 多选 / 错误路径）9/9 通过
  - 老 `test_extrude_cut_sw.py` / `test_patterns_sw.py` 13/13 仍过（backward
    compat 验证）
- 关键设计：**保留 `both_dir` 旧参数兼容**所有现有调用（modeling_tools /
  pattern_tools / hole tests 等 14 个 caller）；新代码鼓励用更明确的
  `end_condition='mid_plane'`

### PR #25 — `feat(solidworks): GB 沉孔（CounterBore + CounterSink）`
- 合并：2026-05-24，头分支：`feat/hole-wizard-counterbore-countersink`
- 解锁：**GB 柱形沉头孔（M3-M12）+ GB 锥形沉头孔（M6-M12）**。机械装配
  常用的内六角圆柱头螺钉沉孔（CounterBore）+ 沉头螺钉沉孔（CounterSink）
  两路径全破。L3 22 → **24 工具**。
- 破局路径：PR #24 GB Tap 套路同款——用户录"异形孔向导 → GB CounterBore M6"
  + "GB CounterSink M6"两个宏 → 真值一并到手：
    - **GB CounterBore fastener_type = 361**（hole_type=0）
    - **GB CounterSink fastener_type = 363**（hole_type=1）
  录宏 Value 完全对应 **GB/T 152.3 沉孔标准表**（M6 → CB 直径 11mm 完全匹配）。
- 新文件：
  - `scripts/setup_holewiz_sink_record.py`：脚手架零件让用户录 2 个宏
  - `scripts/debug_hole_wizard_v5_sinks.py`：1:1 复刻录宏的破局探针
  - `tests/test_sink_holes_sw.py`：12 个集成测试（CB M3/M4/M5/M6/M8 + CSK
    M6/M8/M10/M12 + 错误路径）12/12 通过
- 改动：
  - `solidworks/feature.py`：加 `add_counterbore_hole` / `add_countersink_hole` +
    常量 `SW_FASTENER_GB_COUNTERBORE=361` / `SW_FASTENER_GB_COUNTERSINK=363` +
    `GB_COUNTERBORE_TABLE`（GB/T 152.3）+ `GB_COUNTERSINK_TABLE`（GB/T 152.2）
  - `tools/editing_tools.py`：加 `add_counterbore_hole_to_part` /
    `add_countersink_hole_to_part` + 2 个 spec（L3 22 → 24）
  - `config/e2e_cases.yaml`：加 `gb-counterbore-m6` / `gb-countersink-m8` E2E
    case（27 → 29）
- **关键发现 1（GB fastener_type 系列）**：
  - GB Tap = 359 (PR #24)
  - GB CounterBore = 361 (本 PR)
  - GB CounterSink = 363 (本 PR)
  - 规律：奇数 + 间隔 2；推测中间偶数 360/362 是某些细分类（如 ISO 螺钉用 GB 兼容）
- **关键发现 2（每种 hole_type 的 Value 模板不同）**：
  - **CB**：Value1 = 沉孔直径（GB/T 152.3）/ Value2 = 沉孔深度 / Value6 =
    沉孔直径+0.05（D+公差）/ Value7 = π/1.8 ≈ 100°（占位）/ Value4=1（flag）
  - **CSK**：Value1 = 沉头大端 D（GB/T 152.2）/ Value2 = π/2 = 90°（沉头角）/
    Value4=1（flag）/ Value10/11/12 = -1（"SW 默认"占位）
  - PR #24 GB Tap 的 4 个魔法位（Value7/8/11/12 = 1/1/-1/-1）跟这两个**完全不同**
    —— 说明 Value 位语义由 hole_type 决定，不是通用模板
- **GB_COUNTERBORE_TABLE 覆盖完整（M3-M12，GB/T 152.3）**；
  **GB_COUNTERSINK_TABLE 仅 M6-M12**（M3-M5 SW 2026 内部 GB 沉头螺钉数据库缺失，
  HoleWizard5 返 None，已 L3 spec 拒绝 + docstring 标注 limitation）
- E2E：DeepSeek v4-flash 2 calls / 20-55s，LLM 一次选对 CB / CSK 工具

### PR #24 — `feat(solidworks): GB 标准螺纹孔（M3-M12 攻丝孔）`
- 合并：2026-05-23，头分支：`feat/hole-wizard-gb-metric`
- 解锁：**国内最高频螺丝孔场景** —— GB M3 / M4 / M5 / M6 / M8 / M10 / M12
  螺纹孔（攻丝孔）。从 PR #13 起"hole_wizard GB/Metric 全失败"破局。L3 21 → 22 工具。
- 破局路径：v3 探针扫了 12 个 GB / ISO / Metric 组合全失败；用户在 SW UI 录
  "异形孔向导 → GB M4 螺纹孔"宏 → 真值暴露 `fastener_type=359` + 4 个魔法
  Value 位（v3 全设 0 的根因）。
- 新文件：
  - `scripts/setup_hole_wizard_record_part.py`：脚手架零件让用户录宏
  - `scripts/debug_hole_wizard_v4_gb_tap.py`：1:1 复刻录宏的破局探针
  - `tests/test_threaded_hole_sw.py`：8 个集成测试（M4 happy path + M3/M5/M6/M8
    参数化 + 错误路径）8/8 通过
- 改动：
  - `solidworks/feature.py`：加 `add_threaded_hole(model, plane, thread_size, *,
    depth_mm, through_all)` + 常量 `SW_FASTENER_GB_TAP=359` + `GB_TAP_TABLE`
    （M3-M12 标准 drill/pitch 查表）
  - `tools/editing_tools.py`：加 `add_threaded_hole_to_part` + `ThreadedHoleInPartSpec`
    （L3 21 → 22）
  - `config/e2e_cases.yaml`：加 `gb-threaded-hole-m4` 和 `gb-threaded-hole-m6-blind`
    E2E case（25 → 27）
- **关键发现 1（GB tap fastener_type=359）**：
  - CHM 的 `swStandardGBFastenerTypes_e` 枚举完全不公开 int 值
  - v3 探针猜 0/18 等小值全失败；真值 **359** 只能从录宏拿
  - 同 fastener_type=359 + 不同 thread_size 字符串覆盖 M3-M12（参数化测试验证）
- **关键发现 2（HoleWizard5 27 参里的"魔法位"）**：
  - v3 探针把 Value1-12 全设 0 → GB tap 必返 None
  - 录宏揭示 GB tap 路径必需的非零位：
    `Value7=1.0, Value8=1.0`（flag enable）
    `Value11=-1.0, Value12=-1.0`（"SW 默认"占位）
    `Value3=π/1.8≈100°`（沉头角默认；tap 不沉但 SW 内部仍要）
  - `Length`（第 8 位）官方文档说 Double，录宏传 True（=-1.0，VBA 隐式转 double）
- **关键发现 3（GB_TAP_TABLE 标准查表）**：
  - tap drill 直径 ≠ 螺纹标称直径（M4 → drill=3.3, 螺纹=4.0）
  - 全由 GB/T 196-2003 普通螺纹基本尺寸 + GB/T 3098.1 紧固件标准查得
  - 一表覆盖 M3 / M4 / M5 / M6 / M8 / M10 / M12 七种规格
- E2E：DeepSeek v4-flash 2 calls / 17-26s，LLM 一次选对 `add_threaded_hole_to_part`

### PR #22 — `feat(e2e): 组件阵列三件套 E2E case`
- 合并：2026-05-23，头分支：`feat/component-pattern-e2e`
- 解锁：把 PR #21 的 3 个 L3 工具（local_linear_pattern_components /
  local_circular_pattern_components / mirror_components）从"SW 集成测试 7/7"
  升级到"LLM 端到端实战 3/3"。E2E 22 → 25 cases。
- 改动：`config/e2e_cases.yaml` 加 3 个 case（asm-linear-pattern-components /
  asm-circular-pattern-components / asm-mirror-components）
- 关键 prompt 设计：每个 case 显式说"用 ``local_*_pattern_components`` 把**组件
  实例 cyl-1** 阵列" —— 防 LLM 误选 ``pattern_*_in_part``（零件版）
- 实测：DeepSeek v4-flash 各 case 4 tool calls / 30-80s，0 retry
- DEV_LOG / SW_API_REFERENCE 无新增 SW API 知识（PR #21 已写齐）

### PR #21 — `feat(solidworks): 装配体组件阵列三件套（linear / circular / mirror）`
- 合并：2026-05-23，头分支：`feat/component-pattern`
- 解锁：**装配体级组件阵列 / 镜像** —— 把一个螺栓阵列成 6 个、左右对称组件
  镜像。L3 从 18 → 21 个工具。
- 破局路径：v3 卡在 `SeedComponentArray` 报 readonly，本以为要 early
  binding 解 `ISet*Array`。读 CHM "Create Local Linear Pattern" VBA example
  + ILocalLinearPatternFeatureData Remarks 的 **selection mark 表**，发现
  v3 是 mark 用错了。Linear/Circular 走 pre-select mark；Mirror 走显式
  setter（属性名是 `ComponentsToInstanceAlignToComponentOrigin`，不是
  `ComponentsToInstance`）。
- 新文件：
  - `scripts/debug_pattern_component_v4.py`：破局探针，3 个 API 都拿到真特征
  - `scripts/debug_mirror_clean_asm.py` / `debug_mirror_two_cyls.py`：mirror
    限制定位
  - `tests/test_pattern_component_sw.py`：7 个集成测试（6 passed + 1 skipped）
- 改动：
  - `solidworks/feature.py`：加 3 个公开 API
    - `add_local_linear_pattern_components(asm, direction_edge_xyz_mm, component_names, count, spacing_mm, *, flip, direction2_edge_xyz_mm, count2, spacing2_mm, flip2)`
    - `add_local_circular_pattern_components(asm, axis_xyz_mm, component_names, count, *, total_angle_deg, equal_spacing)`
    - `add_mirror_components(asm, plane, component_names, *, orientation, mirror_type)`
    - 私有 helpers：`_strip_doc_title` / `_select_component_by_instance_name`
  - `tools/pattern_tools.py`：加 3 个 LLM 工具
    - `local_linear_pattern_components` / `local_circular_pattern_components` / `mirror_components`
    - 各自 Pydantic spec 带 LLM-friendly description
  - `tests/test_layer3_tools.py`：spec smoke + 工具计数从 18 → 21
- **关键发现 1（CHM 隐藏 mark 表）**：
  - SW CHM 在 `ILocalLinearPatternFeatureData.html` / `ILocalCircularPatternFeatureData.html`
    / `IMirrorComponentFeatureData.html` 的 Remarks 段给出**完整 selection
    mark 表**，公开文档第一眼看不到，要点开接口主文档
  - 组件版 vs 零件版 mark 约定**完全不同**：
    - 组件 linear：direction edge=2, seed component=1
    - 组件 circular：axis=2, seed component=1
    - 组件 mirror：plane=1, components=2（仅参考；走 setter 路径）
    - 零件 linear：edge=1, seed feature=4
    - 零件 mirror：plane=2, seed feature=1
- **关键发现 2（数组属性的真实 setter）**：
  - CHM 的 `SeedComponentArray` / `ComponentsToInstance` 标 `get; set;`，但
    pywin32 late binding 解析成 readonly（实测 v3 / v4 同验）
  - **正确路径**：
    - Linear/Circular：根本不用 setter，pre-select mark 让 SW 自动绑
    - Mirror：用带后缀 `AlignToComponentOrigin` / `AlignToSelection` 的属性，
      即 `ComponentsToInstanceAlignToComponentOrigin = (comp1, comp2, ...)`
- **关键发现 3（swFeatureNameID_e 整数值不公开）**：
  - CHM 只列枚举名（`swFmLocalLPattern` 等），不给 int 值
  - v2 探针暴力扫 `CreateDefinition(1..300)` + 按数据对象特有属性识别得：
    `swFmLocalLPattern=6` / `swFmLocalCirPattern=5` / `swFmMirrorComponent=116`
- **已知 limitation**：
  - `add_mirror_components` 在 SW 2026 SP02.1 + late binding 对**无 mate 约束
    的自由组件**返 None；对 LocalLPattern 派生实例会被 SW 内部转成
    `ReferencePattern` 而非 `MirrorComponent`。`test_mirror_components_across_right_plane`
    带 `@pytest.mark.skip` + reason 留给下一轮（思路：录宏 + early binding
    加载 swdoc typelib + AccessSelections fallback）
  - L2 / L3 docstring 已明确写：mirror 失败时建议先 `add_mate` 约束后再调用，
    或 UI 手动操作

### PR #20 — `feat(solidworks): distance mate 经 AddMate5 破局`
- 合并：2026-05-23，头分支：`feat/distance-mate`
- 解锁：**distance mate 正常工作** —— 从 PR #10 挂到现在的 known-limitation
  破了。`feature.add_mate` 的 distance 分支改走 `IAssemblyDoc.AddMate5`；
  coincident / concentric 等其余类型仍走 CreateMate（不动，不冒险）
- 破局路径：**用户在 SW UI 录了一段 distance mate 宏** → 比对宏调用与脚本差异
  → 锁定 AddMate5 是 distance 的正确入口
- 新文件：`scripts/debug_addmate5.py` —— AddMate5 调用形式扫描探针
- 改动：
  - `feature.py`：新增 `_add_distance_mate`（AddMate5 路径）；`add_mate` 的
    distance 分支早返回走它；distance 选引用 mark 改 0
  - `assembly_tools.py` / `agent.py`：去掉"distance mate 是 known limitation /
    不工作"的措辞
  - `test_mate_sw.py`：`test_add_mate_distance_mate_known_limitation`
    （期望失败）→ `test_add_mate_distance`（期望成功）
  - `config/e2e_cases.yaml`：加 `asm-distance-mate` 用例
- **关键发现 1（CreateMate 不是万能）**：
  - PR #10 认定"AddMate3/4/5 已废弃，全切 CreateMate"。但 CreateMate 对
    distance **静默返回 None** —— AddMate5 才是 distance 的工作入口
  - 早期试 AddMate5 失败的真因：齿轮比 / 角度上下限传了 0；录宏显示 SW 自己
    传的是非零的 `0.001` / `π/6`（这些字段对 distance 无实义但 API 不接受 0）
- **关键发现 2（录宏文本 ≠ 真实签名）**：
  - SW UI 录出的宏 `AddMate5(...)` 文本里有 16 个值，但 late binding 实测
    AddMate5 是 **15 个参数**（官方文档签名：14 in + 1 ByRef out）
  - 照搬 16 个 → `DISP_E_TYPEMISMATCH`；`debug_addmate5.py` 扫出"15 参 +
    末位 `byref_long_variant()`"才成功
  - distance mate 选引用须 **mark=0**（CreateMate 路径用 mark=1）
- 测试：`test_mate_sw` distance 测试翻正 + `asm-distance-mate` E2E（25 cases）

### PR #19 — `feat(tools): inspect_assembly 装配体内省工具（L3 17 → 18）`
- 合并：2026-05-22，头分支：`feat/inspect-assembly`
- 解锁：**LLM 加 mate 前能"看见"装配体** —— `inspect_assembly` 把 .sldasm 的
  组件列表 / 已有配合读成文本报告，LLM 据此取组件实例名（`hub-1` / `pin-2`），
  不再靠猜。PR #18 给零件装了"眼睛"，本 PR 给装配体补上同一只眼
  （`asm-coincident-mate` E2E 此前不得不在 prompt 里手喂组件名）
- 新文件：
  - `solidworks/assembly.py`：`list_components`（实例名 / 源零件 / 位置）+
    `list_mates`（已有配合），返回纯 dataclass（ComponentInfo / MateInfo）
  - `scripts/debug_introspect_asm.py`：装配体内省探针（复用 PR #18 `_member`，
    一轮即通）
- 改动：
  - `tools/inspect_tools.py`：加第 2 个工具 `inspect_assembly`（L3 17 → 18）
  - `connection.py`：`_com` helper 从 part.py 提升到此（COM 层正确归属），
    part.py / assembly.py 共用
  - agent.py：SYSTEM_PROMPT 加"加 mate 前不确定组件名先 inspect_assembly"；
    `assembly_tools.MateRef.component_name` description 同步加引导
- **关键发现**（探针实测）：
  - `IComponent2.Name2` = 组件实例名；`GetPathName` = 源零件；`GetXform[9:12]`
    = 平移分量（米 → mm）
  - 配合不在顶层特征树，挂在 `MateGroup` 文件夹下 —— 遍历找到该文件夹再走
    `GetFirstSubFeature` / `GetNextSubFeature`，mate 类型名形如 `MateCoincident`
  - 装配体内省的 late binding 坑与零件一致，直接复用 PR #18 的 `_com`
- 测试：2 smoke + 4 SW 集成（`test_assembly_sw` 2 个 list_* + `test_layer3_tools_sw`
  2 个，含 inspect→mate 闭环）+ 1 E2E（`inspect-then-mate`：prompt 不喂组件名，
  顺序断言含 inspect_assembly）

### PR #18 — `feat(tools): inspect_part 零件内省工具（L3 16 → 17）`
- 合并：2026-05-22，头分支：`feat/inspect-part`
- 解锁：**LLM 编辑前能"看见"零件** —— `inspect_part` 把 .sldprt 的特征树 /
  边 / 面读成结构化文本报告，LLM 据此为 fillet/chamfer 选边坐标、为
  pattern/mirror 选特征名、为打孔选面坐标，不再靠猜（PR #16 demo-l-bracket
  暴露的"反复猜哪条短边"痛点）
- 新文件：
  - `solidworks/part.py`（原空预留模块填实）：`list_features` / `list_edges` /
    `list_faces` —— 返回纯 dataclass（FeatureInfo / EdgeInfo / FaceInfo），
    不漏 COM 对象、可单测；代表点统一转 mm
  - `tools/inspect_tools.py`：`inspect_part` 工具 —— 只读打开零件 → 三段文本
    报告（特征 / 边中点 / 面代表点）
  - `scripts/debug_introspect.py`：内省 API 探针（4 轮迭代）
- agent.py：注册 `ALL_INSPECT_TOOLS`（L3 16 → 17）；SYSTEM_PROMPT 加"编辑前
  不确定边坐标 / 面坐标 / 特征名就先 inspect_part"引导
- **关键发现 1（late binding 方法 / 属性误判）**：
  - SAFEARRAY 取出的 IEdge / IFace2 / ICurve 是无 TypeInfo 的动态 COMObject
  - pywin32 对其无参成员判定不稳：有的当属性（`getattr` 直接给值）、有的给
    绑定方法须再 `()` 调用、有的（返回 IDispatch 的 GetCurve / GetNextFeature）
    须先 `_FlagAsMethod`
  - 统一封 `part._com()` helper 吸收三种情况
- **关键发现 2（GetCurveParams2 尾部参数不可靠）**：
  - 探针先只扫 4 条竖直边，gp[7] 恰好 == 边长 → 泛化错；横向直线边 gp[7]≈0，
    `Evaluate(gp[7]/2)` 退化成返回端点（隐藏 bug，被 test_introspect_block 抓到）
  - 改纯几何法：直线取弦中点 + 弦长；圆 / 弧用 `ICurve.CircleParams`（圆心 +
    半径）算代表点与弧长 —— 完全不依赖 gp 尾部参数 / Evaluate
  - `ICurve.GetEndParams` 是 4 个 out 参数的方法，late binding 下报
    DISP_E_PARAMNOTFOUND，弃用
- **关键发现 3**：特征树遍历会带出 14 个系统文件夹（FavoriteFolder 等），
  `list_features` 按 GetTypeName2 过滤掉
- 测试：2 smoke（inspect_tools 路径校验）+ 4 SW 集成（`test_part_sw.py` 2 个
  核对几何 + `test_layer3_tools_sw.py` 2 个，含 inspect→fillet 闭环）+ 1 E2E
  （`inspect-then-fillet`：create → inspect_part → add_fillet 顺序断言）

### PR #17 — `docs: 沉淀 PR #13-#16 知识 + 新增 E2E + LLM 工具设计指南`
- 合并：2026-05-20，头分支：`docs/post-pr16-kb`
- 把 PR #13-#16 的 PR 史 / 覆盖率补进 DEV_LOG；新建
  `docs/E2E_AND_LLM_DESIGN.md`（E2E 框架 + LLM 工具 description 设计经验）；
  CLAUDE.md / SW_API_REFERENCE.md 同步更新

### PR #16 — `feat(test): E2E cases 12 → 22 + 修复 mate 别名 + 优化 LLM 行为`
- 合并：2026-05-19，头分支：`feat/e2e-expand-cases`
- **22/22 PASS**（v6），总耗时 310s，54 tool calls
- 新 API / 改动：
  - `feature._expand_mate_entity_aliases(name) -> list[str]`：英文 ↔ 中文 ↔
    短名（front / top / right）候选展开
  - `feature.add_mate` SelectByID2 改循环试候选 —— 任何 LLM 形式填都工作
  - `assembly_tools.AddMateSpec` docstring 重写为 limitations 排序表
    （coincident > parallel > concentric face-only > distance broken），
    **同时强调"用户要求时仍调"**
- **关键发现 1（Layer 2 真 bug）**：
  - 中文 SW 下 SelectByID2 是字面匹配 entity_name
  - LLM 填 "Front Plane" → 选不中（必须 "前视基准面"）
  - 修复后 asm-concentric-mate 从 17 mate calls → 5 calls
- **关键发现 2（LLM tool description trade-off）**：
  - v4 description 无限制 → LLM 反复试错（17 calls）
  - v5 description "不推荐 concentric" → LLM 直接跳过 mate
  - v6 description 说明 limitations + "用户要求时仍调" → 5 calls 完成
  - **沉淀到 docs/E2E_AND_LLM_DESIGN.md §3**
- 扩展用例（10 个）：cyl-chamfer / block-mirror-hole / block-linear-pattern /
  asm-coincident-mate / asm-save-rebuild / block-hw-blind / demo-flange-disk /
  demo-l-bracket / demo-bushing / inch-unit

### PR #15 — `feat(test): E2E LLM 链路测试（12/12 通过）`
- 合并：2026-05-18，头分支：`feat/e2e-llm-tests`
- 解锁：**用户中文 → LLM → 工具 → SW → 文件**全链路自动化验证
- 新文件：
  - `scripts/agent_e2e_runner.py`：独立 runner，提取 tool_calls + final_output
    + usage 为结构化 JSON
  - `config/e2e_cases.yaml`：12 个数据驱动用例
  - `tests/test_agent_e2e_sw.py`：pytest 包装（parametrize 自动展开）
- 改动：
  - `tests/conftest.py`：加 `--agent` flag + `@pytest.mark.agent`
  - `src/mech_pilot/solidworks/connection.py`：**关键修复** —— `get_app`
    顶部 `pythoncom.CoInitialize()` + dispatch retry
- **关键发现：跨子线程 COM apartment**
  - pydantic-ai 的 `agent.run_sync` 在**子线程**跑 tool（不是 main thread）
  - 未初始化子线程的 COM apartment 让 Dispatch 失败
  - 第 1 个 case 偶然成功（win32com cache 残留），第 2+ 个 100% 失败
  - 修复后跨 case 稳定 12/12 PASS
  - **沉淀到 docs/SW_API_REFERENCE.md §2.5**
- 成本：DeepSeek v4-flash，12 case ~122s ~¥0.15

### PR #14 — `feat(tools): Layer 3 agent tools impl (16 工具暴露给 LLM)`
- 合并：2026-05-17，头分支：`feat/agent-tools-impl`
- 解决 mech-pilot **最大裂痕** —— L3 工具从 1/16 → **16/16**
- 新文件（5 个 tools 模块）：
  - `tools/modeling_tools.py`：create_cylinder / rectangular_block / cone / tube
  - `tools/editing_tools.py`：add_fillet / chamfer / simple_hole / hole_wizard *to_part*
  - `tools/pattern_tools.py`：pattern_linear / circular / mirror_feature *in_part*
  - `tools/assembly_tools.py`：create_assembly_from_parts / add_mate_between_parts / save_assembly
  - `tools/export_tools.py`：export_to_step / export_to_stl
- **Layer 2 顺手修复**：
  - `connection.open_assembly(app, path)` —— assembly_tools 依赖
  - `feature.add_mate`：strip `asm_title` 的 `.sldasm` 后缀（reopen 装配体后
    GetTitle 带 ".SLDASM" 后缀，SelectByID2 path 不能含；这是 reopen 模式才
    暴露的潜伏 bug）
- agent.py 合并 5 个 `ALL_*_TOOLS` 列表注册全部；扩展 SYSTEM_PROMPT 按
  "创建 → 编辑 → 阵列 → 装配 → 导出"工作流引导 LLM
- 测试：20 个 smoke (Pydantic spec) + 12 个集成 (SW happy path) + 全套 100
  个 SW 测试无退化

### PR #13 — `feat(solidworks): add_hole_wizard via HoleWizard5 (ANSI Inch path)`
- 合并：2026-05-17，头分支：`feat/hole-wizard`
- 解锁：`feature.add_hole_wizard(model, plane, diameter_mm, *, depth_mm=None,
  through_all=False)` —— SW Hole Wizard 工艺孔（导出 STEP 被识别为标准孔）
- **关键转折点**：穷尽枚举 + 复刻官方 C# 示例破局
  - HoleWizard5 = **27 参数**（按 `sldworksapi.chm` 官方 VB 签名）
  - 解 `swconst.chm` 拿 `swWzdGeneralHoleTypes_e` / `swWzdHoleStandards_e` /
    `swStandardAnsiInchFastenerTypes_e` 全部枚举
  - **唯一稳定工作组合**（22 组合扫出来）：
    `hole_type=swWzdHole(2) + standard=AnsiInch(0) + fastener=FractionalDrillSizes(19)
     + size="1/4" + Selection=Plane mark=0`
  - Diameter 与 size 字符串**语义独立** —— size 只是 SW 内部分类索引
  - 已确认不工作（已知 limitation）：GB / Metric / ISO standard、
    Face / SketchPoint selection、CounterSink / Tap 类型、位置控制
- 7 个集成测试 + 探针留 `scripts/debug_hole_wizard_v3.py`

### PR #12 — `chore(config): register deepseek v4-flash/pro + fix base_url`
- 合并：2026-05-17，头分支：`feat/agent-tools-layer3`
- `config/models.yaml`：新增 `deepseek:deepseek-v4-flash` / `deepseek-v4-pro`
  条目；`deepseek-chat` 的 base_url 改为 `https://api.deepseek.com`

### PR #11 — `docs: project knowledge base for new Claude sessions`
- 合并：2026-05-16，头分支：`docs/knowledge-base`
- 新建 `CLAUDE.md` + `docs/DEV_LOG.md` + `docs/SW_API_REFERENCE.md`

### PR #10 — `feat(solidworks): add_mate via CreateMate (装配体闭环)`
- 合并：2026-05-16，头分支：`feat/mate-holewiz-shell`，commit: `9fb46e7`
- 解锁：`feature.add_mate(asm, ref1, ref2, mate_type)` + `SW_MATE_TYPE` 常量表
  （coincident / concentric / perpendicular / parallel / tangent / distance /
  angle / lock / symmetric / width）
- **关键转折点：读官方文档破局**
  - `AddMate3 / 4 / 5` 全部 "Obsolete, use CreateMate"
  - silent fail（`r=None, err=0`）= deprecated API 征兆
  - 正确入口 `IAssemblyDoc.CreateMateData(type) → CreateMate(data)`
- **Known limitation: distance mate** — `CreateMate(distance_data)` 返回 None
  - PR #10 已写 known-limitation 测试锁定
  - **本地分支 `feat/distance-mate-fix`（commit `0121f28`，未 PR）**：
    穷尽 36 组合 + 用 SW 官方 cylinder20.sldprt 复刻 C# 示例仍失败 ——
    确认是 SW 2026 + late binding 的根本性 broken，需录 SW UI 宏破局
  - 详 `scripts/debug_distance_mate.py`

---

## 已合并 PR（早期）

### PR #9 — `feat(solidworks): assembly phase 1 — new_assembly + insert_component`
- 合并：2026-05-16，头分支：`feat/assembly`，commits: `ed41c21`
- 新 API：
  - `connection.new_assembly(app)` —— 自动定位 `.asmdot` 模板
  - `connection.insert_component(asm, part_path, *, x_mm, y_mm, z_mm,
    configuration="", app=None)`
- 关键发现：
  - **`AddComponent5` 不会自动加载零件文件** —— 直接调返回 None
  - Workaround：先 `OpenDoc6` 预加载零件到 SW 内存，再 AddComponent5
- 撤回：`add_mate` selection-state 谜题，留到 PR #10

### PR #8 — `feat(solidworks): I/O + draw_arc / draw_polygon`
- 合并：2026-05-16，头分支：`feat/io-arc-dim-holewizard`，commits: `8062af0`
- 新 API：
  - `connection.open_part(app, path)` + `connection.export_part(model,
    path, format=None)` — 支持 STEP / IGES / STL / Parasolid
  - `connection.byref_long_variant()` —— **第三个 VARIANT 工厂**
  - `sketch.draw_arc(model, cx, cy, r, start_deg, end_deg, *, direction)`
  - `sketch.draw_polygon(model, cx, cy, circumscribed_radius, n_sides,
    *, inscribed=False)`
- 关键发现：
  - **SW out 参数（ByRef Long）必须 `VARIANT(VT_BYREF | VT_I4, 0)`**
  - 传 Python int 或 empty_variant() 都 DISP_E_TYPEMISMATCH
  - `OpenDoc5` 在 SW 2026 上不存在（OpenDoc4 → 6 间断）
- 撤回：
  - `add_dimension`：`model.AddDimension2(x,y,z)` 让 SW 进程崩溃
    （RPC_E_DISCONNECTED = -2147023170）
  - `add_hole_wizard`：调研到 HoleWizard5 27 参数但 selection 不识

### PR #7 — `feat(solidworks): add add_simple_hole convenience wrapper`
- 合并：2026-05-16，头分支：`feat/hole-wizard`，commits: `345a4f0`
- 新 API：`feature.add_simple_hole(model, face_xyz_mm, position_mm,
  diameter_mm, *, depth_mm=None, through_all=False)`
- 关键发现：
  - **`ISketch.Name` 是属性**（不是 `GetName()` 方法）—— 用 .Name 拿草图名
  - 借此实现"动态草图名定位"：不再依赖 `Sketch{N}` 硬编码
  - 同一面上能连续打多孔（之前要手算 sketch 序号）
- 撤回：`add_hole_wizard` 因 27 args + 标准件库索引留下个 PR

### PR #6 — `feat(solidworks): add mirror + linear pattern + circular pattern`
- 合并：2026-05-16，头分支：`feat/patterns`，commits: `69041d9`
- 新 API：
  - `feature.mirror_feature(asm, plane, feature_name)`
  - `feature.pattern_linear(model, direction_edge_mm, feature_name, count,
    spacing_mm, *, flip=False, direction2_edge_mm=None, count2=1, ...)`
  - `feature.pattern_circular(model, axis_xyz_mm, feature_name, count,
    total_angle_deg=360)`
  - `sketch.select_axis(model, x, y, z, *, mark=0, append=False)`
  - `sketch.select_plane / select_feature` 升级支持 mark/append + 9 对
    英中前缀别名表
- 4 个关键坑（已沉淀到代码注释）：
  1. Mirror selection-mark `(plane=2, feature=1)` ≠ Pattern `(1, 4)`
  2. `InsertMirrorFeature2` 在 SW 2026 上 **5 参数**（文档普遍 4）；第 5 个
     必须 `empty_variant()`
  3. `FeatureCircularPattern3` 第 5 个 `EqualSpacing` **必须 False**（与文档反）
  4. 临时轴 type 字符串是 **"TEMPAXIS"**（不是 "AXIS"）

### PR #5 — `feat(solidworks): add revolve + draw_line / draw_centerline primitives`
- 合并：2026-05-15，头分支：`feat/revolve`，commits: `fd5eff8`
- 新 API：
  - `sketch.draw_line(model, x1, y1, x2, y2)`
  - `sketch.draw_centerline(model, x1, y1, x2, y2)`
  - `feature.revolve(model, angle_deg=360.0, reverse=False)`
- 关键发现：
  - **`FeatureRevolve2` 在 SW 2026 需要 20 参数**（文档说 15；多 5 个尾部 Variant）
  - `FeatureRevolve3 / 4` / `InsertRevolve` / `FeatureRevolveBoss` 都不存在

### PR #4 — `feat(solidworks): add fillet / chamfer primitives`
- 合并：2026-05-15，头分支：`feat/fillet-chamfer`，commits: `ab9e7e1`
- 新 API：
  - `feature.add_fillet(model, edges_mm, radius_mm)` —— 支持多边一次
  - `feature.add_chamfer(model, edges_mm, distance_mm)`
  - `connection.null_dispatch_variant()` / `empty_variant()` —— VARIANT 工厂
  - `sketch.select_face / select_edge` 升级支持 mark / append
- 3 个核心坑（已沉淀）：
  1. **`Extension.SelectByID2` 的 Callout 必须 `VARIANT(VT_DISPATCH, None)`**
     传 Python None 就 DISP_E_TYPEMISMATCH
  2. **`FeatureFillet3` 的 Options 必须 = 2 而不是 1**（公开示例都用 1，
     在 SW 2026 上 silent fail）
  3. **`InsertFeatureChamfer` 在 SW 2026 需要 8 参数**（文档 5/6）
- 顺手：同步把"自动关闭 SW 文档"清理 fixture 也并入

### PR #3 — `feat(solidworks): select_face / select_edge / extrude_cut primitives`
- 合并：2026-05-15，头分支：`feat/face-edge-extrude-cut`，commits: `c5d692d`
- 新 API：
  - `sketch.select_face(model, x_mm, y_mm, z_mm)` —— 按 3D 坐标选面
  - `sketch.select_edge(model, x_mm, y_mm, z_mm)` —— 按 3D 坐标选边
  - `feature.extrude_cut(model, *, depth_mm, through_all, flip_direction)`
- 关键发现：
  - **`FeatureCut3` 在 SW 2026 上签名 23 → 26**（多 3 个尾部 bool）
  - 中英文别名自动互译：`Sketch{N} ↔ 草图{N}`

### PR #2 — `feat: end-to-end create_cylinder via SolidWorks COM`
- 合并：2026-05-15，头分支：`feat/cylinder-end-to-end`，commits: `a687064`
- **MVP 第一条端到端链路打通**：自然语言 → LLM → 工具 → SW → `.sldprt` 落盘
- 关键发现：
  - `Extension.SelectByID2` 在 late binding 下有 type mismatch（后续 PR
    才彻底搞懂 VARIANT(VT_DISPATCH) workaround）
  - 用户的 SW 默认零件模板被误设为 `.asmdot`，加了**自动扫描 `.prtdot`
    兜底**
  - SW 中文 UI："Front Plane" 实际叫"前视基准面"，候选列表兜底
  - `EnsureDispatch` 在 SW 上失败（IDispatch 不暴露 TypeInfo）—— 项目锁
    定 late binding

### PR #1 — `feat: initialize project skeleton (pydantic-ai + uv)`
- 合并：2026-05-14，头分支：`feat/initial-skeleton`，commits: `74dc362`
- 项目骨架：uv + pyproject.toml + 整套 src 布局 + smoke 测试
- 关键决策：
  - **Pydantic AI** 而非 Claude Agent SDK（模型无关；要兼容 Claude/OpenAI/
    DeepSeek/GLM/Kimi）
  - **`uv`** 管理依赖（不用 pip）
  - Windows-only；pywin32 标 `sys_platform == "win32"`
  - master 分支保护开启

---

## 当前操作覆盖率（master 上，截至 PR #20 合并后）

### Layer 2（SolidWorks 业务封装）

| 层级 | 已实现 | 覆盖率 | 备注 |
|---|---|---|---|
| 文档操作 | 9 / 9 | **100%** | get/new/open_part/**open_assembly**/save/close/export 全套 |
| 选择系统 | 6 / 6 | **100%** | + select_vertex（PR #26）|
| 草图原语 | 7 / 9 | 78% | 缺 ellipse / spline |
| 特征建模 | 9 / 15 | **60%** | +add_hole_wizard (ANSI Inch only) / 拉伸 / 旋转 / cut / fillet / chamfer / mirror / pattern × 2 / simple_hole |
| 装配体阵列 | 3 / 3 | **100%** | local linear / local circular / mirror 组件版（mirror 对自由组件有 limitation） |
| 拉伸模式 | 3 / 4 | 75% | blind / through_all / mid_plane（PR #26）；缺 up_to_surface |
| 导入导出 | 3 / 3 | **100%** | STEP / IGES / STL / Parasolid |
| 装配体 | 3 / 5 | 60% | new_assembly + insert_component + add_mate（distance 经 AddMate5；含别名兜底） |
| 草图约束 | 0 / 3 | 0% | dimension / relation 完全未做 |
| 工程图 | 0 / 5 | 0% | IDrawingDoc 未涉足 |
| 高级（配置 / 方程式） | 0 / 4 | 0% | 依赖 add_dimension 先落地 |
| 零件内省 | 3 / 3 | **100%** | list_features / list_edges / list_faces（PR #18） |
| 装配体内省 | 2 / 2 | **100%** | list_components / list_mates（PR #19） |
| GB 螺纹孔 | 1 / 1 | **100%** | add_threaded_hole（M3-M12 GB 粗螺距，PR #24）|
| GB 沉孔 | 2 / 2 | **100%** | add_counterbore_hole（inner_hex M3-M12 / **hex_bolt M3-M12 PR #28**）+ add_countersink_hole（M6-M12，PR #25）|
| 复杂曲面 | 2 / 4 | 50% | add_sweep + add_loft（PR #27）；缺 boundary surface / fill surface |
| 参数化 | 2 / 3 | 67% | modify_dimension + list_dimensions（PR #29 特征级）；缺 add_dimension（草图级，受 AddDimension2 崩溃 limit）|
| **L2 总计** | **55 / 82** | **~67%** | |

### Layer 3（Agent 工具）

| 模块 | 工具数 | E2E 覆盖 |
|---|---|---|
| modeling_tools | 6 (cylinder / block / cone / tube / **swept_pipe** / **lofted_transition**) | 5+2 |
| editing_tools | 8 (fillet / chamfer / simple_hole / hole_wizard / threaded_hole / counterbore / countersink / **modify_dimension**) | 4+2+2+1 |
| pattern_tools | 6 (零件 linear/circular/mirror + 组件 linear/circular/mirror) | 3+3 |
| assembly_tools | 3 (create_asm / add_mate / save_asm) | 3/3 |
| export_tools | 2 (to_step / to_stl) | 2/2 |
| inspect_tools | 2 (inspect_part / inspect_assembly) | 2/2 |
| **L3 总计** | **27 / 27** | **36 cases 直接覆盖（含 1 个 modify_dimension PR #29）** |

### 测试矩阵（截至 PR #29）

| 类别 | 数量 | 触发 |
|---|---|---|
| smoke (不需 SW) | ~82（+5 个 modify_dimension spec smoke） | `pytest -q` |
| @pytest.mark.sw 集成测试 | ~167（+8 个 modify_dimension） | `pytest --solidworks -q` |
| @pytest.mark.agent E2E | **36**（+1 modify-extrude-depth） | `pytest --solidworks --agent -q` |
| **总计** | **~285** | 全套 ~15 min |

---

## 待开发清单（按 ROI 排序）

### ⭐⭐⭐⭐ 高（关键 capability）

1. ✅ **已完成（PR #20）—— distance mate 破局**
   - 用户录 SW UI 宏 → 锁定 distance mate 走 `IAssemblyDoc.AddMate5`
     （15 参，引用 mark=0，齿轮比 / 角度限必须非零）
   - `feature.add_mate` 的 distance 分支已切到 AddMate5，正常工作

2. ✅ **已完成（PR #18）—— `inspect_part` 内省工具**
   - 合并为单个 `inspect_part`：特征树 + 所有边中点坐标 + 所有面代表点的
     结构化文本报告（不是计划里的两个分开工具，理由见 PR 设计）
   - demo-l-bracket 等用例 LLM 反复猜"哪条短边"的痛点已解

3. **hole_wizard GB / Metric / 位置控制**（PR #13 续作）
   - 探针框架现成：`scripts/debug_hole_wizard_v3.py`
   - 缺 GB / Metric 对应的 `fastener_type` 真值 —— 录 SW UI 宏对齐
   - 位置控制走 `Create_Holes_Using_Hole_Wizard_and_Sketch_Points_Example_CSharp.htm`

### ⭐⭐⭐ 中（覆盖率补齐）

4. ✅ **已完成（PR #21）—— 装配体组件阵列三件套**
   - L2: add_local_linear_pattern_components / add_local_circular_pattern_components
     / add_mirror_components（L2 总计 → 45）
   - L3: 同名 3 个 LLM 工具（L3 17 → 21）
   - Linear / Circular 走 CHM mark 表（edge/axis=2, component=1）；Mirror
     走 setter 路径（`ComponentsToInstanceAlignToComponentOrigin`）
   - **遗留**：`add_mirror_components` 对自由组件返 None（已 mark.skip 测试
     + docstring 标注 limitation），下轮探针破

4b. ✅ **已完成（PR #21 同 PR 内补完）—— mirror_components happy path 破局**
   - 走 `IAssemblyDoc.MirrorComponents3` 一次性 14 参 API（SW UI 录宏揭示）
   - ComponentsToInstance / ComponentOrientations 必须用
     `VARIANT(VT_ARRAY|VT_*)` SAFEARRAY 显式构造（Python tuple 静默返 None）

4c. ✅ **已完成（PR #22）—— 组件阵列 3 个 E2E case**
   - asm-linear-pattern-components / asm-circular-pattern-components /
     asm-mirror-components 3/3 通过

5. ✅ **已完成（PR #26）—— `select_vertex` + `extrude` 多模式**
   - extrude end_condition='blind' / 'through_all' / 'mid_plane' 全支持
   - select_vertex 加进 sketch.py
   - 仍缺 extrude 'up_to_surface'（需选 face 作为终止参考，下次按需）

6. ✅ **已部分完成（PR #29）—— 参数化迭代设计**
   - modify_dimension + list_dimensions（特征级 Parameter）OK
   - **遗留**：add_dimension（草图级智能尺寸）AddDimension2 仍崩，
     未试 mark-based selection / 录宏内部隐藏 selection state
     重现路径。下一轮可深挖。

### ⭐⭐ 较低（新模块 / 战略方向）

7. **MCP server 适配** —— 让 mech-pilot 作为 Claude Desktop / Cursor 的工具
8. **跨 provider E2E 测试矩阵** —— Claude / OpenAI / GLM 验证 spec 跨 LLM 兼容
9. **CLI UX 改进 + demo 视频** —— 项目可见度
10. **工程图（IDrawingDoc + add_standard_views + auto_dimension）**

### ⭐ 低（阻塞 / 需特殊路径）

11. `shell` —— SW 2026 上 API **完全不存在**，只能走 `swApp.RunCommand` macro
12. `rib` —— `InsertRib` 10 参数 selection 不识，查 `IRibFeatureData2` 深潜
13. `sweep` / `loft` / `draft` / `thread` —— 复杂曲面 / 工艺特征
14. 配置 / 方程式 —— 依赖 add_dimension 先落地

---

## 测试与 CI

- 单元测试（不需要 SW）：`uv run pytest -q` —— SW / agent 测试 skip
- SW 集成测试：`uv run pytest --solidworks -q`
- E2E LLM 测试：`uv run pytest --solidworks --agent -q tests/test_agent_e2e_sw.py`
- E2E 独立 runner（详细 JSON 输出）：`uv run python scripts/agent_e2e_runner.py`
- `tests/conftest.py` autouse fixture：每个 SW 测试跑完 `close_all_documents`，
  SW 进程保留（热 Dispatch）
- 调试脚本：`scripts/debug_*.py` —— probe 工具 / 复现 bug；25 个保留供未来扩展

---

## 项目知识体系（项目级，不依赖全局）

| 文件 | 用途 |
|---|---|
| `CLAUDE.md` | 新会话入口（30 秒概览，4 件事） |
| `docs/DEV_LOG.md`（本文件） | PR 史 + 覆盖率 + 待开发清单 |
| `docs/SW_API_REFERENCE.md` | SW API 知识库（CHM 路径 + 签名漂移 + mark + VARIANT + **CoInitialize** + **中英文别名兜底**） |
| `docs/E2E_AND_LLM_DESIGN.md` | E2E 测试框架 + LLM 工具 description 设计经验（PR #15/#16 沉淀） |
| `plan-pydantic-ai.md` | 项目初始设计文档（基本实现） |
| `plan-claude-agent-sdk.md` | 历史归档（被 Pydantic AI 版替代） |
| `src/mech_pilot/solidworks/*.py` 代码注释 | 每个踩过的坑都写在 API docstring 里 |
| `scripts/debug_*.py` | 25 个调试脚本，覆盖每次 probe 的现场 |
| `config/e2e_cases.yaml` | 25 个 E2E 用例，覆盖全部 18 个 L3 工具 |
