# UBLauncher — Workspace Architecture & Concept Memory

This file serves as the single source of truth for the architecture, technology stack, design patterns, and cross-runtime interactions of the **UBLauncher** ecosystem. It is designed to give any developer or AI assistant full architectural context.

---

## 📐 Ecosystem Architecture

UBLauncher is built as a hybrid desktop client and game optimizer, splitting concerns between a modern .NET desktop shell and JVM modding components:

```
[ Launcher.UI (Avalonia MVVM) ]
          │
          ▼
[ Launcher.Core (State, VM) ] ───► [ Launcher.Database (LiteDB) ]
          │
          ├─► [ Launcher.Services (RPC, Forking) ] ──► [ Minecraft.Runtime (JVM Args, Natives) ]
          ├─► [ Launcher.Downloader (Multi-thread) ]
          ├─► [ Launcher.Security (DPAPI, Integrity) ]
          ├─► [ Launcher.Updater (Delta Patches) ]
          └─► [ Minecraft.ModManager (CurseForge/Modrinth) ]

[ JVM / Minecraft Runtime ]
          │
          ├─► [ Minecraft.Performance (Fabric/Mixins) ]
          └─► [ Minecraft.HUD (OpenGL Widgets) ]
```

### 1. Project Directory & Responsibility Breakdown

#### C# / .NET 10 Ecosystem (Solution: `UBLauncher.slnx`)
- **[Launcher.UI](file:///f:/UBLauncher/UB_Louncer/Launcher.UI)**: Desktop frontend using Avalonia UI and CommunityToolkit.Mvvm. Focuses on premium, responsive, glassmorphic styling, custom control templates, and hardware-accelerated transitions.
- **[Launcher.Core](file:///f:/UBLauncher/UB_Louncer/Launcher.Core)**: ViewModels, state management, screen navigation models, and abstract services interfaces. Contains the orchestration pipeline.
- **[Launcher.Services](file:///f:/UBLauncher/UB_Louncer/Launcher.Services)**: Background worker orchestrators, Discord Rich Presence (RPC) updates, and JVM process spawning logic.
- **[Launcher.Downloader](file:///f:/UBLauncher/UB_Louncer/Launcher.Downloader)**: High-speed, multi-threaded parallel download engine. Uses chunk-based HTTP requests and verifies integrity of Minecraft assets using SHA-1 hashing.
- **[Launcher.Security](file:///f:/UBLauncher/UB_Louncer/Launcher.Security)**: Platform-specific secure token encryption (Windows DPAPI, planning for Keychain/Keyring), executable integrity scans, and HWID (Hardware ID) computation for telemetry/anti-cheat.
- **[Launcher.Database](file:///f:/UBLauncher/UB_Louncer/Launcher.Database)**: Thread-safe persistent document storage using LiteDB to store launcher configs, profiles, cached mod metadata, and user accounts.
- **[Launcher.Updater](file:///f:/UBLauncher/UB_Louncer/Launcher.Updater)**: Background check pipeline comparing local builds against remote APIs. Performs delta-patch installations to save bandwidth.
- **[Minecraft.Runtime](file:///f:/UBLauncher/UB_Louncer/Minecraft.Runtime)**: JVM argument builder (GC tuning, memory constraints, Classpath generator) and extractor for platform-specific native libraries (`.dll`, `.so`, `.dylib`).
- **[Minecraft.ModManager](file:///f:/UBLauncher/UB_Louncer/Minecraft.ModManager)**: Unified client for CurseForge and Modrinth REST APIs, handling dependency trees, mod versions, and client-side installs.

#### Java 17+ Ecosystem (Build System: Gradle)
- **[Minecraft.Performance](file:///f:/UBLauncher/UB_Louncer/Minecraft.Performance)**: Optimization mod running inside the JVM client. Utilizes SpongePowered Mixins to patch vanilla rendering pipelines, reduce memory footprint, and optimize entity culling.
- **[Minecraft.HUD](file:///f:/UBLauncher/UB_Louncer/Minecraft.HUD)**: Custom in-game HUD widget framework. Leverages OpenGL rendering hooks to draw frames, FPS counters, keystrokes, and customizable draggable components directly onto the screen overlay.

---

## 🛠️ Tech Stack & Dependencies

| Layer | Technology / Package | Target Runtime | Purpose |
| :--- | :--- | :--- | :--- |
| **UI** | Avalonia UI | .NET 10 | Cross-platform UI framework with WPF/UWP XAML paradigm |
| **MVVM** | CommunityToolkit.Mvvm | .NET 10 | Code-generated ViewModels, messaging, and commands |
| **Database** | LiteDB | .NET 10 | Serverless, thread-safe, single-file document database |
| **Modding** | Fabric API & Loader | Java 17+ | Core game hook framework for performance and overlay injection |
| **Injectors** | SpongePowered Mixins | Java 17+ | Bytecode level rendering adjustments inside the JVM |

---

## ⚡ Core Engineering Conventions

1. **Strict Asynchrony (`async`/`await` / `CompletableFuture`)**
   - No UI-blocking calls. Any download, file system hashing, network lookup, or DB query *must* be run using task-based asynchronous patterns.
2. **Defensive Error Management**
   - High-fidelity logging via structured Serilog conventions.
   - All external processes (JVM execution, HTTP fetching) must wrap steps in strict try-catch handlers that capture complete context logs instead of generic dumps.
3. **No Placeholders**
   - All source code written must be complete and operational. Avoid standard `// TODO` shortcuts.
