# Horizon Weather & Time ☁️

**A modular environment and time-of-day system for Unity & VRChat.**

Horizon is a data-driven tool for managing scene lighting, skybox rendering, and weather effects. Instead of configuring lighting directly in the scene, you define states using reusable ScriptableObject profiles.

---

## ⚡ Core Features

- **Modular Architecture:** Weather presets are broken down into sub-modules (`Lighting`, `Sky`, `Cloud`, `Fog`, `Moon`, `Effects`). You can reuse a single "Storm Clouds" profile across multiple weather states without duplicating data.
- **Layer Overrides:** Mix and match active weather elements directly in the Editor. You can swap the active fog or cloud layer independently without affecting the base lighting.
- **Udon-Friendly Data:** The Editor bakes your ScriptableObject profiles into flat arrays. Udon reads primitive data at runtime instead of complex object graphs, reducing overhead.
- **Time Simulation:** A continuous 24-hour cycle that updates Sun and Moon transforms, light intensity curves, day/night ambient colors, and a basic lunar phase.
- **Procedural Atmospherics:** A custom skybox shader handling Rayleigh/Mie scattering, starfield fading, and basic raymarched volumetric clouds.
- **Weather Particles:** Instantiates assigned particle prefabs (like rain or snow) and keeps them positioned above the local player's camera.
- **VRChat Sync:** Includes a basic `HorizonTimeDriver` for Udon network synchronization, with support for local client-side overrides.

---

## 🚀 Quick Start

1. Right-click anywhere in your scene hierarchy and select `GameObject -> Horizon -> Weather & Time System`.
2. Select the newly created **Horizon Weather & Time** object.
3. The system will automatically generate base presets (Clear, Rain, Snow) and assign them.
4. Adjust the **Sun Position** slider in the Inspector to test the day/night cycle.
5. Expand the **Layer Overrides** section to manually mix different cloud or fog modules into your active preset.

---

## 🧠 Core Concepts (Profiles)

Horizon uses a Master-Submodule architecture. You define the specific pieces, and the `WeatherProfile` groups them together.

### The Sub-Modules

- `LightingProfile`: Sun/Moon colors, intensity, and ambient light gradients.
- `SkyProfile`: Atmospheric turbidity, exposure, and the starfield texture.
- `CloudProfile`: Volumetric cloud noise, coverage, density, and wind.
- `FogProfile`: Unity Fog settings seamlessly blended into the skybox horizon.
- `EffectsProfile`: The particle system prefab and its spawn height offset.
- `MoonProfile`: Moon texture, size, and tint.

### The Master Preset (`WeatherProfile`)

A container that holds exactly one reference to each sub-module type. When you switch to a Master Preset, the system updates all active layers to match its references.
