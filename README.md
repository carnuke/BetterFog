# BetterFog

This is a source/reference repository for BetterFog, a mod for REPO. The full local build and deployment configuration is intentionally excluded, including game-install paths, Thunderstore profile paths, generated output, and game assemblies.

REPO's built-in fog uses forward camera depth. As a result, surfaces at the same real distance can receive different fog amounts depending on their position and viewing angle, which can make the fog feel box-shaped. BetterFog replaces this with radial fog so visible surfaces at a consistent distance from the camera receive consistent fog.

## Source Layout

- `src/Plugin.cs` is the BepInEx plugin. It patches REPO's `EnvironmentDirector.FogLogic`, manages the fog lifecycle, and adds a full-screen command-buffer effect to the gameplay camera.
- `unity/Assets/Shaders/CylindricalFog.shader` is the custom Unity shader. It reconstructs the visible surface position from Unity's depth texture and calculates fog from that surface's radial camera-space distance.
- `unity/Assets/Editor/BuildBundles.cs` assigns the shader to an AssetBundle named `betterfog` and builds that bundle for 64-bit Windows.

## Requirements

Building the plugin requires a .NET SDK, BepInEx 5, HarmonyX, and references to REPO's managed assemblies, including `Assembly-CSharp.dll` and the relevant UnityEngine assemblies. These dependencies are intentionally not included.

Building the shader AssetBundle requires a Unity 2022.3 editor with support for the Built-in Render Pipeline and the Standalone Windows 64-bit target.

## Building the AssetBundle

The plugin loads an AssetBundle named `betterfog` from its own plugin folder. In Unity, run **Build > Build BetterFog Bundle** to build it.

`BuildBundles.cs` retains the original local-project assumption: the Unity project directory is named `BetterFogShaders` and sits beside the C# project directory named `BetterFog`. Adjust its output path if you use another layout. The generated `betterfog` bundle is required alongside `BetterFog.dll` at runtime.

## Configuration

After BetterFog starts once, BepInEx writes `carnuke.betterfog.cfg` in its configuration directory.

- `Enable Better Fog`: Enables BetterFog's radial fog. Default: `true`.
- `Start Distance`: Distance in world units where fog begins. Default: `8`.
- `End Distance`: Distance in world units where fog reaches full strength. Default: `16`.

One REPO level module is 15 world units long.

## Installation

Use the published Thunderstore package to install BetterFog. This repository is intended for inspecting the implementation and is not a complete ready-to-build project.

