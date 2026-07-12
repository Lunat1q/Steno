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
