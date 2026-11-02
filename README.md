# TizenAmbilight

[![pipeline](https://gitlab.com/ritwick7/TizenAmbilight/badges/master/pipeline.svg)](https://gitlab.com/ritwick7/TizenAmbilight/-/pipelines)
[![license: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
[![site](https://img.shields.io/badge/site-ritwickverma.dev-ff6f00)](https://www.ritwickverma.dev/)
[![tip](https://img.shields.io/badge/tip-buy_covfefe_for_Ritwick-e91e63)](https://buycovfefefor.ritwickverma.dev/)

**A real-frame ambilight for Samsung Tizen TVs.** One small headless app runs *on the TV*, reads the
decoded video picture straight out of memory, and drives a [WLED](https://kno.wled.ge/) LED strip over
your Wi-Fi.
On the S95B it instead reads hardware-measured spot colours; see [S95B setup](#s95b-setup-build-v7).

**Designed to keep ambilight running during DRM-protected playback:** when the TV refuses full-frame capture, the app can switch to the TV’s built-in spot-colour sampler. It does not capture or copy protected video, and the result is coarser and slower than full-frame capture. Protected playback has been verified on the S95B, but support may vary by TV and firmware.

## Tizen 6.5 support and original work

The Tizen 6.5 additions extend the original project with a separate hardware-sampling path for
compatible TVs such as the S95B, selectable with `captureBackend: "ppi"`. Instead of capturing a full
image, it measures colour in small areas around the screen and interpolates those readings across the
LED zones. This makes Ambilight possible on older firmware, with coarser and slower updates than the
full-frame path. Credit and thanks to [ritwick7](https://gitlab.com/ritwick7), the original developer,
for the foundational work bringing native Ambilight to Samsung Tizen TVs.

[![TizenAmbilight running on a Samsung S84F: the LED glow behind the TV follows the colours of the picture](docs/media/showcase-preview.webp)](docs/media/showcase.mp4)

*A 10-second loop from a phone recording on a 2025 Samsung S84F, using the full-picture capture path at
about 20 fps. [Watch the full 32-second video](docs/media/showcase.mp4) (1080p, converted from the
phone's HDR recording to standard dynamic range, audio removed).*

* **No extra hardware.** No capture card, HDMI splitter or Raspberry Pi; just the TV and a WLED strip.
* **No middleware.** No Hyperion or HyperHDR server in between. One process goes from capture to colour
  maths to UDP, at about 25 fps.
* **The whole picture on the S84F.** The full-frame backend averages the 960×540 video plane into
  perimeter zones. The S95B uses a handful of hardware sample points instead.
* **HDR-aware full-frame path.** It decodes PQ (HDR10), maps BT.2020 colours into what LEDs can show,
  and adapts the tone curve to the scene. The sampler uses measured RGB without this HDR decode.
* **Keeps going under DRM, coarsely.** Streaming apps withhold the video plane. The app then switches to
  the TV's own built-in colour measurement (the one Samsung's Hue Sync support uses): a handful of spot
  colours instead of the whole picture, and far slower. See [DRM content](#drm-content).

> [!WARNING]
> **Status: early.** Full-frame capture was developed on a 2025 Samsung S84F (Tizen 9);
> hardware colour sampling also works on a 2022 S95B (Tizen 6.5). It relies
> on an undocumented Samsung library that may be missing or different on other models and firmware.
> If you try it on another TV, please [report what happened](#device-reports), whether it worked or not.

---

## Contents

- [Tizen 6.5 support and original work](#tizen-65-support-and-original-work)
- [Compatibility](#compatibility)
- [S95B setup](#s95b-setup-build-v7)
- [DRM content](#drm-content)
- [What you need](#what-you-need)
- [Install](#install)
- [Configuration](#configuration)
- [Updating safely](#updating-safely)
- [Troubleshooting](#troubleshooting)
- [FAQ](#faq)
- [How it works](#how-it-works)
- [Contributing and device reports](#contributing)
- [Security](#security) · [Credits](#credits) · [License](#license)

## Compatibility

**Full-frame capture is verified on the S84F; spot-colour sampling is also verified on the S95B.**
These are different backends with different spatial and temporal resolution.

| Model year | Tizen | Status |
|---|---|---|
| 2025 | 9.0 | ✅ Works on the S84F (OLED, `QN77S84FAEXZA`). Other Tizen 9 models are likely to work but unconfirmed; one Q7F user reports capture is blocked unless the app is signed with a partner certificate ([#1](https://github.com/RitwickVerma/TizenAmbilight/issues/1)). |
| 2026 | 10.0 | ❔ Untested |
| 2022 | 6.5 | Works on `QE55S95BATXXN` using `captureBackend: "ppi"` and the legacy `cs_ve_*` hardware sampler (build v7). The `tizen90` build starts without retargeting. Known RGB/white/black video was verified through actual WLED live pixels. Full-frame capture, protected streaming and HDMI are not verified on this TV. |
| 2023–2024 | 7.0–8.0 | Untested. Sampler discovery supports the `ve_*` family as well; runtime and capture still need device verification. |
| 2021 | 6.0 | ❌ One report (QN90A, [#2](https://github.com/RitwickVerma/TizenAmbilight/issues/2)): starts only after retargeting to `tizen80`; the video-plane call exists with the documented layout but returns `rc=-95`. Was the build signed partner? Open question. |
| 2020 and older | ≤ 5.5 | ❌ Unknown, probably not without porting work. Untested. |

What the app needs from the TV:

| need | used for | if missing |
|---|---|---|
| A Tizen .NET runtime that can load a `tizen90` app (the manifest asks for API level 6.5, the project targets `tizen90`) | running at all | the app does not install or start |
| `libvideo-capture.so` exporting `secvideo_api_capture_video_main` | the video plane (the normal source) | control and telemetry stay available; select the independent sampler with `backend=2` or `captureBackend: "ppi"` |
| `libvideoenhance.so` exporting a complete RGB-measure family (`cs_ve_*`, `ve_*` or `ppi_ve_*`) | the DRM fallback, or the primary source in `ppi` mode | sampler telemetry explains the load, missing-symbol or invalid-condition failure |

**How to check on your TV** (needs `probeEndpoint` set, see
[diagnostics](docs/development.md#diagnostics-telemetry-and-frame-dumps)):

* `tv` in any telemetry report ends in `captureApi=ok` or `captureApi=UNAVAILABLE`, and the startup log
  has `video-plane backend ready: ...` or `VIDEO-PLANE backend NOT ready: <why>`. If it is not ready, the
  plane backend cannot work yet. In forced `ppi` mode the plane is deliberately not initialized;
  `captureApi=UNAVAILABLE` does not mean the sampler is unavailable.
* The DRM fallback is only tried when a streaming app withholds the plane. The log then says either
  `ppi sampler ready: api=FAMILY cond rc=0 slots=N block=WxH settle=Tms ...` or `ppi sampler NOT ready: <why>`. To
  check without waiting for DRM content, send `backend=2` on the [control channel](docs/configuration.md#live-control-channel)
  and read the `ppi` block in telemetry (`ready`, `init_error`, `cond_slots`, `cond_sleep_ms`); send
  `backend=0` to go back. The slot count and settle time come from what your TV reports, never from the
  numbers measured on the S84F.

Reports from other TVs are the most useful thing you can send; see [device reports](#device-reports).

### S95B setup (build v7)

Set `captureBackend` to `"ppi"` in your private `config.json` and use a Samsung partner certificate.
This path needs neither the video-plane nor file-capture library. Start with `ppiZones: 12` and
`ppiSettleMs: -1` (the TV-reported delay); the tested TV reported two slots, 16x16 blocks,
3840x2160 coordinates and 20 ms settle time. Steady sampling reached about 99 readings/s,
or about 8 updates/s per measured position at 12 positions; bar probes temporarily reduce that rate.
The output is interpolated over the whole strip, not a full captured picture.

The initial end-to-end SDR validation used `adaptiveGamma: 0`, `gamma: 1`, `black: 0`,
`punch: 0` and `staleMs: 0`. These are a neutral starting point, not automatic colour calibration.
The sampler uses measured RGB directly; the plane's PQ decode and gamut conversion do not apply.
Red, green and blue had the correct dominant channel at WLED, white was non-black, and black
produced all-zero LEDs. Exact colour fidelity and physical strip alignment still need visual checking.
Four logical edge-midpoint checks also passed. Protected streaming, HDMI, standby/wake and cold-boot
autostart have not been verified on the S95B.

See the [copyable sampler settings](docs/configuration.md#older-tv-hardware-sampler),
[SDK-free partner signing](docs/development.md#sdk-free-signing-with-apps2samsung-cli) and
[engineering notes](docs/how-it-works.md#s95b-compatibility-and-api-discovery).

## DRM content

Streaming apps mark their picture as copy-protected, and the TV then refuses the video-plane capture
(`rc=-4`). **The protected picture is never captured or copied.** Instead, after three refusals in a
row, the app switches to the TV's own colour-measurement block (`libvideoenhance` `ppi_ve_*`), the
interface Samsung's Hue Sync support is built on. It reads a few average colours, inside the video
processor, and never receives a frame. The strip keeps following the picture, but it is a different and
much weaker source:

* **Only 2 measurement slots**, so at most about **100 readings per second** at the TV's own settle time
  (20 ms), or about 116 at 17 ms, the shortest wait that was still fresh on the S84F. The default is the
  TV-reported 20 ms; on the S84F that measured **80-99 readings per second** in the final build.
* **Spots, not the picture.** `ppiZones` (default 24) spots are measured round the screen edge and
  interpolated onto the other zones. Per-spot refresh on the S84F (measured at a 17 ms settle): 8 spots ≈
  12-15 Hz, 12 ≈ 8-10 Hz, **24 ≈ 4-5 Hz**, 73 ≈ 1.4 Hz. At the default 20 ms settle the default 24 spots
  measured **3.3-4.1 Hz**. The app still renders about 24 frames per second, but that is
  *interpolation* between those updates, not new measurements. Do not read it as a frame rate.
* **Letterbox bars are visible to it** (the plane hides them), so the app detects them and moves the
  spots into the picture (`ppibars`).
* Colours are the TV's 10-bit measurement reduced to 8 bits, with no HDR decode. How close that is to the
  plane's colours has **not** been measured.
* Hand-back to the plane is implemented (the plane is polled twice a second) and was seen to trigger once
  on a pre-release build: in a static scene the plane returned `rc=0` while a streaming app was still on
  screen, the strip went back to the plane, and about 20 s later back to the fallback after three more
  `rc=-4`. So expect occasional flips between the two sources. The case that matters, protected playback
  actually ending, has **not** been observed.

Details and the measurement method are in [docs/how-it-works.md](docs/how-it-works.md#drm-fallback).

## What you need

* A Samsung Tizen TV with **Developer Mode** enabled. Open **Apps**, type `1 2 3 4 5` on the remote,
  switch Developer Mode on and enter your computer's IP, then unplug the TV for about 60 s.
* A **WLED** controller (ESP32/ESP8266) driving an LED strip around the TV, on the same network.
  Up to 490 LEDs.
* **Recommended in WLED:** give the controller a fixed IP (a DHCP reservation), and enable
  *Settings → Sync Interfaces → Realtime → "Force max brightness"* (or set the brightness to 255). WLED's master
  brightness also scales realtime data, so a controller at the default of 128 shows the ambilight at
  50%.

## Install

> [!IMPORTANT]
> **There is no downloadable, ready-to-install `.tpk`, and there cannot be one.** Samsung TVs only install
> packages signed with a certificate that embeds *that TV's* unique ID (DUID). A `.tpk` signed for the
> author's TV installs on no other TV. The `.tpk` attached to each
> [release](https://github.com/RitwickVerma/TizenAmbilight/releases) is built with the Tizen SDK's
> default certificate, which a retail TV rejects; it exists only as input for Apps2Samsung to re-sign.
> Pick one of the two routes below.

### Option 1 (recommended): Apps2Samsung, no build

TizenAmbilight is listed in the Apps2Samsung
[community catalog](https://github.com/Apps2Samsung/tizen-community-packages) (category **Tools**).
[Apps2Samsung](https://github.com/Apps2Samsung/Apps2Samsung) downloads the latest
[release](https://github.com/RitwickVerma/TizenAmbilight/releases), signs it for your TV and installs it.

1. Enable Developer Mode on the TV (see [What you need](#what-you-need)) and connect Apps2Samsung to it.
2. Make sure your Samsung certificate is **partner** level (see the note below).
3. Find **TizenAmbilight** in the catalog and install it. The install form asks for:

| field | example |
|---|---|
| WLED IP address | `192.168.1.50` |
| Number of LEDs | `96` |
| Where the strip's first LED sits, counted clockwise from the screen's top-left corner | `0` |
| Strip runs counter-clockwise? | `0` |
| *(optional)* Spots measured under DRM content (`ppiZones`, 4-73) | `24` |

4. Power-cycle the TV (see [Starting it](#starting-it)).

Not sure about the mapping answers? Use `0` and `0`, then see [Getting the LED mapping right](docs/configuration.md#getting-the-led-mapping-right).
Leave the optional field alone unless you know you want it. To change an answer later, install again
with the new values (read [Updating safely](#updating-safely) first).

> [!IMPORTANT]
> **Sign with a partner-level certificate.** Apps2Samsung signs at the *public* level unless you choose
> partner when it creates your certificate; an existing public certificate has to be re-created as
> partner. With a public certificate the app runs but Tizen blocks the capture libraries, so the strip
> stays dark (startup log: `Operation not permitted`). An install through Apps2Samsung with its partner
> option has not been confirmed working yet: please [report](#device-reports) either way.

### Option 2: build from source and sign it yourself

You need your own Samsung certificate profile **for your own TV's DUID**, at the **partner** privilege
level (created with Tizen Studio's Certificate Manager or the standalone Apps2Samsung CLI).
A public-level certificate installs, but capture is blocked. For the S95B, include
`"captureBackend": "ppi"` in your private settings before building.

```bash
git clone https://github.com/RitwickVerma/TizenAmbilight.git && cd TizenAmbilight
cp TizenAmbilight/config.example.json TizenAmbilight/config.json      # set wledIp, ledCount, ...
dotnet msbuild TizenAmbilight/TizenAmbilight.csproj -restore -t:Rebuild -p:Configuration=Release
```

Then re-sign the TPK with your saved profile and install it using either signing route in
[docs/development.md](docs/development.md#sign-and-install-with-your-own-certificate).

### If you only set `wledIp`

On the S84F's default full-frame path, nothing else is required. The S95B also requires
`captureBackend: "ppi"`. With **no** WLED address the app sends no LED packets (its log and telemetry
say `no wledIp configured`). The telemetry collector (`probeEndpoint`) and control channel
(`controlUrl`) are optional helpers, off unless you set them.

### Starting it

TizenAmbilight is a background service with no icon or screen. It is set to start when the TV boots
(`on-boot`), so after installing, **power-cycle the TV** (unplug it, or hold the power button). Some
other sideloaded projects report that `on-boot` is ignored on their firmware. If the strip stays dark
after a cold boot, see [Troubleshooting](#troubleshooting).

## Configuration

Settings live in `config.json`, which is bundled into the app when it is built or installed. The
essentials:

```json
{
  "wledIp": "192.168.1.50",
  "ledCount": 96,
  "mapOffset": 0,
  "mapReverse": 0
}
```

Everything else (brightness curve, smoothing, HDR handling, bar detection, the sampler's
`ppiZones`, ...) has a tuned default for the reference S84F. On the S95B start with the
[sampler settings](docs/configuration.md#older-tv-hardware-sampler).
[docs/configuration.md](docs/configuration.md) lists every setting. It also covers the optional
**live control channel**, which lets you tune settings and line the strip up while you watch, without
reinstalling.

## Updating safely

> [!CAUTION]
> Installing over a running copy kills it instantly. If that happens in the middle of a capture call,
> the TV's video scaler can get stuck. The strip then stays dark until the TV is **unplugged for a
> minute**. Nothing is damaged, but it is confusing.

* If you run the [control channel](docs/configuration.md#live-control-channel), send `pause` before
  installing. Wait for fresh telemetry reporting `last_status: "paused by control channel (no capture calls)"`
  and `ppi.sampler_running: false`; only then install and send `run` afterwards.
* Otherwise: install, then unplug the TV for 60 s and plug it back in. That clears a stuck scaler and
  starts the new version.
* Upgrading from a build older than v5 (package id `io.gh.hermes.TizenAmbilight`)? The package id has
  changed, so uninstall the old app first. Otherwise two copies fight over the strip.

## Troubleshooting

| symptom | likely cause and fix |
|---|---|
| Startup log says `Operation not permitted` loading a capture library | The package was signed with a **public** certificate. Re-sign with a **partner** certificate ([how](docs/development.md#sign-and-install-with-your-own-certificate)). |
| Strip never lights up | `wledIp` not set, or not reachable from the TV. Check that WLED shows *"Live"* / a realtime source. Power-cycle the TV once after installing. |
| Strip was working, now stays dark after an update | A stuck scaler (capture `rc=-2`). Unplug the TV for 60 s. |
| Strip is coarse and slow during some apps | DRM-protected content (`rc=-4`). The app has switched to the TV's colour-measurement fallback: a few spots, updated a few times per second. This is a hardware limit, see [DRM content](#drm-content). If it goes fully dark instead, that TV has no `libvideoenhance` measure API: the log says `ppi sampler NOT ready`. |
| Everything is dim | WLED brightness below 255 (it scales realtime data). For a brighter look, raise `gamma`, not `gain`. |
| Colours appear on the wrong side | `mapOffset` / `mapReverse`. Use the test pattern ([guide](docs/configuration.md#getting-the-led-mapping-right)). |
| Flicker between two looks | Something else also streams to WLED (Hyperion, the WLED app, sync). Only one sender can own the strip. |
| Strip glows WLED's own colour (often orange) | The app is not running, so WLED fell back to its own preset after live mode timed out. In standby the app holds the strip black. Set WLED's boot preset to off, and check that the app started. |

For anything else, set `probeEndpoint`, run `tools/stats-receiver.py`, and look at `latest.json`. It
shows the capture status, frame rate, power state and the last log lines
([diagnostics](docs/development.md#diagnostics-telemetry-and-frame-dumps)).

## FAQ

**Why is there no signed `.tpk` to download?**
Samsung only installs packages signed with a certificate that lists *your* TV's unique ID (DUID), so a
TPK signed for one TV installs on no other. Apps2Samsung solves this by signing on your machine for
your TV. Releases therefore attach only a default-certificate build (rejected by retail TVs), which is
input for re-signing, not something to install directly.

**Does it work with HDMI inputs and consoles?**
Untested so far. The video plane carries what the TV decodes for display, so it is plausible, but it
has only been verified with built-in apps. Reports welcome.

**Does it work on Tizen 5 / 2019–2020 TVs?**
Not as built. Those TVs ship an older .NET runtime and expose different capture libraries. See
[Compatibility](#compatibility)

**Does it need the internet or any account?**
Not during normal operation. It talks to your WLED controller and, if you configure them, two optional
helpers on your LAN. Nothing is sent anywhere by default. Obtaining the signing certificate requires
internet access and a Samsung account; the saved profile can be reused for later builds.

**How is this different from HyperTizen or TizenGlow?**
Those sample a few points through a Samsung library and support many more TVs. This app can read the
whole decoded frame on the S84F, or use hardware spot sampling on the S95B and during DRM fallback.
Both paths talk directly to WLED. There is a
[comparison](docs/how-it-works.md#how-this-differs-from-other-tizen-ambilights).

## How it works

```
video plane (960x540, in memory) -> perimeter zones -> HDR decode + gamut map
  -> smoothing + scene-adaptive tone curve -> one WLED DRGB datagram per frame

S95B: hardware RGB spots -> perimeter interpolation (no full frame or PQ decode)
  -> shared smoothing + tone curve -> WLED DRGB output
```

The capture ABI was recovered by static analysis of the TV's own libraries and anchored to return values
measured on the device. [docs/how-it-works.md](docs/how-it-works.md) has the full story: why the video
plane rather than a screenshot, the struct layouts, the colour pipeline, and the safety mechanisms
(standby detection, stale-frame fade, blackout hold).

## Contributing

Issues and merge/pull requests are welcome; see [CONTRIBUTING.md](CONTRIBUTING.md). Development is on
[GitLab](https://gitlab.com/ritwick7/TizenAmbilight), mirrored to
[GitHub](https://github.com/RitwickVerma/TizenAmbilight).

### Device reports

The most useful contribution right now is a report from another TV: model, year, firmware, and what
happened. Open an issue with the **Device report** template. Even "it did nothing" helps.

## Security

Everything here assumes a trusted home network: the WLED protocol, the control channel and the
telemetry endpoint are unauthenticated by design. Read [SECURITY.md](SECURITY.md) before exposing
anything, and use it to report vulnerabilities.

## Credits

The ambilight idea, the DRGB protocol and much hard-won practical detail come from
[Hyperion](https://github.com/hyperion-project/hyperion.ng), [HyperHDR](https://github.com/awawa-dev/HyperHDR)
and [WLED](https://github.com/wled/WLED). The Tizen-side groundwork comes from
[HyperTizen](https://github.com/reisxd/HyperTizen) and [TizenGlow](https://github.com/DuckieOnQuacks/TizenGlow).
[Apps2Samsung](https://github.com/Apps2Samsung/Apps2Samsung) makes sideloading practical.

Built AI-assisted: the code was written with Claude, and the design and every on-device verification
are the author's.

## License

[MIT](LICENSE). Not affiliated with or endorsed by Samsung.
