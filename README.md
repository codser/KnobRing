<p align="center">
  <img src="docs/logo.png" width="96" alt="KnobRing logo">
</p>

<h1 align="center">KnobRing</h1>

<p align="center">
  Turn the volume knob on your keyboard into a radial action menu for Windows, inspired by the Actions Ring on Logitech's MX mice.<br>
  Press the knob to open the ring, turn it to choose, press again to run.
</p>

---

KnobRing was built for the **AULA F75**, but works with any keyboard whose knob sends volume or media keys.

## Features

- **A ring of actions at your pointer.** Press the knob and up to 12 actions appear around the mouse. Turn the knob (or point the mouse in a direction, or scroll) to pick one, and press again or click to run it.
- **A different ring for each app.** It ships with rings for Premiere Pro, After Effects, Photoshop, DaVinci Resolve and File Explorer, plus a Default ring for everything else. You can add any app that's running in one click.
- **What an action can do:**
  - send a keyboard shortcut, including sequences like `Ctrl+K, Ctrl+C`
  - open an app, file, folder or website
  - type a piece of text
  - press a media key
  - open a sub-ring with more actions
- **Liquid highlight.** The highlight flows between options. A *Liquid feel* slider goes from a solid glide to fully gooey.
- **Minimal line icons** for every action, plus a bouncy opening: the centre pops in first, then the options appear one by one down both sides of the ring.
- **A settings window in the style of G HUB.**
  - Edit any slot: record shortcuts, pick from 32 icons, use letters or your own PNG.
  - The live preview ring follows the real knob while the window is in front.
- **Learn my knob.** Press and turn the knob once and KnobRing learns which keys your keyboard sends.
- **Clicks stay with the ring.** While the ring is open, clicks never reach the app underneath.
- **Your volume stays put.** Many keyboards send the knob as media keys that Windows always acts on. While the ring is open, KnobRing puts your volume and mute back to how they were.
- **Light and self-contained.** One small `.exe` with no installer that runs in the tray. It can start with Windows, and it updates itself when `KnobRing.update.exe` is dropped next to it.

## Getting started

1. Download `KnobRing.exe` from [Releases](../../releases), or [build it yourself](#building).
2. Run it. The KnobRing window opens and an icon appears in the system tray.
3. If pressing the knob doesn't open the ring, go to **Settings → Learn my knob** and follow the three prompts.
4. On the AULA F75: if turning the knob changes the lighting, hold the knob down for a few seconds until the lights flash. That switches it to volume mode.

> Windows may show "Windows protected your PC" because the exe isn't code-signed. Click **More info → Run anyway**.

## Using it

| Do this | To |
| --- | --- |
| Press the knob | Open the ring for the app in front |
| Turn the knob, scroll, or move the mouse in a direction | Move the highlight |
| Press the knob again, left-click, or Enter | Run the highlighted action |
| Point at the centre and click, right-click, or Esc | Close the ring without doing anything |

When the ring is closed, the knob keeps controlling the volume as usual.

## Settings file

Everything is saved in `%APPDATA%\KnobRing\rings.json`. The settings window writes it for you. If you edit it by hand, KnobRing reloads it as soon as you save. A ring looks like this:

```json
{
  "name": "Photoshop",
  "apps": ["photoshop.exe"],
  "items": [
    { "label": "Brush", "icon": "brush", "type": "keys",  "value": "B" },
    { "label": "Mute",  "icon": "mute",  "type": "media", "value": "mute" },
    { "label": "Docs",  "icon": "globe", "type": "run",   "value": "https://helpx.adobe.com/photoshop" }
  ]
}
```

| `type` | `value` |
| --- | --- |
| `keys` | A shortcut like `Ctrl+Shift+E`. Separate several with commas to press them in order. |
| `run` | An app, file, folder or URL. Optional `args`. |
| `text` | Text to type |
| `media` | `playpause`, `next`, `prev`, `stop`, `mute`, `volup`, `voldown` |
| `ring` | The name of a sub-ring (a ring with no `apps`) |

## Building

KnobRing is plain C# on .NET Framework 4.x and WinForms, with no NuGet packages.

- **Windows:** run `build.bat`. It uses the C# compiler that comes with Windows.
- **Linux / macOS:** install Mono and run `./build.sh`.

Both write `out/KnobRing.exe`.

## How it works

| File | What it does |
| --- | --- |
| `Program.cs` | Tray app, ring flow, knob and mouse routing, self-update |
| `Hooks.cs` | Low-level keyboard and mouse hooks |
| `RawKnob.cs` | Raw-input listener for media-key knobs, and the volume guard (Core Audio) |
| `RingRenderer.cs` | Draws the ring and runs the spring physics of the liquid highlight |
| `RingWindow.cs` | The see-through, never-focused overlay window |
| `SettingsForm.cs`, `Ui.cs`, `Dialogs.cs` | The settings window and its custom-drawn dark UI |
| `Actions.cs` | Parses and sends shortcuts, text, media keys and launches |
| `Config.cs` | Loads and saves `rings.json` |
| `VectorIcons.cs` | KnobRing's own line icons, drawn from SVG-style paths |

## Notes

- Apps running as administrator only accept shortcuts from KnobRing if KnobRing is also run as administrator.
- Raw media keys can't be blocked, so the Windows volume pop-up may flash while you turn the knob with the ring open. The volume itself won't change.

## License

[MIT](LICENSE): free to use, change and share, as long as the copyright notice stays with it.
