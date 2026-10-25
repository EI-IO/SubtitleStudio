# Subtitle Studio

English | [简体中文](README.zh-CN.md)

Turn the audio in a video into editable subtitles, then export them as transparent overlay videos or subtitle files.

![Subtitle Studio](docs/screenshot.png)

## What it does

- Opens common video and audio files and lists every audio track it finds.
- Recognizes speech with a local model or a cloud service.
- Lets you fix the text and adjust timing directly in the app.
- Previews the subtitles over a checkerboard background, with live styling.
- Plays back only the audio tracks you select.
- Exports ready-to-use subtitle files or videos with a transparent subtitle layer.

## Download and install

The Releases page offers four packages:

| Package | What it is |
|---|---|
| `SubtitleStudio-Setup-x.y.z.exe` | Recommended. Installs the app with a local model and everything needed to play and export video. |
| `SubtitleStudio-Slim-Setup-x.y.z.exe` | Smaller installer. Requires the .NET 10 Desktop Runtime and downloads ffmpeg on first use. |
| `SubtitleStudio-full.zip` | Portable full version. Unzip and run `SubtitleStudio.exe`; no installation needed. |
| `SubtitleStudio-slim.zip` | Portable slim version. Requires the .NET 10 Desktop Runtime and downloads ffmpeg on first use. |

- Windows 10 or 11, 64-bit.
- The installer is per-user by default and does not require administrator rights. It creates a Start menu shortcut; a desktop icon is optional. Uninstall from **Settings → Apps**.
- Your settings and imported fonts are kept when you uninstall.
- The installer is not code-signed, so Windows SmartScreen may warn on first launch. Choose **More info → Run anyway**.
- The slim packages need the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). They do not include ffmpeg: on first launch the app offers the official download page and a mirror, shows the folder to use, and checks again when you click **Recheck**.

## Quick start

1. Open the app and choose **Open file**, or drag a video onto the window.
2. The audio tracks appear as rows on the timeline. Tick the tracks you want to work on.
3. Pick a recognition model in the title bar, then click **Start recognition**.
4. Correct the text in the subtitle list, and drag subtitle blocks on the timeline to fix their timing.
5. Adjust the font, color, outline and position until the preview looks right.
6. Export a subtitle file or a transparent overlay video.

## The window

The window is split into four areas:

- **Subtitle list** — one row per subtitle. Edit the text directly.
- **Timeline** — one row per audio track, with a waveform and its subtitle blocks. Click a row to switch to that track.
- **Style bar** — font, size, color, outline and bottom margin.
- **Preview** — the subtitle over a checkerboard background; the checkerboard stands for transparency.

Drag a panel title to rearrange or dock panels, and drag the dividers to resize them. The layout is remembered between sessions. If it gets messy, click **Reset layout** in the bottom bar to restore the default arrangement.

The bottom bar also holds the file name, the language menu and the current status.

## Working with multiple audio tracks

A video can contain several audio tracks. Each one gets its own timeline row and its own set of subtitles.

- Tick the checkbox next to a track to include it in recognition and export. Unticked tracks are skipped, and their existing subtitles are kept.
- Click a track row to bring its subtitles, preview and waveform into view.
- Hover over a track to reveal two small buttons at the right edge: export that track's audio, or delete the whole track (with confirmation).

Recognition runs track by track, and the status bar shows how many tracks and subtitles are done.

## Recognition

### Local models

Local recognition works offline once a model has been downloaded. Open **Settings → Local models** to download, continue a partial download, stop, or delete a model. Downloads resume where they left off after a restart.

The title-bar model menu lists the models that are ready to use, plus links and size notes for common models and a shortcut to the models folder.

### Cloud services

SiliconFlow and TeleAI are available after you enter an API key. Open **Settings**, select the provider and click **Test connection**; the app reports whether the key works and which models are available.

You can also add your own OpenAI-compatible speech recognition service: give it a name, address, API key and model name. It then appears in the model menu. Custom providers can be removed again; the built-in ones cannot.

### Starting and stopping

- Recognition never starts by itself. Choose the tracks and click **Start recognition**.
- During a run, the title bar shows a ring-shaped progress indicator and a percentage. Click the **✕** next to it to stop.
- Recognized tracks keep their subtitles; stopping only ends the current track.
- When a full run finishes, the button becomes **Re-recognize**.

### Cleaner audio

**Denoise before recognition** is on by default. It removes low rumble and background noise before recognition, which usually improves results on noisy recordings. Clean recordings are not affected.

## Editing subtitles

### Subtitle list

Each row shows the subtitle's midpoint time and an editable text box. Typing updates the preview and the timeline immediately.

Use **Clear** in the panel header to remove all subtitles from the current track (with confirmation).

### Timeline

- **Jump around:** click or drag in an empty part of the timeline to move the playhead. This works while playing too.
- **Move a subtitle:** drag the middle of its block.
- **Change its length:** drag the left or right edge.
- Blocks stop at their neighbours, so subtitles cannot overlap.
- **Add or delete:** right-click an empty spot in a subtitle lane to add a subtitle there, or right-click a block to delete it. A new subtitle is two seconds long and is fitted into the available gap.
- **One menu at a time:** right-clicking somewhere else closes the previous menu; a left click on the timeline does the same.
- **Navigate:** the mouse wheel scrolls, Ctrl+wheel zooms around the pointer, and middle-drag or Alt+left-drag pans. The `−` and `+` buttons also zoom.
- **Fit the window:** track rows grow and shrink with the panel height.

## Playback

**Play** produces sound for the tracks that are ticked. The playhead follows the audio, and clicking again pauses.

If no track is ticked, playback does not start; the status bar asks you to tick one. Changing the ticks or deleting a track stops playback and prepares the mix again next time.

Playback is for preview only. Preview playback covers the first 30 minutes of very long media; exports are not limited.

## Style and preview

The style bar controls the font, size, text color, outline and distance from the bottom.

- **Fonts:** the font menu lists system fonts and includes **Import other font…** for `.ttf`, `.otf` and `.ttc` files. Imported fonts are copied into your user profile and remain available after a restart; they do not have to be installed for the whole system.
- **Colors:** click a color swatch to open a full palette with hue, shade, transparency and common colors. The preview updates as you choose. A hex value can also be typed directly.
- **Quality:** Smooth, HD and Ultra set the render size. The current export size is shown next to the quality selector, for example `226×122 · 1 line · 20 chars/line`.

The preview shows the subtitle inside the full video frame. The exported overlay video is cropped around the subtitle text, so it is much smaller than the source frame. Each track can produce a differently sized overlay.

## Export

Choose **Export** in the preview panel:

| Format | Use |
|---|---|
| WebM (VP9) | Transparent subtitle overlay for most editors. |
| MOV (ProRes 4444) | Transparent overlay for professional editing tools. |
| MP4 (H.264) | Standard video, but no transparency. |
| SRT / ASS / VTT | Subtitle files for players and other editors. |

- Exporting overlay video produces one file per ticked track that has subtitles.
- Each track's audio can also be exported as `.m4a` from the button that appears when you hover over its row.
- Exports use the source frame rate.
- If an export fails, a red message appears at the bottom left. Hover over it to read the full text and click to copy it.

## Settings

Open **Settings** from the model menu.

- **Providers:** the left side lists local and cloud recognition services. Click a row to edit it; the whole row is clickable. **Add model provider** adds an OpenAI-compatible service. **Test connection** checks the key and address and updates the available models.
- **Local models:** download, resume, stop or delete models.
- **Denoise before recognition:** toggle noise reduction.
- Changes are saved automatically when the window closes; there is no Cancel button.
- Window position, size and panel layout are remembered automatically.

> **API keys are stored in plain text on this computer.** Anyone who can read your user profile can read them. Be careful on shared computers, and revoke a key at the provider if it may have leaked.

## Language

Use the language menu in the bottom bar to switch between Simplified Chinese and English. The change is immediate and does not require a restart. **More languages…** reopens the language picker.

On first launch the app asks you to choose a language; after that it remembers your choice.

## Troubleshooting

- **Something failed:** the red message at the bottom left shows the first part of the error. Hover to expand it, click to copy it.
- **Unexpected crash:** details are written to `crash.log` in the app's user settings folder so the problem can be reported.
- **Cannot write settings or exports:** if permissions are the cause, the app offers to restart with administrator rights.
- **SmartScreen warning:** the installers are unsigned; choose **More info → Run anyway**.
- **Slim version says ffmpeg is missing:** use the download links in the dialog, put the files in the folder shown there (or add ffmpeg to PATH), then click **Recheck**.
- **No transparency in MP4:** MP4/H.264 does not support an alpha channel. Use WebM or MOV for overlays.

## Known limitations

- MP4 exports cannot contain transparency; use WebM or MOV for overlays.
- Cloud recognition needs an API key; offline recognition needs a downloaded local model.
- Long recordings are processed one track after another. You can stop at any time, but a stopped track starts again from the beginning.
- Preview playback covers only the first 30 minutes of very long media.
- API keys are saved in plain text, as noted in Settings.

## Source code

The source package contains the complete source code, build scripts, the MIT license and both README files. Developers can build and package the app with the included PowerShell scripts.

---

Developed with DeepSeek and Kimi K3.
