# EchoVRShade

ReShade effects **inside the headset** for Echo VR (PC, DX12), without SteamVR.

Stock ReShade only renders in VR through OpenVR/OpenXR. Echo VR uses the Oculus runtime (LibOVR), so ReShade normally
only shows on the desktop mirror. This project is a patched ReShade that hooks LibOVR and applies effects to the eye
textures before they are submitted to the headset, plus a small app to install it and tune the effects.

## Requirements

- Echo VR PC build (`bin\win10\echovr.exe`)
- A Quest/Rift running through **Meta Quest Link or Air Link** (the Oculus runtime).
  Virtual Desktop, ALVR or SteamVR/ReVive do not use the Oculus runtime, so effects will only show on the desktop.

## Install

1. Download `EchoVRShade.zip` from [Releases](../../releases) and extract it anywhere (keep `reshade-shaders` next to the exe).
2. Run `EchoVRShade.exe`, pick your Echo VR folder, click **Install / Update**.
3. Start Echo with **Launch Echo**.

The app installs ReShade as `dinput8.dll` (a `dxgi.dll` never loads in Echo, because the Oculus runtime loads the
system one first) and copies the bundled shaders into the game folder.

## Tuning

- The headset uses `ReShadeVR.ini` / `ReShadeVRPreset.ini`. Change effects with the app's tabs; changes apply live.
- The ReShade overlay on the desktop (Home key) edits `ReShadePreset.ini`, which is **not** the headset preset,
  unless you turn on the app's option to point the desktop overlay at the headset preset.

## Troubleshooting

Check `ReShade.log` in `bin\win10` after a session:

| Log | Meaning |
| --- | --- |
| no `Redirecting ovr_CreateTextureSwapChainDX` | Echo is not running on the Oculus runtime (see Requirements) |
| that line, but no `Creating LibOVR effect runtime` | The hook loaded but never received eye textures; open an issue with the log |
| both lines | Working; check the effects are enabled in the headset preset and their shaders exist |

## Uninstall

Click **Uninstall** in the app (removes `dinput8.dll`; your presets are kept).

## Building

- ReShade: clone [crosire/reshade](https://github.com/crosire/reshade) at commit `4a50d1eddace85734871d91792ff214f13f66c01`,
  `git apply reshade/reshade-libovr.patch`, build `ReShade.sln` (Release, 64-bit) and copy `ReShade64.dll` to
  `ReShade64-libovr.dll` next to the `app` folder.
- App: run `app\build.cmd` (uses the C# compiler that ships with Windows); it embeds `..\ReShade64-libovr.dll`.

## Credits

- [ReShade](https://github.com/crosire/reshade) by crosire (BSD 3-Clause); the patch is under the same license.
- Bundled shaders keep their own licenses in their headers: SweetFX (CeeJay.dk), CAS (AMD), MagicBloom (luluco250).
