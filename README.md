# SubtitleStudio

从视频里抽音轨 → 语音识别 → 生成可编辑字幕 → 渲染成透明叠加视频或字幕文件。
.NET 10 + Avalonia 桌面端 + ffmpeg + whisper.cpp（Whisper.net）。

## 界面

![界面截图](docs/screenshot.png)

Dock 式四区布局，面板可拖动：

```
┌───────────────┬──────────────────────────────────────┐
│ 字幕列表       │ 时间轴：每条音轨一行                    │
│ （改错字）     │   波形 + 紧贴其下的字幕条               │
│               │   点哪条音轨就切到哪条                  │
├───────────────┴──────────────────────────────────────┤
│ 样式条：字体 / 字号 / 字色 / 描边 / 底部边距            │
├──────────────────────────────────────────────────────┤
│ 预览（棋盘格 = 透明背景）+ 导出下拉                     │
└──────────────────────────────────────────────────────┘
```

## 目录

```
SubtitleStudio/
├─ build.ps1                 # 离线还原 + 编译
├─ publish.ps1               # Release 自包含打包到 dist/
├─ run.ps1                   # 编译并启动
├─ Directory.Build.props     # 编译目标、可空性、输出整洁度的统一配置（在 src/ 下）
├─ NuGet.config              # 只指向本地离线包目录
├─ models/                   # 放 ggml-*.bin（见 models/README.md）
├─ tools/ffmpeg/bin/         # 放 ffmpeg.exe / ffprobe.exe
└─ src/
   ├─ SubtitleStudio.Core/   # 无 UI 依赖：媒体、ASR、断句、字幕写出、透明视频渲染
   ├─ SubtitleStudio.Cli/    # 命令行：SubtitleStudio.Cli.exe（AssemblyName 与项目同名）
   └─ SubtitleStudio.App/    # Avalonia 桌面应用
```

### 分层约定

* `Core` 不认识 Avalonia，也不做任何 UI 线程调度；目录按 `Models / Media / Asr / Subtitles / Export` 切分，每个文件只负责一件事。
* `App` 的 `AppState` 是一个 `partial class`，按关注点拆成 5 个文件：
  `AppState.cs`（状态与派生属性）、`AppState.Media.cs`（加载、波形、选轨）、
  `AppState.Recognition.cs`（抽音轨 + ASR + 断句）、`AppState.Preview.cs`（预览渲染、时间轴交互）、
  `AppState.Export.cs`（透明视频 / 字幕导出）。
* `Controls/TimelineControl` 同样是 `partial`：`TimelineControl.cs`（属性与外观常量）、
  `.Render.cs`（刻度 / 波形 / 字幕块 / 播放头绘制）、`.Input.cs`（缩放、平移、拖动、拉伸）。
* `Panels.cs` 只放四个 Dock 面板的壳（`SubtitleList/Timeline/Preview`），样式编辑单独在 `StyleViewModel.cs`。
* 每个 `View` 的对话框与窗口事件放在 `.Dialogs.cs` / `DockChrome.cs` 这类附属文件里，不再往一个文件里堆。

### Core 里几个关键点

* `Media/AudioExtractor` —— `-map 0:<流索引> -vn -ac 1 -ar 16000 -c:a pcm_s16le`，ASR 统一 16k 单声道；失败时只回传 stderr 最后几行。
* `Media/TempWorkspace` —— 抽出来的 wav 写在素材同级的 `.subtitle-studio-tmp`，写不进去再退到程序目录 / `%LOCALAPPDATA%` / `%TEMP%`。
* `Media/WaveformBuilder` —— 把音频解成 8k s16le 走 stdout，边读边算每秒 min/max，用于时间轴波形。
* `Asr/WhisperLocalProvider` —— Whisper.net 流式回调，边识别边出段。
* `Subtitles/CueSegmenter` —— 按「句末标点 → 逗号类 → 硬切」三级断句，限制 18 字/行、0.9–6 秒。
* `Export/SubtitleLayerRenderer` —— SkiaSharp 画文字，把预乘 alpha 反转成直通 alpha（否则 ffmpeg 出来边缘发黑）。
* `Export/OverlayVideoExporter` —— 每帧 BGRA 裸数据喂给 ffmpeg 的 `rawvideo`，输出带 alpha 的视频。

## 构建

需要 .NET 10 SDK（或 9/8 改 `Directory.Build.props` 里的 `TargetFramework`）。

```powershell
cd SubtitleStudio
.\build.ps1      # 还原 + 编译 App 与 Cli
.\run.ps1        # 编译并启动
.\run.ps1 D:\videos\demo.mp4   # 带文件启动，自动抽音轨 + 识别
```

也可以直接用 dotnet：

```powershell
dotnet build src\SubtitleStudio.App\SubtitleStudio.App.csproj
dotnet run   --project src\SubtitleStudio.App
```

> 离线环境：如果仓库上一层放了 `.tools\dotnet`（本地 SDK）和 `.tools\nuget-local`（离线 nupkg），
> `build.ps1` / `publish.ps1` 会自动改用它们并把临时目录、NuGet 缓存收进 `.tools`；
> 没有 `.tools` 时脚本走系统 dotnet 和 nuget.org。

## 设置

菜单里点「设置」打开独立窗口，配置存在 `%LOCALAPPDATA%\SubtitleStudio\settings.json`（不可写时依次退到「文档\SubtitleStudio」「程序目录\config」「临时目录」；也可用环境变量 `SUBTITLESTUDIO_SETTINGS` 指定）。

- **硅基流动**：地址固定 `https://api.siliconflow.cn/v1`，填 API Key 后点「获取模型列表」，会拉 `/v1/models` 并只保留语音识别相关的模型（`type=audio` 或名字里带 sensevoice / whisper / asr / speech）。
- **天翼星辰**：走短语音识别 `POST https://api-maas.teleai.com.cn/aipaas/voice/v1/asr/short`，**只要 API Key**（`Authorization: Bearer sk-...`），不需要 AppID / AppKey。请求体是 `{req_id, audio_data(Base64 PCM), sample_rate, enable_punc, enable_itn}`，返回 `result.sentences[]`（`begin_time` / `end_time` 毫秒 + `texts`）。官方限制单次 1 分钟以内，程序会把长音频按 58 秒切片、逐片识别后平移时间轴拼接，所以整段素材也能用。「测试连接」会拿 0.5 秒静音打一次，返回 `code=10000` 就说明 Key 可用。
- **本地模型**：列出全部可选 whisper 模型（体积 + 一句话说明），点「下载」按镜像顺序拉取，支持断点续传。默认镜像为 HuggingFace → HF 镜像 → ggml-org，可用环境变量 `SUBTITLESTUDIO_MODEL_MIRRORS` 覆盖（分号分隔，`{file}` 会被替换成文件名），例如内网镜像：

```powershell
$env:SUBTITLESTUDIO_MODEL_MIRRORS = "http://10.0.0.5/models/{file};https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{file}"
```

命令行等价物：

```powershell
SubtitleStudio.Cli.exe --download medium        # 按镜像下载模型
SubtitleStudio.Cli.exe --settings               # 打印当前设置与配置文件路径
SubtitleStudio.Cli.exe --settings --set-backend siliconflow --set-key sk-xxx
```

## 模型菜单

标题栏的下拉分三段：**本地模型**（二级菜单列出 models/ 里的模型，显示体积）、**硅基流动**、**天翼星辰**，末尾是**设置**。选中云端后端时如果还没填 Key，才弹一次「去设置」提示；填过之后直接切换。

## 删除与清空

- 每条音轨右侧 gutter（导出图标下方）的垃圾桶 = **删掉整条音轨**（连同它的字幕与波形），删除前二次确认；删掉的轨道会从时间轴和列表里移除，剩下的自动重排。
- 字幕列表右上角的「清空」只清当前音轨的字幕，同样二次确认。

## 时间轴行高

轨道行会**撑满面板高度**（每行 30–96 DIP 自适应），窗口拉高时波形和字幕条一起变厚，下方不会留空白；轨数多到装不下时按最小行高排布。

## 按钮自测

`tools\ui-test.ps1` 用 Windows UI Automation 把界面按钮跑一遍（不依赖窗口位置）：

```powershell
pwsh .\tools\ui-test.ps1 -Media test\test.mp4 -Shots .\shots
```

覆盖：播放/暂停、缩放、清空（含确认框）、删除音轨（含确认框）、重新识别、打开文件对话框、导出音频、导出下拉六个格式、模型菜单、设置窗口。脚本会打印 `OK/FAIL` 与截图路径。


- 每条音轨右侧（导出图标下方）有垃圾桶按钮，删除该轨字幕前会二次确认。
- 字幕列表右上角的「清空」同样二次确认。

## 多语言（i18n）

界面文案已收口到 `Services/Loc.cs`（`Loc.T("键")`），内置中/英两套；XAML 里可以用 `{loc:T Menu.Settings}`。
切换语言：设置环境变量 `SUBTITLESTUDIO_LANG=en`（不设或 `system` 则跟随系统）。
后续加语种只要在 `Loc.Languages` 里加一份字典，或调用 `Loc.Merge("ja", 字典)` 从外部 JSON 合并，不用改界面代码。

## 调色板

样式条上的文字颜色 / 描边颜色方块现在是按钮，点开是完整调色板（色相条 + 饱和度/明度面板 + 透明度 + 常用色），选完立刻预览。
旁边的十六进制输入框仍然可以手输 `#AARRGGBB`。

## 音轨上的悬浮按钮

鼠标移到某条音轨上，右侧才会出现两个按钮：上面「导出该轨音频」，下面红色「删除整条音轨」。
两个按钮上下铺满该行的波形 + 字幕条，正好吃满这一列的高度，不留空隙。

## 云端模型

「硅基流动」「天翼星辰」在模型菜单里都是二级菜单：硅基流动点「刷新模型列表」会调 `/v1/models`
并只保留语音识别类（`type=audio`，或名字里带 sensevoice/whisper/asr/speech）；天翼星辰用官方算法编码清单。

## 云端识别实测

同一段素材（`test/test.mp4` 第 4 轨，10 秒中文）两个云端后端都跑通了：

| 后端 | 结果 | 备注 |
|---|---|---|
| 硅基流动 `FunAudioLLM/SenseVoiceSmall` | 「噢噢噢。」/「这是一段测试音频，」/「应该可以识别出来这几句话。」 | 接口只回整段文本 + `language`，**没有分句时间戳**，时间轴按字数估算 |
| 天翼星辰 `asr/short` | 同样三句，时间 `0.20–2.04` / `2.27–4.47` / `4.49–7.63` | 返回 `begin_time`/`end_time`（毫秒），时间是真实的 |

> 天翼星辰短语音限制单次 1 分钟，超长会自动按 58 秒切片拼接。

## 打包

```powershell
.\publish.ps1                  # full：自包含，目标机不用装 .NET（默认）
.\publish.ps1 -Mode slim       # slim：框架依赖，要求目标机有 .NET 10 Desktop Runtime
.\publish.ps1 -Zip             # 额外压一个 zip 方便分发
.\publish.ps1 -NoModel         # 不打包模型（首次运行在设置里下载）
```

| 产物 | 目录 | 体积 | zip |
|---|---|---|---|
| full | `dist\SubtitleStudio` | 363 MB | 188 MB |
| slim | `dist\SubtitleStudio-slim` | 289 MB | 154 MB |

体积构成（full）：`tools\ffmpeg` 186 MB + `models` 74 MB + .NET 运行时与程序 101 MB。

- **ffmpeg 是最大头**：用的是 shared 版（`ffmpeg.exe` + `avcodec/avformat/avfilter/...` 一组 DLL）。换成只带所需编解码器的自编译版本可以压到 20–30 MB，代价是失去一部分输入格式的解码能力（当前构建几乎什么都能读）。
- **想再小**：`-Mode slim` 直接省掉 74 MB 的运行时；`-NoModel` 再省 74 MB 的模型。
- 打包会顺手删掉 `createdump.exe`、`Microsoft.DiaSymReader.Native`、`*.pdb` 等只在调试期有用的文件，以及 `runtimes` 下非 `win-x64` 的平台产物。
## 使用

1. 把 ffmpeg 放到 `tools/ffmpeg/bin/`（或加进 PATH）。
2. 把模型放到 `models/`，例如 `ggml-tiny.bin` / `ggml-medium.bin`。
3. 打开应用 → 拖入视频 → 自动抽音轨并识别 → 左侧列表改字、时间轴拖块。
4. 样式条调字体颜色描边，下方预览实时看效果。
5. 导出：WebM(VP9 alpha) / MOV(ProRes 4444) / MP4(H.264，无透明) / SRT / ASS / VTT。

### 字体

字体下拉里除系统字体外，最后一项是「＋ 导入其他字体…」。选中后挑一个 `.ttf/.otf/.ttc`，
文件会被复制到 `%LOCALAPPDATA%\\SubtitleStudio\\fonts`，重启后还在；渲染（预览与导出）直接按字体文件加载，
不需要装进系统字体库。

### 报错

出错时窗口左下角出现一条红色提示，只显示开头几个字，鼠标悬停展开完整信息，点一下复制到剪贴板。

### 多音轨

一个视频有多条音轨时，时间轴上每条音轨占一行（标着「音轨 1 / 2 / 3」，后面跟 codec 与语言），
每条都会独立抽音轨、独立识别、独立出字幕。点某一行就切到那条：

* 左侧字幕列表换成该轨的字幕，可以直接改字；
* 下方预览换成该轨的渲染效果；
* 每行「音轨 N」右边的方框是**导出勾选**：勾上的轨道才导出视频，没勾的跳过；
* 鼠标悬浮到某一行，该行最右侧贴边的地方才出现一个小 `⤒` 图标（跟着窗口走，不随时间轴滚动）：
  点一下选目录，把这条音轨导出成 `.m4a`。

多音轨的耗时是按轨串行累加的，状态栏会显示 `共 3/3 条音轨、3 条字幕`。

### 导出尺寸

导出的透明视频**不再贴合原视频画幅**，而是按字幕内容裁：

* 宽度 = 这条轨道里最长一行字幕的宽度 + 左右留白；高度 = 行数 × 行高 + 上下留白；
* 单条字幕超过 20 个字会在识别断句阶段就断开，转行由渲染器按 20 字/行折行；
* 预览面板左上角的「清晰度」有三档（流畅 / 高清 / 超清），决定渲染字号与最终像素，
  旁边直接显示当前这条轨道的导出尺寸，例如 `226×122 · 1 行 · 20 字/行`。

因为每条轨道字幕长度不同，多轨导出会得到**几段尺寸各自不同的视频**——按需叠加到别的工程里。

### 识别时的按钮

识别期间「音频与字幕轨道」标题栏右侧显示**圆环进度**（始终圆环，窄窗口也不会换成长条）与百分比，
百分比旁边是 `✕` 停止按钮：

* 抽音轨阶段点它，ffmpeg 立刻被中断；
* 识别阶段点它，会直接**结束当前这条音轨**（杀掉 whisper 进程，临时文件清掉），
  已完成的音轨保留；

### 播放

点「播放」会真的出声（`NAudio` + ffmpeg 先把勾选的音轨混成一个临时 wav，再播放），
播放头跟着真实进度走，再点一下暂停。没有勾选任何轨道时默认放第一条。
纯预览用，导出与它无关。

### 模型

标题栏的「本地模型 · tiny ▾」是模型菜单：

* **本地模型**：列出 `models/` 下的 `ggml-*.bin`，点一个就切过去；
* 云端通道（硅基流动 / 天翼星辰）：填了 Key 才可用；
* **模型说明（体积 / 下载地址）**：列出常用模型的体积与 HuggingFace 链接；
* **打开 models 文件夹**：直接定位到模型目录。

### 识别质量

识别完先进两层处理再断句：

* `HallucinationFilter`：丢掉静音段的凭空编造（`[BLANK_AUDIO]`、`[MUSIC]`、`(掌声)`、套话、整段重复、语速异常）；
  像 `[Bell]` 这种真的音效标注会保留；
* `PunctuationRestorer`：按词间停顿补逗号/句号（有词级时间戳时用真实停顿，没有就按字数切口气），
  括号标注不加标点。

## 命令行

```powershell
SubtitleStudio.Cli --input video.mp4 --out out --format srt,vtt,ass --model models\ggml-tiny.bin
SubtitleStudio.Cli --input video.mp4 --out out --track 2          # 只处理第 2 条音轨
SubtitleStudio.Cli --input video.mp4 --out out --overlay webm --width 1920 --height 1080 --fps 30
SubtitleStudio.Cli --input video.mp4 --waveform                   # 打印每条音轨的波形统计
SubtitleStudio.Cli --models                                       # 列出已装模型 + 可下载模型清单
SubtitleStudio.Cli --input clip.wav --asr-json result.json        # 只识别 wav 并输出 JSON（App 内部用）
```

`--out` 是**前缀**，会生成 `out.srt` / `out.vtt` / `out.ass` / `out-overlay.webm`。
多音轨文件默认逐条处理，文件名带轨号：`out-t1.srt`、`out-t2.srt`……
`--overlay` 用第一条（或 `--track` 指定的那条）音轨的字幕。

## 验证记录

* CLI 端到端（`testclip.mp4` 6 秒 + `ggml-tiny.bin`）：抽音轨 → 识别（1 段）→ 断句（1 条）→
  写 srt/vtt/ass → 渲染 180 帧 → 编码 WebM，总耗时约 3 秒。
* CLI 多音轨（`testclip3.mp4`，1 视频 + 3 音轨）：三条轨分别识别出 `[Bell]` / `[MUSIC]` /
  `[screaming]`，输出 `clip-t1.srt`~`clip-t3.srt`，耗时 2.8 秒。
* GUI：启动 → 自动加载并识别三条音轨 → 点击「音轨 2」那一行后，左侧列表换成 `[MUSIC]`、
  状态栏变成 `音轨 2 · 1 条字幕 · 语言 en`、预览同步换成 `[MUSIC]`。
* 真实录屏（`test/test.mp4`，1920×1080@60、HEVC、3 条 AAC 音轨、26.7 秒）：
  三轨分别识别出 `[MUSIC]` / `[BLANK_AUDIO]` / `[BLANK_AUDIO]`；GUI 导出 1080p60 WebM 成功，
  得到 `test-t1-overlay.webm`（198 KB）。
* 失败路径：导出到不可写目录、输出文件被独占占用两种情况都只弹提示并退出，
  不再崩溃；错误信息里带 ffmpeg 原文（`Permission denied`）。
* 按字幕裁尺寸（`testclip3.mp4`）：三条轨道导出得到 `226×122`、`326×122`、`440×122`
  三段落宽不同的 WebM；取消勾选音轨 2 后只导出 t1 与 t3。
* 每轨音频导出：点第一行右侧 `↓` 得到 `testclip3-t1.m4a`（137 KB）。
* 停止识别（`long.m4a`，180 秒双音轨）：抽音轨阶段点 `■` 立即停；识别阶段点 `■`
  会在当前音轨跑完后停下，状态栏显示「识别已停止（已完成的音轨保留）」，已完成轨的字幕保留。
* 播放：点「播放」后播放头推进（截图时 2.52s），按钮变成「暂停」，状态栏「播放中（只放勾选的音轨）」，
  音频经 ffmpeg 混流 + NAudio 真实出声。
* 悬浮导出：把鼠标移到「音轨 2」那一行，右侧出现 `⤒`，点一下得到 `testclip3-t2.m4a`（137 KB）。
* 识别质量：`testclip3.mp4` 三轨现在只剩 `[Bell]` 与 `[screaming]`（`[MUSIC]` 被当静音标注过滤），
  括号标注不再被补句号。
* 模型菜单：截图确认可展开「本地模型 / 云端通道（未填 Key）/ 模型说明 / 打开 models 文件夹」。
* 停止识别（`long.m4a`，180 秒）：识别中直接点 `✕`，当前音轨被中断，
  状态栏「识别已停止（已完成的音轨保留）」，进程存活无崩溃。
* 导出目录不可写（把 `SUBTITLESTUDIO_EXPORT_DIR` 指到 `C:\Windows\System32\...`）：
  自动改存到可写目录（依次尝试 `%LOCALAPPDATA%\SubtitleStudio\exports`、
  `我的视频\SubtitleStudio`、程序目录\exports、`%TEMP%`），界面提示改存位置，导出照常完成。

## 排错

* **导出 / 识别失败**：界面左下角会出现红色提示，只显示前几十个字，鼠标悬停看完整内容，点一下把完整错误复制到剪贴板。错误里会带上 ffmpeg 自己的输出（例如 `Error opening output ... Permission denied`、`moov atom not found`），不再只有一句「管道已结束」。
* **崩溃日志**：程序里所有没人接住的异常都会写进 `%LOCALAPPDATA%\SubtitleStudio\crash.log`，不会再弹「应用程序发生异常 0xe0434352」。遇到异常界面会留在原地（状态栏给提示），把日志发出来就能定位。
* **导出目录**：设置环境变量 `SUBTITLESTUDIO_EXPORT_DIR` 可以跳过文件夹选择框，直接导出到指定目录（自动化 / 批处理用）。
* **写入失败的两种原因**：程序会实际试写探针文件来判断。若是**权限/受控文件夹访问**拦的，底栏会出现「以管理员重启」按钮，
  点一下走 UAC 提权重开；若是系统层面的限制（磁盘只读、目录被别的程序独占），则直接提示原因与可换的目录。

## 已知限制

* MP4/H.264 不支持 alpha 通道，透明字幕请用 WebM 或 ProRes 4444。
* 云端 ASR 通道需要 API Key（见 `src/SubtitleStudio.Core/Asr/AsrChannels.cs`），且本机 TLS 有问题，
  换到正常机器再用。
* 长视频识别是串行的：可以中途停止，但停下后要重新识别只能整段重来（已完成的音轨不会丢）。
* 进度在「音频与字幕轨道」标题栏右侧，识别期间显示**圆环**与百分比（始终圆环，不按窗口宽窄切换）。
* 面板标题栏上的「▾ 菜单」「📌 固定」按钮已隐藏，只保留拖动与分隔条。
* 导出按素材原始帧率走（60fps 的 1080p 录屏编码 VP9 会比较慢，27 秒素材约 80 秒）；
  导出尺寸按字幕裁，所以像素量比原视频小很多，实际会比全画幅快。

## 开发小工具

`tools/shot-window.ps1` 按窗口截图（类似录屏软件的窗口捕获）：取 `DwmGetWindowAttribute`
的真实窗口边界，抢前台后再抓屏，抢不到就退化成 `PrintWindow` 自己渲染。

```powershell
.\\run.ps1 ..\\.tools\\testclip3.mp4
.\\tools\\shot-window.ps1 -Out shot.png
```
