# _CE Shaders — SWSL Rules

Shaders here are written in SWSL (Space Wizard Shader Language), RobustToolbox's GLSL-based
shader format (`.swsl`). Source: https://docs.spacestation14.com/en/robust-toolbox/rendering/shaders.html
(the official doc is thin — most of the rules below come from reading the engine source directly,
`RobustToolbox/Robust.Client/Graphics/Shaders/` and `RobustToolbox/Robust.Client/Graphics/Clyde/Clyde.Shaders.cs`).

## Precision qualifiers are mandatory

Every numeric type (`float`, `vec2`, `vec3`, `int`, ...) must carry `highp`, `mediump`, or `lowp`.
Untyped declarations can silently compile wrong (or not at all) on GLES targets. This codebase
uses `highp` almost everywhere and `lowp` for loop counters / small enums.

## Avoid GLSL ES 3.0 reserved keywords

Don't name variables/functions after GLSL ES 3.0 reserved words (section 3.8 of the spec).
Desktop drivers often tolerate it; other drivers won't. Check before naming something generic
like `sample`, `texture`, `input`, `output`, `filter`, etc.

## `#include`

`#include "/path/to/file.swsl"` is resolved by SWSL's own tokenizer — the included file's tokens
are spliced in at that point *before* anything reaches the real GLSL compiler. It is not a
conditional or deduplicated include: including the same file twice (directly or via two other
includes) will redeclare everything in it and fail to compile.

## `#ifdef` / `#ifndef` / `#else` / `#endif`

These are **not** interpreted by SWSL — they're passed through verbatim to the real GLSL
preprocessor at final compile time. The engine defines these feature flags in the version header
before your code runs (see `Clyde.Shaders.cs`, `_compileProgram`):

- `HAS_DFDX` — screen-space derivatives (`dFdx`, `dFdy`, `fwidth`) are available.
- `HAS_FLOAT_TEXTURES`, `HAS_SRGB`, `HAS_MOD`, `HAS_VARYING_ATTRIBUTE`, `NO_ARRAY_PRECISION` — other
  GLES2/GLES3/desktop capability gates.

**Any use of `dFdx`/`dFdy`/`fwidth` must be guarded with `#ifdef HAS_DFDX` and have a constant
fallback in `#else`.** GLES2 (and `--cvar display.compat=true`) targets don't have derivatives.
The only place in this codebase this pattern is confirmed working end-to-end is directly inside
`fragment()` (see `Resources/Textures/Shaders/cooldown.swsl`). Prefer keeping derivative calls
directly in `fragment()`/`vertex()`/`light()` rather than inside a nested helper function — if a
helper genuinely needs a derivative, test it explicitly (see Testing below) before trusting it.
If a derivative-based effect is only cosmetic (e.g. debug/dev overlays), it's simplest and safest
to skip `fwidth` entirely and hardcode the fallback width — one less thing that can break per-driver.

## Never rely on implicit int-to-float conversion

Desktop GLSL (`#version 140`, used when not on GLES/compat) implicitly converts `int` literals to
`float` in arithmetic and initializers, so `highp float x = 1;` or `vec3 * 10` compile fine there.
GLSL ES (the `#version 100`/`300 es` path this engine uses for GLES/`--cvar display.compat=true`)
does **not** allow this — it's a compile error, not a warning. Always write float literals as
float literals: `1.0`, `10.0`, `0.5`, never bare `1`, `10`, `0` in a `float`/`vecN` context. This
is an easy mistake to introduce (it compiles and runs fine on your desktop dev GPU) and only shows
up when compat mode or a GLES device actually compiles the shader — test both (see Testing below)
before assuming a new shader edit is done.

Also don't call `smoothstep(edge0, edge1, x)` with `edge0 == edge1` — the GLSL spec leaves the
result undefined in that case (some drivers return NaN). Use two genuinely distinct edges.

## Uniform array budgets

GLES2 only *guarantees* 16 `vec4` (64 scalar components) of fragment uniforms; desktop GL
guarantees 1024 components minimum. Real hardware usually has much more headroom, but every
`uniform T[64]` array eats into that budget, and multiple parallel arrays (e.g. `positions`,
`radii`, `strengths`) add up fast:

- `murk.swsl`: one 64-slot layer × (vec2 + float + float + float) ≈ 320 components.
- `murkdebug.swsl`: two 64-slot layers (free + wall) × (vec2 + float + float) ≈ 512 components.

Keep array sizes as small as the feature actually needs, and don't duplicate a full 64-slot set
per visualization pass if you don't have to — measure against the smallest target (GLES2/compat),
not just your desktop dev GPU.

## Testing (required, not optional)

Shader syntax/type errors only surface at runtime when the driver compiles the GLSL — there is no
C#-side static check. Before considering a shader change done:

1. Launch normally and trigger the effect.
2. Relaunch with `--cvar display.compat=true` (forces the GLES2-style compat path) and trigger it
   again.
3. Use `/rldshader` to hot-reload `.swsl` changes without restarting the client while iterating.

## Misc

- `light_mode unshaded;`, `blend_mode <mix|add|subtract|multiply|none|normal>;`, and
  `preset <default|raw>;` are shader-level pragmas, not statements — they go before any
  uniform/varying/function declarations.
- Built-in fragment inputs/outputs: `FRAGCOORD` (highp vec4), `COLOR` (lowp vec4, the output),
  `lightMap` (sampler2D), `modulate` (highp vec4), `SCREEN_PIXEL_SIZE` (highp vec2), `TIME`
  (highp float).
- The `// CrystallEdge: reason` upstream-edit-comment convention from the root `CLAUDE.md` is a C#
  convention and does not apply to `.swsl` files. Still, comment anything non-obvious (e.g. why a
  derivative call is guarded, why an array is sized the way it is) since shader bugs are expensive
  to diagnose (driver-side errors, no stack trace, no repro without a GPU).
