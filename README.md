Itexoft Toolkit
================

This repository contains the shared Itexoft toolkit for console‑first workflows. It focuses on terminal runtime infrastructure, binding and validation helpers, IO/networking utilities, and reusable presets that keep keyboard‑driven experiences consistent across projects. The code is organized as composable slices so applications can opt into only what they need while keeping rendering and hosting predictable.

Principles
----------
- Terminal‑centric by design: layout, navigation, and input flows assume a keyboard‑driven console environment.
- Immutable descriptions, predictable runtime: snapshots are built up front, while render and dispatch layers stay minimal.
- Batteries included, selectively: higher‑level DSL and presets sit atop low‑level primitives instead of forcing a single stack.
- Operational safety: shared helpers centralize edge cases (console sizing, pooling, validation) to avoid one‑off fixes.

Packaging and licensing
-----------------------
- Licensed under the Mozilla Public License 2.0 with the Exhibit B notice (Incompatible With Secondary Licenses).
- NuGet packages ship with bundled docs and notices (LICENSE, NOTICE, README, DONATE) to keep distribution self‑contained.
- Targets modern .NET with nullable annotations enabled; unsafe blocks are allowed where performance or interop require it.

Contributing and support
------------------------
- Issues and discussions are welcome for bugs, integration questions, and design feedback.
- Commercial terms are available alongside MPL‑2.0 if your organization needs alternative licensing; see DONATE.md for contact.
