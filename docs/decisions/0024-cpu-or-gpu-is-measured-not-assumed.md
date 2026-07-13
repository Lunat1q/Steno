# ADR 0024 — CPU or GPU is measured, not assumed

**Status:** accepted · **Date:** 2026-07-13 · **Amends:** [ADR 0010](0010-sub-second-latency.md)

## Problem

[ADR 0010](0010-sub-second-latency.md) settled that sub-second transcription is a GPU feature, and
the app has behaved ever since as if GPU were simply the right answer: load the Vulkan library,
take whatever it gives you, and put a warning banner up if it fell back to CPU.

That is true on a desktop with a discrete card. It is not a law. A low-end laptop's integrated GPU
shares system memory with the display, has a fraction of the bandwidth, and can be **slower than
the CPU sitting next to it** while also making the screen stutter for the length of the call. On
that machine the app was choosing the GPU, calling it the fast path, and being wrong — with no way
for the user to say otherwise short of not having a GPU.

The spec sheet does not distinguish the two machines. "Intel Iris Xe" tells you nothing about which
side of that line it falls on.

## Decision

**Make it a setting, and measure it rather than guess.**

Three choices under *What does the work* — Automatic, Graphics card, Processor — and a **Test
speed** button next to them that transcribes the same five-second utterance on both processors,
with the model the user actually picked, and switches to whichever won.

Two things made this cheap:

- **CPU vs GPU is not a native-library choice.** The Vulkan build of whisper.cpp contains the CPU
  kernels too; the processor is picked per model load, via `WhisperFactoryOptions.UseGpu`. So the
  setting takes effect on the next call, and the benchmark can measure *both* in one process — no
  restart, no second process, no library juggling. (A native library can only be loaded once per
  process, so had this not been true, the benchmark would have needed to fork itself.)
- **whisper's cost is dominated by the encoder**, which runs over a padded 30-second window
  whatever you feed it. Synthetic speech-shaped audio therefore times almost like real speech, and
  the benchmark ships no WAV asset.

`Graphics card` means it: if no GPU backend can be loaded, the session fails to start with a
sentence telling the user to pick Automatic or Processor. Silently running 60× slower than the
thing the user explicitly asked for is not a fallback, it is a different app.

## Measurements

Same model (large-v3-turbo), same 5 s utterance, one warm-up run discarded:

| Machine | CPU | GPU (Vulkan) | Winner |
|---|---|---|---|
| Desktop — Ryzen 9 9950X3D, RTX 5090 | 10.8 s | **0.19 s** | GPU, by 57× |

The desktop number reproduces ADR 0010's ~11 s / ~170 ms to within noise, which is the point: the
benchmark is not a different measurement from the one the app's latency depends on, it is the same
one, run on the user's machine instead of on ours.

The weak-laptop row is deliberately empty. We do not have that number, and that is the entire
argument for this ADR: the machine where the answer is in doubt is the machine we cannot measure
from here, so the app has to be able to measure it there.

## The model, too — extrapolated, not measured

CPU-or-GPU is not the choice the user is most able to get wrong; **which model** is. So the same
button also proposes one. Benchmarking all three would mean up to 5 GB of downloads and six timed
passes on the slowest machine we have, so instead the measured model is extrapolated to the others
from a fixed cost table — and the table has to be **per backend**, because one table would be
wrong. Measured here (9950X3D + RTX 5090, 5 s utterance, relative to large-v3-turbo):

| | CPU | ×turbo | GPU (Vulkan) | ×turbo |
|---|---|---|---|---|
| Fast (small) | 2 127 ms | 0.20 | 121 ms | 0.65 |
| Balanced (large-v3-turbo) | 10 705 ms | 1.00 | 186 ms | 1.00 |
| Best (large-v3) | 12 117 ms | 1.13 | 396 ms | 2.13 |

The two columns are not the same shape and no single ratio could serve both. whisper's encoder
dominates on a CPU, and turbo and large-v3 *share an encoder* — so large-v3's 32 decoder layers in
place of turbo's 4 cost a CPU only 13%, while on a GPU, where the encoder is nearly free, they more
than double the bill. It inverts going down: dropping to small saves a CPU 80% and a GPU only 35%,
because at that size the GPU is mostly idling on fixed overheads.

The advice is then: **the largest model whose estimate stays under 1.5 s a sentence** — above that,
the pipeline is still transcribing the last sentence while the next is being spoken, and the
transcript drifts further behind the longer the call runs. When nothing fits, the app says so
outright and points at recording ([ADR 0020](0020-recording-and-offline.md)) instead of
recommending a model it knows cannot keep up. On the desktop CPU above, *nothing fits*: even Fast
is 2.1 s.

The backend is applied automatically — it was measured. The model is only proposed: switching it
could trigger a 3 GB download nobody asked for, and accuracy-vs-speed is the user's taste, not ours.

## Consequences

- The warning banner ("this is CPU, it will not keep up") stays, and stays correct — but it is now
  a consequence of a choice the user can see and change, not of a fallback they never knew happened.
- The benchmark loads the model twice, sequentially, and unloads between. Two copies of 1.5 GB at
  once is exactly the wrong thing to do to the 8 GB laptop this feature exists for.
- It costs a minute or two on a slow machine. That is the machine that needs the answer.
- Changing the setting reloads the model on the next Start, the same as changing quality does.

## Alternatives rejected

**Guess from the GPU name.** A denylist of weak integrated GPUs is a maintenance liability that is
wrong the day a new one ships, and it still cannot see how much of the GPU is already spoken for by
the four browser windows behind the call.

**Ship a real 5 s WAV and time that.** Honest about the decoder as well as the encoder, at the cost
of an asset in the installer. Worth doing only if the benchmark's verdict ever disagrees with the
app's real latency; there is a `ponytail:` note on `SampleUtterance` saying so.
