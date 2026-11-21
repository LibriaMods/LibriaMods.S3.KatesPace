# Kate's Pace – Syberia 3

Faster walking/running and line skipping for **Syberia 3** (Unity 5.4, BepInEx 5).

| Key | Action |
|---|---|
| **+** / **−** | Speed multiplier ±0.1 (default ×1.6) |
| **F8** | Extra speed on/off |
| **Space** / **B (Circle)** | Skip the current spoken line (dialogues and cutscenes; never choices) |

Settings: `BepInEx\config\LibriaMods.S3.KatesPace.cfg`.

Kate moves with root motion, so her animator is sped up only while she is moving.

## Install
1. [BepInEx 5 x64](https://github.com/BepInEx/BepInEx/releases) extracted into the game folder.
2. `LibriaMods.S3.KatesPace.dll` → `BepInEx\plugins`.

## Build
Open `LibriaMods.S3.KatesPace.slnx`. References come from the game folder (default `D:\Games\Syberia 3`,
override `Syberia3Dir` in `..\LibriaMods.user.props`); the build copies the DLL into `BepInEx\plugins`.
