# MeddlingIdiot.Greenlight.SabreClient

One lightsabre standing on your desktop. It changes hilt and blade colour when the build
changes: a brushed-steel Graflex burning green while everything passes, a brass Sentinel in
amber when a pull request wants you, a dark crossguard in red when a pipeline breaks — and a
plain dark hilt with the blade in when there is no Greenlight to ask.

A runnable reference consumer of the [Greenlight](https://github.com/meddlingidiot/MeddlingIdiot.Greenlight)
SDK, and a demonstration of how little an app needs to do to use it. The point of it is what
it does **not** have. No Azure DevOps client, no GitHub client, no token, no polling loop —
everything it knows arrives through the SDK, from the Greenlight already running on the
machine. Strip out the drawing and the tray icon and the integration is about twenty lines,
all of them in [`App.cs`](Greenlight.SabreClient/App.cs).

## Running it

```bash
dotnet run --project Greenlight.SabreClient
```

Windows only: the click-through overlay and the work-area maths are Win32. The SDK itself is
not — it is plain .NET, and the same twenty lines work anywhere.

## What the sabre means

| Blade | Hilt | Greenlight |
|---|---|---|
| **Green** | Graflex — brushed steel and gold | Everything passing |
| **Amber** | Sentinel — brass and bone | Yellow: a pull request is waiting on you |
| **Red** | Crossguard — dark iron and hot copper | A pipeline is broken |
| **Out** | Curved — gunmetal, dark | No Greenlight running, or nothing to report |
| **Breathing** | — | A build is running right now, whatever colour it is |

That last row is Greenlight's own rule and worth repeating: a broken pipeline stays red while
it rebuilds. "Something is happening" is said by the pulse, not by the colour — a sabre that
went amber the moment somebody started a fix would be contradicting the thing it is reporting
on.

The pulse is a four-second breath, about fifteen a minute, which is roughly a person sitting
still. The whole blade takes it — halo, body and white core all narrow and dim together — but
not evenly: the halo gives up a fifth of its reach and the core only a tenth, so the sabre
swells as one piece while the core stays the brightest thing on screen throughout. The
unevenness is the point. A pulse spent on brightness alone was slow, gentle and completely
unnoticeable, because the eye reads a change in size far more readily than a change in
brightness; a core dipping as far as the halo does reads as the blade faltering, and
"faltering" is uncomfortably close to "going out", which already means something else here.

### The changeover

A status change is not a recolour. The blade retracts in the colour it was lit in, the hilt is
swapped while it is dark, and the new one ignites in the new colour — about a second, end to
end. A hilt that changed shape under a lit blade would read as a rendering glitch; swapped in
the dark it reads as somebody having drawn a different sabre, which is the thing the toy is
trying to say. That is the whole reason the scene tracks what it is *wearing* separately from
what Greenlight last *said*: the two differ for exactly as long as the changeover takes.

## Moving it about

**Move it about…** in the tray turns the overlay into something you can click, and for as long
as it is on:

- **Drag the hilt** to carry the sabre anywhere on the screen.
- **Drag the blade** to aim it and to draw it out longer or shorter, in one gesture. Hold
  **Shift** to snap the angle to 15°.
- **Scroll over the hilt** to make it bigger or smaller.
- **Esc**, a click on bare desktop, or the tray item again, to finish.

Every drag is written straight to `sabre.json` where it lands, so there is nothing to save. The
blade is held out while you are arranging even if Greenlight is away — a blade you cannot see
is a blade you cannot grab.

The rest of the time the sabre ignores the mouse entirely, which is the whole reason arrange
mode has to be a mode: the overlay covers the desktop, and a desktop you cannot click is not a
desk toy, it is a fault.

## The tray

Everything else lives on the mascot in the notification area:

- **Sabre out** — put it away and bring it back. Clicking the icon does the same.
- **Which way it points** — straight up, leaning either way, or laid across the screen.
- **Blade length**, **Hilt size**, **How much glow**, **How solid**.
- **Where it stands** — the desktop above the taskbar, or the whole screen.
- **Leave the hilt when it goes dark** — on by default. A blank desktop looks like the app
  crashed; a dark hilt looks like what it is.
- **Start with Windows** — one value under the current user's `Run` key. No scheduled task, no
  service, nothing needing an administrator, and it is read back from the registry each time
  the menu opens rather than remembered, so turning it off in Task Manager's Startup tab is
  reflected here rather than contradicted.
- **Edit the sabre…** opens `sabre.json`. **Reload the file** picks up hand edits without a
  restart.

Every setting is written straight back to the file, so the menu and the JSON are never two
different sets of settings — except **Start with Windows**, which is not in the file at all.
The registry is the only record of it, deliberately: Windows offers the user two other places
to turn the same thing off, and a copy of the answer in `sabre.json` would be wrong the moment
they used either, with no way to tell which of the two was lying.

What gets registered is the stable shim beside the install
(`…\Greenlight.SabreClient\Greenlight.SabreClient.exe`), never the versioned copy under
`current\` that the app is actually running from. Registering the running path works perfectly
until the first update replaces `current`, and then fails at every login afterwards — which is
not a fault anybody connects to a setting they turned on weeks earlier. A dev build out of
`bin` has no shim and registers itself; the path is also re-checked at every startup, in case
an older registration got it wrong.

The one thing deliberately *not* in the menu is which hilt goes
with which state: four hilts, each with a style and two metals, is a menu nobody would finish
reading, and it is the one thing somebody will want to sit and fiddle with — which is what a
file is for.

## The sabre itself

`%AppData%\Greenlight.Sabres\sabre.json`, written out with the defaults on first run. Five
hilt styles to choose between — `Graflex`, `Crossguard`, `Curved`, `Sentinel`, `Duelist` —
each drawn from its own metalwork: stacked grip rings and a side clamp, a vented quillon
housing, a bowed duelling grip, a spiral wrap with a belt ring, a fluted body with a stone set
in the pommel.

```json
{
  "Sabre": {
    "AnchorX": 0.5,
    "AnchorY": 0.97,
    "Angle": 0,
    "BladeFactor": 0.5,
    "HiltScale": 1.2
  },
  "WhenGreen": { "Hilt": "Graflex", "Metal": "#C9CDD4", "Accent": "#C9A227" },
  "WhenAmber": { "Hilt": "Sentinel", "Metal": "#B08D57", "Accent": "#E8E2D0" },
  "WhenRed": { "Hilt": "Crossguard", "Metal": "#8A8F98", "Accent": "#B4642E" },
  "WhenOff": { "Hilt": "Curved", "Metal": "#6E7480", "Accent": "#A8AEB8" },
  "Glow": 1.0
}
```

`Metal` is the body and `Accent` is the inlay — the rings, the filigree, the rivets, the stone
in a pommel that has one. Nothing stops all four states wearing the same hilt in four different
metals, or the same metal in four different shapes.

`AnchorX` and `AnchorY` are fractions of the screen rather than pixels, so the sabre survives
the monitor it was placed on: a laptop undocked from a 4K display finds it where it was left
instead of jammed in the corner. `Angle` is degrees clockwise from straight up, and
`BladeFactor` is blade length as a fraction of the screen's height.

A file that cannot be parsed falls back to the defaults rather than refusing to start, and so
does a block deleted by hand. It is a desk toy; a stray comma should not cost you the whole
thing.

## Building on it

```bash
dotnet add package MeddlingIdiot.Greenlight.Sdk
```

That is exactly what this repository does — the SDK comes from nuget.org like any other
dependency.

Greenlight itself is a separate product and is not open source — this sample is. The three
things it wants you to notice: the client sits in a disabled state and reconnects on its own
when Greenlight is not running, so there is nothing to guard; the events arrive on a background
thread, so anything touching your UI has to get itself back onto the UI thread; and
`IsBuilding` is a separate flag from `Status`, meant to be shown separately. All three are
worked through in `App.cs`.

## Tests

```bash
dotnet test
```

`SabreScene` — where the hilt sits, which way it points, how far the blade has run out, which
hilt is currently being worn, and what the mouse is over in arrange mode — is deliberately free
of Avalonia so it can be tested without a window. That is not ceremony: a hilt that swaps while
the blade is still lit, a blade that loses a little length every time you drag it, or a
grabbable line that has drifted away from its own picture, is not something anyone is going to
catch by looking at a desktop.

## Licence

MIT — see [LICENSE](LICENSE). The MeddlingIdiot name and mascot are not part of that grant; see
[TRADEMARKS.md](TRADEMARKS.md).

Star Wars, Jedi and lightsaber are trademarks of Lucasfilm Ltd. This is an unaffiliated
fan-adjacent desk toy: it draws a glowing sword, and it is not endorsed by or connected with
Lucasfilm or The Walt Disney Company.
