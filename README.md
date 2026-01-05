# Horizon Weather & Time ☁️

**A modular environment simulation system for Unity & VRChat.**

A comprehensive solution that manages time of day, lighting, and volumetric atmospherics. It is designed to be data-driven, allowing for easy state switching via configuration profiles.

---

## Key Features

*   **Time Simulation**
    A complete 24-hour cycle logic with accurate celestial positioning for the Sun and Moon. Includes a calculated lunar phase cycle.

*   **Atmospherics**
    Features a raymarched volumetric cloud system and procedural skybox that dynamically adapt to light direction, density settings, and wind.

*   **Profile System**
    Weather states (Clear, Storm, Heavy Snow) are stored as **ScriptableObjects**. This makes switching environments instant and allows for easy preservation of settings.

*   **Network Sync**
    Built-in `HorizonTimeDriver` handles Udon synchronization across the instance, while supporting local overrides for client-side adjustments.

*   **Environment Integration**
    Automatically controls scene lighting, reflection probes, and particle emission rates based on the active profile.

---

## Setup Guide

1.  **System Setup:** Create a GameObject with the `WeatherTimeSystem` component.
2.  **Configuration:** Create `Weather Profile` assets in your project folder.
3.  **Assignment:** Add your profiles to the "Weather Profiles List" in the Inspector.
4.  **Run:** The system handles initialization and baking automatically at runtime.

---

## Integration

Designed to work with **Horizon GUI** for immediate in-game debugging and control, providing ready-to-use UI elements for time and weather manipulation.
