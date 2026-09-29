# AtlasNet Unity Shooter Demo

Unity project for comparing the shooter sample's AtlasNet client- and server-authoritative players, including cross-server handoff scenes.

Open this repository root in Unity **6000.6.2f1**. Unity Package Manager resolves AtlasNet Unity from the Git revision pinned in `Packages/manifest.json` and `Packages/packages-lock.json`.

Scenes in `Assets/Scenes`:

- `ClientAuth` and `ServerAuth` are the single-worker comparisons.
- `ClientCrossServer` and `ServerCrossServer` exercise local multi-worker interest and handoff. In Multiplayer Play Mode, use the same scene in each instance and start a Host, then a Worker, then a Client.

The imported Low Poly Shooter Pack - Free Sample assets are under `Assets/Infima Games`. They are third-party content; verify their redistribution terms before publishing this repository. Unity-generated `Library`, `Logs`, `Temp`, IDE files, recordings, and local user settings are intentionally not included.

