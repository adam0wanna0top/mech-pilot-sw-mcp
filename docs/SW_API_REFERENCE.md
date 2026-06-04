# SolidWorks API 参考（项目级知识库）

本文件浓缩 mech-pilot 项目积累的全部 **SolidWorks 2026 + pywin32 late binding**
经验。新会话查这里就够了，不要再盲目 probe args。

---

## 0. TL;DR — 新 API 实现流程

1. **先查文档** —— 本地 CHM > 在线官网 > probe args（最后选）
2. **核对签名漂移** —— SW 2024+ 多数 Feature API 尾部加了 3-5 个 Variant 占位
3. **核对 selection-mark 约定** —— 不同 feature 用不同 mark（见 §4）
4. **VARIANT 三件套** —— SW out 参数 / IDispatch null / Variant 数组各有专用工厂
5. **Silent fail = API obsolete** —— `r=None, err=0` 多半是 SW 故意保留的 deprecated API；查官方文档找新入口

---

## 1. 文档资源（按速度排序）

### 1.1 本地 CHM（首选，离线，快）

| CHM | 用途 | 大小 |
|---|---|---|
| `G:\solidwork\SOLIDWORKS Corp2026\SOLIDWORKS\api\sldworksapi.chm` | **主 API 文档（27 MB）** | 27 MB |
| `G:\solidwork\SOLIDWORKS Corp2026\SOLIDWORKS\api\swconst.chm` | **所有枚举值表** | 8 MB |
| `.../sldworksapiprogguide.chm` | API 编程指南（套路说明） | 550 KB |
| `.../obsoleteapi.chm` | 已废弃 API 列表（重要！查 silent fail） | 2.3 MB |
| `.../swcommands.chm` | UI 命令 ID（macro / RunCommand 用） | 220 KB |
| `.../sldworksapivb6.chm` | VBA 版示例代码 | 5 MB |
| `.../routingapi.chm` / `cworksapi.chm` / 等 | 专项模块（routing / cworks 等） | 各异 |

### 1.2 解出的 HTML（已缓存，但会随系统重启丢失）

路径：`C:\Users\Hello\AppData\Local\Temp\sw_api_help\`（17616 个 HTML 页）

**重新解压（如缓存丢失）**：
```powershell
$dest = "C:\Users\Hello\AppData\Local\Temp\sw_api_help"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
# 先复制到本地（hh.exe 在 G: 上有时失败）
Copy-Item "G:\solidwork\SOLIDWORKS Corp2026\SOLIDWORKS\api\sldworksapi.chm" "$env:TEMP\sw.chm"
Start-Process hh.exe -ArgumentList '-decompile', $dest, "$env:TEMP\sw.chm" -Wait -NoNewWindow
(Get-ChildItem $dest -Recurse | Measure-Object).Count    # 应该 ~17616
```

**查方法签名**：
```powershell
# 1. 列方法相关页面
Get-ChildItem "C:\Users\Hello\AppData\Local\Temp\sw_api_help" -Filter "*AddMate5*"

# 2. 解出文本
cat "<path>/.../IAssemblyDoc~AddMate5.html" |
  sed 's/<[^>]*>//g' | tr -s ' \n' '\n' > /tmp/x.txt

# 3. 找声明行
grep -n "ByVal\|ByRef\|Function" /tmp/x.txt
```

### 1.3 在线官方文档（兜底）

- `https://help.solidworks.com/2024/english/api/sldworksapi/`（按年份切换）
- `https://help.solidworks.com/SearchApi/`（全文搜索）

**用法**：本地 CHM 找不到时，用 `WebFetch` 拉对应 URL：
```
WebFetch(
  url="https://help.solidworks.com/2024/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~FeatureFillet3.html",
  prompt="提取 FeatureFillet3 的完整参数列表和返回类型"
)
```

---

## 2. VARIANT 三件套（封装在 `connection.py`）

SW + pywin32 late binding 下，**3 种特殊类型的参数必须显式包装**：

| 工厂函数 | VARIANT 类型 | 用途场景 |
|---|---|---|
| `null_dispatch_variant()` | `VT_DISPATCH, None` | SW API 中的 `Callout As IDispatch` / 类似可空 IDispatch* 参数 |
| `empty_variant()` | `VT_EMPTY, None` | SW API 中的 `As Variant`（数组型可选参数） |
| `byref_long_variant()` | `VT_BYREF \| VT_I4, 0` | SW API 中的 `ByRef Errors As Long` / 类似 out 参数 |

**踩坑现场**：
- `Extension.SelectByID2` 的第 8 个参数 Callout：传 Python `None` → DISP_E_TYPEMISMATCH at arg 8。改 `null_dispatch_variant()` 即过。
- `OpenDoc6` 的 errors/warnings：传 Python `0` → DISP_E_TYPEMISMATCH at arg 5。改 `byref_long_variant()` 即过。
- `FeatureFillet3` 末尾 4 个 paiRadiusVariable 等：传 `None` 或 `()` 都让 fillet 返回 None。改 `empty_variant()` 才工作。

**调用范例**：
```python
from mech_pilot.solidworks.connection import (
    null_dispatch_variant, empty_variant, byref_long_variant,
)

# Selection（Callout 是 null IDispatch*）
model.Extension.SelectByID2(
    "前视基准面@part1-1@装配体1", "PLANE", 0, 0, 0,
    False,                       # append
    1,                           # mark
    null_dispatch_variant(),     # Callout —— 关键！
    0,                           # SelectOption
)

# OpenDoc6（errors/warnings 是 ByRef Long out 参数）
errors = byref_long_variant()
warnings = byref_long_variant()
model_or_None = app.OpenDoc6(path, 1, 0, "", errors, warnings)
print(errors.value, warnings.value)    # 调用后可读

# FeatureFillet3（末尾 4 个 paiXxx 是 Variant 数组）
fm.FeatureFillet3(
    2, r, 0.0, 0.0, 0, 0, 0, 0,
    empty_variant(), empty_variant(), empty_variant(), empty_variant(),
)
```

---

## 2.5. 跨子线程 COM apartment（PR #15 关键修复）

### 现象
`pydantic-ai` 的 `agent.run_sync` 在**子线程**跑 tool（不是 main thread）：

```python
main thread: 19656
!! get_app called in thread 18312   # <- 子线程
```

未初始化子线程的 COM apartment 会让 `client.Dispatch(SW_PROG_ID)` 失败 ——
但 SW 进程明明在跑（`client.GetActiveObject` 在 main thread 能用）。

具体症状：E2E 跑 12 cases，第 1 个偶然成功（win32com cache 残留），
**第 2 个 case 开始 100% `[error] [get_app] SolidWorks 不可用`**。

### 根因
Windows COM 要求每个线程在调用 COM 前显式调用 `CoInitialize()`：
- main thread：Python 启动时 pywin32 自动调
- 子线程：**必须手动调** —— 不然 `Dispatch` 失败

### 修复（已落到 `connection.get_app`）

```python
def get_app(start_if_not_running=True, _retry_count=2, _retry_delay_s=2.0):
    client = _import_win32com()
    # 跨子线程 COM apartment 必须显式 init（幂等：已 init 时 pythoncom 返回 S_FALSE）
    try:
        import pythoncom
        pythoncom.CoInitialize()
    except Exception:
        pass

    last_exc = None
    for attempt in range(_retry_count + 1):
        try:
            app = client.Dispatch(SW_PROG_ID) if start_if_not_running else client.GetActiveObject(SW_PROG_ID)
            ...
            return app
        except Exception as exc:
            last_exc = exc
            if attempt < _retry_count:
                time.sleep(_retry_delay_s)
    raise SolidWorksUnavailableError(...) from last_exc
```

### 副作用：避免 main thread 早期 Dispatch
E2E runner 起初在 main thread 调 `is_available()`（内部 `GetActiveObject`），
然后 LLM 子线程的 `Dispatch` 也失败。**已删除** `runner` 里的早期检测调用，
让工具自己在子线程里发现 SW 状态。

### 适用范围
任何**不在 main thread 直接调用**的 SW COM 场景都需要 CoInitialize：
- pydantic-ai / LangGraph / Claude Agent SDK 的 tool 子线程
- asyncio 配合 `run_in_executor`
- 任何 `threading.Thread` / `ThreadPoolExecutor`
- 未来 MCP server / Web UI 场景

**已落到 `connection.get_app`，所有 L2/L3 调用透传受益**。

---

## 2.6. C# 早绑定下的 VARIANT 对应（M4 add_fillet 实证）

项目已迁到 C# .NET 8 + 官方 Interop DLL（**早绑定**）。§2 的 Python VARIANT 三件套
在早绑定下大多**不需要手动包装** —— CLR 的 COM marshaler 自动转换。M4 `add_fillet`
（首个"编辑已有零件"工具）实测对应关系：

| Python late binding（§2） | C# 早绑定 | 实测 |
|---|---|---|
| `empty_variant()`（VT_EMPTY 可选数组参数） | **`null`**（object 形参直接传 null） | FeatureFillet3 尾部 7 个 Object 数组参数全传 `null`，一次成功 |
| `byref_long_variant()`（ByRef Long out） | **`ref int`** | OpenDoc6 / SaveAs 的 Errors/Warnings：声明 `int x = 0;` 后 `ref x` 即可 |
| `null_dispatch_variant()`（可空 IDispatch） | `null`（类型化接口形参） | （本工具未用，按 marshaler 规则应同样自动） |

**关键反转**：v1 Python 下 FeatureFillet3 尾部传 `None` / `()` 会让 fillet
**返回 None**，只有 `empty_variant()` 才工作（§2 line 87）。**C# 早绑定下传 `null`
即是 VT_EMPTY，第一次就成** —— 不需要任何特殊工厂。CLR 的 null→VARIANT 规则比
pywin32 的 None→VARIANT 更贴近 SW 期望。

**FeatureFillet3 C# 签名**（反射 `SolidWorks.Interop.sldworks.dll` 实证，**14 参**）：
```
Options(int), R1(double), R2(double), Rho(double), Ftyp(int),
OverflowType(int), ConicRhoType(int),            ← 7 个标量
Radii, Dist2Arr, RhoArr, SetBackDistances,
PointRadiusArray, PointDist2Array, PointRhoArray  ← 7 个 Object 数组（全传 null）
```
等半径全边倒角调用：`Options=swFeatureFilletUniformRadius(2)`、
`Ftyp=swFeatureFilletType_Simple(0)`、`OverflowType=swFilletOverFlowType_Default(0)`、
`R1=半径(米)`，其余标量 0，7 个数组传 `null`。**注意 Options=2 是 UniformRadius，
不是 KeepFeatures(128)**（§4 表已更正）。

**边选择**（遵循黄金法则 #6，不用坐标 SelectByID2）：
`(IPartDoc).GetBodies2(swSolidBody, false) → (IBody2).GetEdges() →
(IEntity).Select2(Append:true, Mark:1)`，逐边 append。mark=1 是 FeatureFillet3
要求（§4）。

**OpenDoc6 C# 签名**（6 参）：`FileName, Type(int=swDocPART=1),
Options(int=swOpenDocOptions_Silent=1), Configuration(string),
ref Errors(int), ref Warnings(int)` → 返回 `IModelDoc2`（null = 打开失败，
读 errors/warnings 位诊断）。

---

## 3. 签名漂移表（SW 2024+ 比公开文档多 args）

SW 2024+ 在多个 Feature API **尾部**加了若干 Variant 占位（实测，文档没说）。

| API | 文档签名 | SW 2026 实际 | 差额 |
|---|---|---|---|
| `IFeatureManager.FeatureCut3` | 23 args | **26 args** | +3 bool（AssemFeatScope 等） |
| `IFeatureManager.FeatureRevolve2` | 15 args | **20 args** | +5 Variant |
| `IFeatureManager.InsertFeatureChamfer` | 5-6 args | **8 args** | +3 Variant 尾部 |
| `IFeatureManager.InsertMirrorFeature2` | 4 args | **5 args** | +1 Variant 尾部 |
| `IFeatureManager.HoleWizard5` | 27 args (官方 VB 签名) | **27 args** 实测 | 字段语义比文档复杂（见 §6） |
| `IAssemblyDoc.AddComponent5` | 8 args | **8 args**（但需 OpenDoc6 预加载） | 行为不同 |

**新 API 试错套路**：
1. 找文档的"公开签名" N
2. 尝试 `fn(arg1..argN)` —— 通常 `DISP_E_PARAMNOTFOUND`（缺参数）
3. 加 3 个 `empty_variant()` 尾部：`fn(arg1..argN, e, e, e)` —— 多半通
4. 不行就 +5，逐步加；超出就 `'无效的参数数目'`，回退
5. 通过后存到这张表里

---

## 4. Selection-mark 约定表

不同 feature 要求被选元素带不同的 mark。**官方文档每个 feature 页的 Remarks
部分都会写**——必须查清楚再调。

| Feature API | 元素 1 mark | 元素 2 mark | 备注 |
|---|---|---|---|
| `FeatureExtrusion3` / `FeatureCut3` | 草图 mark=0 即可 | — | 选 sketch 时不需要 mark |
| `FeatureRevolve2` | 草图 mark=0 + 含 centerline | — | centerline 自动作轴 |
| `FeatureFillet3` | 边 **mark=1** | — | **Options=2** = swFeatureFilletUniformRadius（**非** KeepFeatures——那是 128）；C# 早绑定签名详 §2.6 |
| `InsertFeatureChamfer` | 边 mark=0 | — | 用默认 select_edge 即可 |
| `FeatureLinearPattern2` | 方向边 mark=1 | 阵列 seed **mark=4** | dir2 用 mark=2 |
| `FeatureCircularPattern3` | 轴 mark=1 | seed mark=4 | **EqualSpacing=False** |
| `InsertMirrorFeature2` | 镜像面 **mark=2** | seed feature **mark=1** | **顺序与 pattern 相反！** |
| `IAssemblyDoc.CreateMate` | ref1 mark=1 | ref2 mark=1 | coincident / concentric / parallel / ... 用此；mark=1（CamFollower=8, Width=16）|
| `IAssemblyDoc.AddMate5`（distance） | ref1 **mark=0** | ref2 **mark=0** | **distance mate 专用** —— CreateMate 对 distance 静默返回 None，改走 AddMate5（详 §8）|
| `HoleWizard5` | **Plane mark=0** (官方示例) | — | 仅 ANSI Inch + FractionalDrillSizes 路径稳定（详 §6 hole wizard 段） |
| **组件**线性阵列<br>`CreateDefinition(swFmLocalLPattern=6)` | 方向边 **mark=2** | seed 组件 **mark=1** | 与零件版 mark 完全不同！dir2=4。SW 自动从 selection 绑 `D1Axis` + `SeedComponentArray`，**不要**手动 set（数组属性 readonly）|
| **组件**圆周阵列<br>`CreateDefinition(swFmLocalCirPattern=5)` | 轴 **mark=2** | seed 组件 **mark=1** | 同上 |
| **组件**镜像<br>`IAssemblyDoc.MirrorComponents3` | plane mark=1（取 Feature 用） | — | **不走** `CreateDefinition`！直接调 MirrorComponents3 14 参数版本（详 §9）。`CreateDefinition + CreateFeature` 路径对自由组件静默返 None，PR #22 切到录宏入口 |

---

## 5. 已确认存在 / 不存在的 SW 2026 方法

### 存在
- IFeatureManager：FeatureExtrusion3, FeatureCut3, FeatureCut4, FeatureRevolve2, FeatureFillet3, InsertFeatureChamfer, FeatureLinearPattern2, FeatureCircularPattern3, InsertMirrorFeature2, HoleWizard5, SimpleHole2, InsertRib, **CreateDefinition(swFeatureNameID_e)** + **CreateFeature(data)**（PR #21 实战路径）
- **swFeatureNameID_e 整数值**（CHM **不公开**，v2 探针扫得）：
  - `swFmLocalLPattern = 6`
  - `swFmLocalCirPattern = 5`
  - `swFmMirrorComponent = 116`
- IAssemblyDoc：AddComponent / 2 / 4 / 5, AddMate / 2 / 3 / 4 / 5, **CreateMate**, **CreateMateData**, AddConcentricMateWithTolerance, AddDistanceMate
- ISldWorks：OpenDoc / 2 / 3 / 4 / 6, NewDocument, CloseAllDocuments, ActivateDoc3
- IModelDoc2：SaveAs3, SketchManager, FeatureManager, Extension, SelectionManager
- ISketch：`.Name` (property, 不是方法), `.Description`
- IModelDocExtension：SelectByID2, SaveAs

### 不存在（在 SW 2026 上完全缺失）
- ~~**`FeatureShell` 全系列**~~ **【M26 修正】** v1 历史的这条结论是**错的**:
  - v1 在 `IFeatureManager` 找 (确实没有), 但实际 SW 2026 SP02.1 把 shell
    API 放在 **`IModelDoc2`**:
    - `IModelDoc2.InsertFeatureShell(double Thickness, bool Outward)` 返 void
    - `IModelDoc2.InsertFeatureShellAddThickness(double Thickness)` 返 void
    (multi-thickness shell)
  - M26 add_shell 复刻 zero-bug L2 6/6 一次过 (用 PartGeometryHelpers.FindPlanarEndFace
    选 +Z 面打开 + InsertFeatureShell + 几何验证防 silent fail)
  - **教训**: v1 知识库 "API 不存在" 类结论需要在每个 SW SP 升级后重新反射验证 —
    不能完全相信
- **`OpenDoc5`**（OpenDoc4 → 6 间断）
- `FeatureRevolve3 / 4`
- `InsertRib2 / 3`，`FeatureRib`，`InsertFeatureRib`
- `HoleWizard6`，`AddPartToAssembly`
- `EnsureDispatch`：SW 的 IDispatch 不暴露 ITypeInfo，所以
  `win32com.client.gencache.EnsureDispatch("SldWorks.Application")` 总失败
  → **项目锁定 late binding `Dispatch`**

---

## 6. 常用 enum 值（来自 `swconst.chm` + 实测核对）

### `swMateType_e`
| 名称 | 值 |
|---|---|
| swMateCOINCIDENT | 0 |
| swMateCONCENTRIC | 1 |
| swMatePERPENDICULAR | 2 |
| swMatePARALLEL | 3 |
| swMateTANGENT | 4 |
| swMateDISTANCE | 5 |
| swMateANGLE | 6 |
| swMateLOCK | 11 |
| swMateSYMMETRIC | 12 |
| swMateWIDTH | 13 |
| swMateCAMFOLLOWER | (查 swconst) |

### `swMateAlign_e`
| 名称 | 值 |
|---|---|
| swMateAlignALIGNED | 0 |
| swMateAlignANTI_ALIGNED | 1 |
| swMateAlignCLOSEST / swAlignANY | 2 |
| swAlignNotApplicable | -1 |

### `swEndConditions_e`（拉伸 / 切除终止条件）
| 名称 | 值 |
|---|---|
| swEndCondBlind | 0 |
| swEndCondThroughAll | 1 |
| swEndCondThroughNext | 2 |
| swEndCondUpToVertex | 3 |
| swEndCondUpToSurface | 4 |
| swEndCondOffsetFromSurface | 5 |
| swEndCondMidPlane | 6 |
| swEndCondUpToBody | 7 |

### `swUserPreferenceStringValue_e`（GetUserPreferenceStringValue 用）
| 名称 | 值 | 用途 |
|---|---|---|
| swDefaultTemplatePart | 9 | 默认零件模板路径 |
| swDefaultTemplateAssembly | 8 | 默认装配体模板路径 |
| swDefaultTemplateDrawing | 7 | 默认工程图模板路径 |

### `swDocumentTypes_e`
| 名称 | 值 |
|---|---|
| swDocPART | 1 |
| swDocASSEMBLY | 2 |
| swDocDRAWING | 3 |

### `swWzdGeneralHoleTypes_e`（HoleWizard5 第 1 个参数）
| 名称 | 值 | 说明 |
|---|---|---|
| swWzdCounterBore | 0 | 沉头孔 |
| swWzdCounterSink | 1 | 锥头沉孔 |
| **swWzdHole** | **2** | **简单钻孔**（最常用，仅这个在 ANSI Inch 路径下稳定） |
| swWzdPipeTap | 3 | 锥度螺纹 |
| swWzdTap | 4 | 直螺纹（攻丝孔） |
| swWzdLegacy | 5 | 旧版 |
| swWzdCounterBoreSlot | 6 | |
| swWzdCounterSinkSlot | 7 | |
| swWzdHoleSlot | 8 | |

### `swWzdHoleStandards_e`（HoleWizard5 第 2 个参数）
| 名称 | 值 | 说明 |
|---|---|---|
| **swStandardAnsiInch** | **0** | **唯一在 SW 2026 + late binding 下完整工作的 standard** |
| swStandardAnsiMetric | 1 | （feat=None） |
| swStandardBSI | 2 | |
| swStandardDME | 3 | |
| swStandardDIN | 4 | |
| swStandardHelicoilInch | 6 | |
| swStandardHelicoilMetric | 7 | |
| swStandardISO | 8 | （feat=None） |
| swStandardJIS | 9 | |
| swStandardGB | 13 | **中国国标（PR #24 破）—— GB tap 用 `fastener_type=359`** |
| swStandardIS | 15 | 印度国标 |
| swStandardAS | 16 | 澳洲国标 |

### `swStandardGBFastenerTypes_e`（CHM 不公开真值，由 SW UI 录宏破）

| 名称 | 值（实测） | 备注 |
|---|---|---|
| GB tap (攻丝孔 / 螺纹孔) | **359** | PR #24 用户录"异形孔向导 → GB M4 螺纹孔"宏破得 |
| GB CounterBore (柱形沉头孔，内六角螺钉用) | **361** | PR #25 录"GB M6 CounterBore"宏破得 |
| GB CounterSink (锥形沉头孔，沉头螺钉用) | **363** | PR #25 录"GB M6 CounterSink"宏破得 |

**规律观察**：359 / 361 / 363 间隔 2 —— 推测中间偶数 360 / 362 是某些细分类
（如 ISO 螺钉用 GB 兼容路径），需要时再录宏补全。

**关键 HoleWizard5 参数模板（GB tap 路径）** —— 1:1 复刻录宏，4 个魔法位是 v3 探针全设 0 失败的根因：
```
args = [
    4,                  # GenericHoleType = swWzdTap
    13,                 # StandardIndex = swStandardGB
    359,                # FastenerTypeIndex
    "M4",               # SSize（M3/M4/M5/M6/M8/M10/M12）
    1 or 0,             # EndType（ThroughAll / Blind）
    drill_m,            # Diameter = tap drill 直径（M4→3.3mm）
    depth_m,            # Depth
    True,               # Length 占位（VBA True → -1.0）
    depth_m,            # Value1 = 螺纹长度
    pitch_m,            # Value2 = 螺距（M4→0.7mm）
    1.74532925199433,   # Value3 = π/1.8 ≈ 100°（沉头角默认）
    0.0, 0.0, 0.0,      # Value4-6
    1.0, 1.0,           # ★ Value7,8 = 魔法 flag enable
    0.0, 0.0,           # Value9-10
    -1.0, -1.0,         # ★ Value11,12 = "SW 默认"占位
    "",                 # ThreadClass
    False, True, True,
    True, True, False,
]
```

详 `feature.add_threaded_hole` + `GB_TAP_TABLE` + `scripts/debug_hole_wizard_v4_gb_tap.py`。

### `swStandardAnsiInchFastenerTypes_e`（HoleWizard5 第 3 个参数，配 standard=0 用）
| 名称 | 值 |
|---|---|
| swStandardAnsiInchBinding | 0 |
| swStandardAnsiInchButton | 1 |
| swStandardAnsiInchFlatHead82 | 15 |
| swStandardAnsiInchAllDrillSizes | 18 |
| **swStandardAnsiInchFractionalDrillSizes** | **19**（simple drill hole 用） |
| swStandardAnsiInchBottomingTappedHole | 26 |
| swStandardAnsiInchDowelHole | 703 |

### `swSelectType_e`（部分；GetSelectedObjectType3 返回值）
| 名称 | 值 | 备注 |
|---|---|---|
| swSelEDGES | 1 | |
| swSelFACES | 2 | |
| swSelSKETCHES | 9 | |
| swSelDATUMPLANES | 4 | |
| swSelCOMPONENTS | 20 | |

### `swSelectByID2.type` 字符串（SelectByID2 第 2 个参数）
- `"PLANE"` —— 基准面
- `"FACE"` —— 面
- `"EDGE"` —— 边
- `"SKETCH"` —— 草图（特征级）
- `"SKETCHSEGMENT"` —— 草图中的线段
- `"SKETCHPOINT"` / `"EXTSKETCHPOINT"` —— 草图点
- `"BODYFEATURE"` —— 特征树中的实体特征（Cut-Extrude 等）
- `"TEMPAXIS"` —— **临时轴**（不是 `"AXIS"`！圆柱体的中心轴）
- `"REFERENCEAXIS"` —— 显式参考轴

---

## 7. 中英文别名（SW 中文 UI 兼容）

项目 `sketch.py` 已实现自动互译（`_feature_name_candidates`、`_PLANE_NAME_ALIASES`）。

### 前缀别名 9 对
| 英文 | 中文 |
|---|---|
| Sketch | 草图 |
| Cut-Extrude | 切除-拉伸 |
| Boss-Extrude | 凸台-拉伸 |
| Fillet | 圆角 |
| Chamfer | 倒角 |
| Revolve | 旋转 |
| Mirror | 镜像 |
| LPattern | 线性阵列 |
| CirPattern | 圆周阵列 |

→ `select_feature(model, "Cut-Extrude1", "BODYFEATURE")` 自动也试 `"切除-拉伸1"`

### 基准面 / 临时轴
| 英文 | 中文 |
|---|---|
| Front Plane | 前视基准面 |
| Top Plane | 上视基准面 |
| Right Plane | 右视基准面 |

→ `select_plane(model, "front")` 自动试两种语言

### 装配体路径格式
```
<plane_or_face>@<comp_full_name>@<asm_title>
例：前视基准面@part_a-1@装配体32
```

`comp_full_name = component.Name2` （如 `"part_a-1"`）
`asm_title = asm.GetTitle`（属性，不带括号）

### 装配体 mate entity_name 中英文别名兜底（PR #16 关键修复）

**问题**：中文 SW 下 `SelectByID2` 是**字面匹配** `entity_name`：
- LLM 填 `"Front Plane@part1-1@asm1"` → 中文 SW 选不中
- 必须 `"前视基准面@part1-1@asm1"`

**E2E 暴露**：asm-concentric-mate 跑 17 次 add_mate 才偶然试到中文成功
（试了 `Face<1>` / `Edge<1>` / `front` / `CylindricalFace` 等都失败）。

**修复**：`feature.add_mate` 内部用 `_expand_mate_entity_aliases()` 自动展开候选：

```python
_MATE_ENTITY_ALIASES = {
    "Front Plane": ["前视基准面"],
    "前视基准面":  ["Front Plane"],
    "front":      ["Front Plane", "前视基准面"],
    # ... Top / Right / 短名 / 大小写变体
}

for cand in _expand_mate_entity_aliases(entity_name):
    full_path = f"{cand}@{comp_name}@{asm_title}"
    if asm.Extension.SelectByID2(full_path, sw_type, ...):
        break   # 选中即用
```

**LLM 友好性**：填 "Front Plane" / "前视基准面" / "front" / "Front" 任一形式
都工作 —— `MateRef.entity_name` description 说明此自动互译行为。

### 装配体 title strip 后缀（PR #14 修复）

`asm.GetTitle` 在不同上下文返回不同后缀：
- `new_assembly()` 后：title = `"装配体1"`（无后缀）
- `open_assembly("xx.sldasm")` 后：title = `"xx.SLDASM"`（**带大写后缀**）

`SelectByID2` 期望的 selection path 不带后缀。`feature.add_mate` 已 strip：

```python
asm_title = str(asm.GetTitle)
for _suffix in (".sldasm", ".SLDASM", ".sldprt", ".SLDPRT"):
    if asm_title.endswith(_suffix):
        asm_title = asm_title[: -len(_suffix)]
        break
```

stateless reopen 模式（每个工具自己 open → operate → save）暴露的潜伏 bug。

---

## 8. 已知 limitation（按 PR 撤回顺序）

| API | 现状 | 上次撞墙点 | 下次破局思路 |
|---|---|---|---|
| `add_dimension` (草图级) | ✅ **PR #31 真破局** —— `ISketch.AutoDimension2` 实例方法（PR #30 9-stage 探针漏的接口）。SW 自动加 D1@草图1=length / D2@草图1=width / D1@草图1=直径 等命名尺寸，可被 `feature.modify_dimension` 改 | PR #8 / PR #29 / PR #30 累计 8+ IModelDoc2.Add* 路径全崩 SW（详 §8.1 历史矩阵） | 走 `sketch.auto_dimension_sketch(model, scheme="baseline")` —— 必须处于草图编辑模式调，SW 内部自动选所有支持的实体加尺寸，返 0=Success |
| `add_hole_wizard` (GB tap) | ✅ **已解决（PR #24）** | PR #13 起 known-limitation | 走 `feature.add_threaded_hole`（HoleWizard5 + fastener_type=359 + 4 个 Value 魔法位）。覆盖 M3-M12 GB 粗螺距 |
| `add_hole_wizard` (GB CounterBore) | ✅ **已解决（PR #25）** | PR #13 起 known-limitation | 走 `feature.add_counterbore_hole`（fastener_type=361 + Value1/2 用 GB/T 152.3 表）。覆盖 M3-M12 |
| `add_hole_wizard` (GB CounterSink) | ✅ **已解决（PR #25，部分）** | PR #13 起 known-limitation | 走 `feature.add_countersink_hole`（fastener_type=363 + Value1 用 GB/T 152.2 表）。覆盖 M6-M12（M3/M4/M5 SW 内部数据库缺失，已 spec 拒绝）|
| `add_hole_wizard` (ANSI Metric / ISO 等其他 standard) | 仍 feat=None | PR #13 部分修复（仅 AnsiInch + GB 路径） | 录对应 standard 的 SW UI 宏对齐 fastener_type 真值 |
| `add_hole_wizard` 位置控制 | 当前实现孔位 = plane 几何中心（AutoSelect=True） | PR #N | 走 `Create_Holes_Using_Hole_Wizard_and_Sketch_Points` 示例的 sketch-point 路径 |
| `add_hole_wizard` CounterSink/Tap | 复刻官方 C# 示例（CounterSink M2 AnsiMetric FlatHead82）仍 feat=None | PR #N | 与 GB/Metric 同一根因；待 fastener_type 真值确认后联动 |
| distance mate | ✅ **已解决（PR #20）** | PR #10 起 known-limitation：`CreateMate(distance_data)` 静默返回 None | 改走 `IAssemblyDoc.AddMate5`：**15 参**（14 in + 1 ByRef out，即官方文档签名；SW UI 录出来的宏文本里看着像 16 个值，多的一个是录制器表象，照搬会 DISP_E_TYPEMISMATCH）、引用 **mark=0**、齿轮比 / 角度上下限**必须传非零值**（`0.001` / `π/6`，传 0 会 fail —— 这是早期试 AddMate5 失败的真因）。由用户录 SW UI 宏比对锁定。详 `feature._add_distance_mate` + `scripts/debug_addmate5.py` |
| mirror components | ✅ **已解决（PR #21 同 PR 二次破局）** | PR #21 走 `CreateDefinition(swFmMirrorComponent) + CreateFeature` 对自由组件静默返 None | 改走 **`IAssemblyDoc.MirrorComponents3`** 14 参 + `ComponentsToInstance` / `ComponentOrientations` 必须用 `VARIANT(VT_ARRAY\|VT_DISPATCH)` / `VARIANT(VT_ARRAY\|VT_I4)` 显式构造 SAFEARRAY（传 Python tuple 静默返 None）。由用户录 SW UI 宏锁定。详 `feature.add_mirror_components` + `scripts/debug_mirror_components3.py` |
| `shell` | API **完全不存在** | PR #10 调研 | 走 `swApp.RunCommand` UI 命令路径，或留 macro 后门 |
| `rib` | `InsertRib(10 args)` selection 不识 | PR #10 调研 | 查 `IRibFeatureData2` + 提取宏录制示例 |

---

### 8.1 草图级 add_dimension 历史失败矩阵（PR #30，已被 PR #31 反转）

⚠️ **本节是历史记录**。PR #31 用 `ISketch.AutoDimension2` 已破局（见 §8.2）。
本节保留是为了说明"PR #30 为什么得出 permanent limitation 错误结论" + 防止
后续 PR 再走老错路（IModelDoc2.AddDimension2 系列彻底死路）。

PR #30 系统性扫描了所有 `IModelDoc2` / `IModelDocExtension` 上的 Add*Dimension
API + RunCommand 路径，**漏了 `ISketch` 实例上的 AutoDimension2**。表中"崩"=
SW 进程 RPC_E_DISCONNECTED 必须用户手动重启；"silent"= 返 None 但 SW 存活。

| 路径 | 结果 | 备注 |
|---|---|---|
| `model.AddDimension2(x, y, z)` | **崩 SW** | PR #8 / PR #29 / PR #30 三次确认 |
| `model.Extension.AddDimension(x, y, z, Direction=0/1/2/3)` | **崩 SW** | swSmartDimensionDirection_e 四个值都崩 |
| `model.AddHorizontalDimension2(x, y, z)` | **崩 SW** | 水平专用 API 也崩 |
| `model.AddVerticalDimension2(x, y, z)` | (未测，预期崩) | B 路径崩后跳过 |
| `model.Extension.AddSpecificDimension(x, y, z, swLinearDim=2, err)` | silent None, Error=1 | DimTypeMismatch，type 不支持 |
| `model.Extension.AddSpecificDimension(x, y, z, swHorLinearDim=11, err)` | **崩 SW** | 第 2 次同进程调用触发 |
| `swApp.RunCommand(38=SmartDimension)` 单独 | True 但不加 | 切换工具状态而已 |
| `swApp.RunCommand(3244=InsertAutoDim)` 单独 | True 但不加 | 同上 |
| `swApp.RunCommand(38)` → `AddDimension2(x,y,z)` | **不崩，silent None** | 唯一安全路径，但**同进程 2 次会崩**；包装在 `sketch.add_smart_dimension_via_command` |

**根因猜测**：
- SW UI 录宏（VBA 上下文）跑 AddDimension2 **OK** —— VBA 在 SW 进程内
- pywin32 late binding（外部 RPC）跑 AddDimension2 **崩** —— 外部 RPC 不持有 SW UI 上下文
- RunCommand(38) 进入"智能尺寸工具模式" → 让 SW 内部有 UI 上下文 → AddDimension2 不崩但 silent（外部 RPC 无法触发"鼠标放置"事件 → 命令挂起）

**未走的破局路径**：
- `ISldWorks.RunMacro2(.swp 路径, 模块名, 子程序名, opt, err)` —— 让 SW 进程内 VBA 跑预录的"参数化尺寸"宏，SW Custom Property 传 X/Y/Z 参数。复杂度高，留作未来 PR。

**实际方案（已交付，PR #29）**：走 `feature.modify_dimension` —— 特征级
Parameter（如 `D1@凸台-拉伸1` = 拉伸深度）已被 SW 自动命名，**不需要 AddDimension2
也能改 80%+ 场景**（深度 / 孔径 / 圆角半径 / 旋转角度 / mate distance 等）。

### 8.2 草图级 add_dimension 真破局（PR #31 ISketch.AutoDimension2）

PR #30 漏了 `ISketch` **实例方法**。PR #31 1 个 stage 直接全胜：

```python
# 必须处于草图编辑模式（insert_sketch 后未 exit）
active_sketch = model.SketchManager.ActiveSketch   # ISketch 实例
status = active_sketch.AutoDimension2(
    EntitiesToDimension=swAutodimEntitiesAll,     # = 1
    HorizontalScheme=swAutodimSchemeBaseline,     # = 1
    HorizontalPlacement=swAutodimHorizontalPlacementAbove,  # = 1
    VerticalScheme=swAutodimSchemeBaseline,
    VerticalPlacement=swAutodimVerticalPlacementRight,      # = 1
)
# status: 0=Success, 1=BadOptionValue, 4=NoActiveSketch, 8=NoEntities, ...
```

**实测结果**（``scripts/debug_autodim2.py`` 3 stage 全胜）：

| 草图几何 | 调用前 | 调用后 |
|---|---|---|
| `draw_rectangle(0, 0, 50, 30)` | 无命名尺寸 | **D1@草图1 = 50mm (length)** + **D2@草图1 = 30mm (width)** |
| `draw_circle(0, 0, radius=20)` | 无命名尺寸 | **D1@草图1 = 40mm (直径)** ← SW 自动用直径不是半径 |
| 闭合多边形草图 | 无命名尺寸 | SW 按 entities 数量加 D1-Dn |

**封装路径**：`sketch.auto_dimension_sketch(model, scheme="baseline"|"chain")` —
内部校验 ``ActiveSketch is not None`` + status code 翻译。

**默认 enable**：``create_rectangular_block`` / ``create_cylinder`` / ``create_tube``
（PR #31）在 `exit_sketch` 前自动调 `auto_dimension_sketch`，让 LLM 后续可用
``modify_dimension_in_part("D1@草图1", value_mm=80)`` 改 length / 直径等
草图级尺寸 —— **mech-pilot 完整参数化迭代设计能力**。

**为何 ISketch.AutoDimension2 不崩而 IModelDoc2.AddDimension2 崩**：
- AutoDimension2 是 SW 内部"自动解析草图几何 + 批量加标准尺寸"路径，**不需要
  UI 鼠标放置事件**，外部 RPC 能完整完成
- AddDimension2 走"用户点了一个 entity + 拖到 X/Y/Z 位置放下"UI 事件路径，
  外部 RPC 无 UI 上下文 → 调用挂起或崩

### 8.3 pattern_circular 在多 cut 零件上 SW permanent limitation（PR #35）

PR #35 投入 12 个 stage 探针（v1-v8 API + RunMacro2 + SW UI 录宏重放）全部 fail。
**SW 本身的限制**，不是 mech_pilot bug。

**复现场景**：cylinder + 中心 D=30 孔（切除-拉伸1）+ 偏心 M6 孔（切除-拉伸2）
→ 任何路径调 ``pattern_circular("切除-拉伸2", count=4)`` 都 silent fail
（CreateFeature/FCP3 返 None / Nothing 但无错误信息，零件最终只 1 个孔）。

**12 个 stage 全 fail 矩阵**：

| Stage | 路径 | 结果 |
|---|---|---|
| v1 | FCP3 + 间距=π/2（PR #32） | ❌ silent fail |
| v2 | CreateDefinition(swFmCirPattern=5) + setattr | ❌ feat=None |
| v3 | + fdef.Axis 显式 setter | ❌ Axis 设后仍 None |
| v4 | + AccessSelections(model, null_disp) | ❌ COM 服务器异常 |
| v5 | FCP3 + EqualSpacing=True + Spacing=2π（同录宏） | ❌ feat=None |
| v6 | CreateDefinition + 完美 1:1 复刻宏 selection 7 步 | ❌ feat=None |
| v7 | + 3 种 SetPatternFeatureArray（含 PatternFeatureArray=arr） | ❌ A/B/C 全 fail |
| v8 | EnsureDispatch fdef early binding | ❌ Axis 仍 None |
| RunMacro2 v1 | swApp.RunMacro2 跑硬编码 .swp | ❌ errnum=22 module 找不到 |
| RunMacro2 v2 | SW UI 手动跑 .swp + MsgBox 诊断 | ❌ MsgBox: CreateFeature 返 Nothing |
| 录宏重放 | 用户用 SW UI 录新宏 + 立即重跑 | ❌ silent fail 无错误 |
| 原录宏 retry | 用户当初录 work 的宏，新跑 | ❌ 不重现 |

**根因**："录的宏 ≠ reliable code"。SW 录制器只记录鼠标点击对应的**部分 API 入口**，
缺少 UI 内部的隐式上下文（焦点窗口 / selection state machine / 视图重绘 / 自动 axis
推断 / 鼠标射线方向命中的隐式 entity）。复杂特征（pattern / mirror / hole_wizard /
sweep）尤其受影响。

**绕过方案（PR #35 已交付）**：``modeling_tools.create_flange`` L3 一键工具
—— 在**一个 sketch** 里同时画所有孔 + **一次** extrude_cut，**不走 pattern API**。
法兰 / 端盖 / 周向孔类零件 100% 可靠。

**docstring 警告**：``pattern_circular_in_part.__doc__`` 已加 PR #35 limitation
说明 + 引导 LLM 改用 ``create_flange`` 或 ``mirror_feature_in_part``。

---

## 9. 调试套路（`scripts/debug_*.py` 模板）

每个新 API 实现前都写一个 probe 脚本。命名 `debug_<api_name>.py`。
模板：

```python
"""探测 SW XXX API 在 SW 2026 上的签名 / selection / 参数语义。"""
from __future__ import annotations
import sys
from mech_pilot.solidworks import connection, feature, sketch
from mech_pilot.solidworks.connection import (
    empty_variant, null_dispatch_variant, byref_long_variant,
)

def _build_test_part(model):
    """造测试母体（圆柱 / 立方体）"""
    sketch.select_plane(model, "front")
    sketch.insert_sketch(model)
    sketch.draw_circle(model, 0.0, 0.0, 10.0)
    sketch.exit_sketch(model)
    sketch.clear_selection(model)
    sketch.select_feature(model, "Sketch1", "SKETCH")
    feature.extrude(model, depth_mm=20.0)

def main() -> int:
    app = connection.get_app()
    model = connection.new_part(app)
    _build_test_part(model)

    # ===== 方法名枚举 =====
    fm = model.FeatureManager
    for name in ("XxxApi", "XxxApi2", "XxxApi3"):
        print(f"  {name}: {'present' if hasattr(fm, name) else 'absent'}")

    # ===== 参数计数扫描 =====
    base_args = [...]
    for n in range(MIN, MAX):
        # 重新建模
        sketch.clear_selection(model)
        ...
        try:
            r = fm.XxxApi(*args[:n])
            print(f"  {n} args -> {r}")
        except Exception as exc:
            short = repr(exc)[:80]
            print(f"  {n} args FAIL: {short}")

    connection.close_all_documents(app, include_unsaved=True)
    return 0

if __name__ == "__main__":
    sys.exit(main())
```

**经验法则**：
- 拿不到 `r` 时先看 `err_var.value` 是否 0；0 = SW 报成功但实际未生效 = 该 API obsolete
- 报错文本对应：
  - `非选择性的参数` = DISP_E_PARAMNOTFOUND = 缺参数
  - `无效的参数数目` = 参数太多
  - `类型不匹配` = 某个 arg 类型不对（注意 byref / variant 包装）
  - `远程过程调用失败` = SW 进程崩溃（API 严重不兼容）

---

## 10. 关键文件引用

| 文件 | 用途 |
|---|---|
| `src/mech_pilot/solidworks/connection.py` | VARIANT 工厂 + 模板自动定位 + 文档操作 |
| `src/mech_pilot/solidworks/sketch.py` | 选择系统 + 中英文别名表 + 草图原语 |
| `src/mech_pilot/solidworks/feature.py` | 特征建模实现，每个函数 docstring 含踩坑记录 |
| `tests/conftest.py` | `--solidworks` 开关 + autouse SW 文档清理 |
| `scripts/debug_*.py` | 19 个调试脚本，每次 probe 的现场保留 |

---

## 11. 外部参考

### SolidWorks API
- 官方在线（推荐）：`https://help.solidworks.com/<year>/english/api/sldworksapi/`
- 论坛：`https://forum.solidworks.com/community/api/`（社区问答；当本地+在线都查不到时用）
- VBA → pywin32 翻译参考：`sldworksapivb6.chm`（5 MB，所有 API 都有 VBA 示例）

### pywin32 / late binding
- pywin32 文档：`https://mhammond.github.io/pywin32/`
- VARIANT 包装：`win32com.client.VARIANT(pythoncom.VT_XXX, value)`
- COM 出参处理：用 `VT_BYREF | VT_I4` 等组合

### Pydantic AI（agent 层）
- 官方：`https://ai.pydantic.dev/`
- 项目仅 `agent.py` 一个文件耦合 pydantic_ai，换框架代价小
