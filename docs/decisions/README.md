# Decisions

One file per decision, newest last. If you change one of these, amend the ADR — the
code alone never explains *why not the other thing*.

| # | Decision | One-line why |
|---|---|---|
| [0001](0001-whisper-cpp-binding.md) | whisper.cpp via the Whisper.net binding | Whisper.net *is* whisper.cpp + the P/Invoke we would hand-write |
| [0002](0002-per-channel-speaker-attribution.md) | The channel is the speaker label | Separate mic/loopback streams make 2-party attribution exact and free |
| [0003](0003-segmentation-and-latency.md) | VAD-cut utterances, not streaming | whisper.cpp has no streaming decoder; fixed chunks cut words in half |
| [0004](0004-audio-capture-wasapi.md) | WASAPI mic + render loopback (NAudio) | Captures any call app with no driver and no virtual cable |
| [0005](0005-translation.md) | Translation = a second whisper pass, English only | whisper can only translate *to* English; the Russian transcript is the primary artifact |
| [0006](0006-echo-and-crosstalk.md) | Assume a headset; heuristic echo gate, AEC deferred | Loudspeaker echo is the one bug that makes speaker labels lie |
| [0007](0007-project-layout.md) | Two projects, layers as folders | The layer boundary is real; five csproj files to enforce it are not |
| [0008](0008-ux-and-visual-language.md) | Two screens, two speaker colours, no engine jargon | The user is about to take a call, not configure an ASR pipeline |
| [0009](0009-com-apartments-and-model-download.md) | WASAPI stays in the MTA; we download the model ourselves | IMMDevice cannot cross COM apartments, and a 1.5 GB download needs a real progress bar |
| [0010](0010-sub-second-latency.md) | Vulkan GPU backend + drafts every 400 ms | GPU is worth 65×; drafts remove the "wait for the sentence to end" delay |
| [0011](0011-transcript-lifecycle-and-pause.md) | Stop keeps the transcript and offers a save; Pause blocks audio before the engine | The transcript is the product — it must not vanish; and a pause that still transcribes is a privacy bug |
| [0012](0012-app-icon.md) | `icon.svg` is the source; the generated `.ico` is committed | Nothing in the .NET SDK rasterises SVG — generating at build time would cost a dependency forever |
| [0013](0013-confidence-shading.md) | Per-word confidence shown as luminance, not hue | whisper.cpp's red→green ramp would collide with "colour = speaker", which the product depends on |
| [0014](0014-cold-start-and-capture-latency.md) | Warm the GPU up before the call; trim the capture buffer | The remaining lag was Vulkan's first-inference cost being swallowed by back-pressure — 4.9 s → 0.7 s |
| [0015](0015-vad-deafness.md) | The noise floor may only learn from quiet frames, and is hard-capped | Fricatives were teaching the VAD that speech was noise; it went permanently deaf after ~20 s |
| [0016](0016-remembering-choices.md) | Remember the last setup in %LOCALAPPDATA%/Steno/settings.json | The app asked the same questions every launch and ignored the same answers |
| [0017](0017-packaging.md) | MSI installer (WiX, per-user, TiQ Studio); no true single exe | whisper.cpp's natives must live in a runtimes/ folder — bundling them yields an exe that dies on Start |
| [0018](0018-updates.md) | Update from GitHub Releases; verify SHA-256 before running the MSI | The releases API is already the source of truth, and the payload is executable |
| [0019](0019-hallucinated-subtitles.md) | Energy gate + content blocklist for whisper's invented subtitles | On pure silence whisper says "Продолжение следует..." at 0.000 no-speech and 0.85 confidence — no threshold can catch it |
| [0020](0020-recording-and-offline.md) | Record as one stereo WAV (left=you, right=them); offline transcription reuses the live rules | The channel IS the speaker, so a recording re-transcribes with attribution intact and needs no diarization |
| [0021](0021-no-blocking-the-ui-thread.md) | Load the model inside Task.Run | "async" is not "off this thread": with the model cached every await completed synchronously, so 1.2 s of native loading ran on the UI thread |
| [0022](0022-vad-over-continuous-audio.md) | The noise floor is a percentile of recent audio, not an EMA of frames judged quiet | Over film audio no frame was ever quiet, so the floor never adapted, no pause was ever silent, and the transcript was 20 s force-cut bricks |
| [0023](0023-capture-level-is-not-speech-level.md) | The pre-whisper gate drops to digital silence; utterances are gain-normalised for whisper | Loopback captures *after* the volume slider — real dialogue arrived at 0.0012–0.008 RMS and a gate set at speech level (0.004) deleted everything but the shouting |
| [0024](0024-cpu-or-gpu-is-measured-not-assumed.md) | CPU or GPU is a user setting with a benchmark button, not an assumption | A discrete GPU wins by 57×, but a weak integrated one can lose to its own CPU — and no spec sheet tells the two apart |
| [0025](0025-dictate-mode.md) | Dictation is a separate exe (Steno.Dictate) that types finals into the focused window via SendInput | Reuses Steno.Core whole; a monologue into any app shares no UI with a two-party call, and keystrokes need no per-app integration |
