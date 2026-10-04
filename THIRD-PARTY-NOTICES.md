# Third-party notices

This file is an index of notices for the packaged AuraSync application. It is not a license for AuraSync itself. The repository does not identify a license for project-authored code; this file does not grant permission to use, modify, or redistribute that code.

The Windows package includes these license and notice materials:

- Angular renderer dependencies: `resources/browser/THIRD-PARTY-LICENSES.txt` (generated from the production Angular build).
- Electron: `resources/LICENSE`.
- Chromium and its bundled components: `resources/LICENSES.chromium.html`.
- .NET 10 runtime: Microsoft's software license terms at `resources/service/DOTNET-LICENSE.txt`. The terms include conditions for distributing the runtime's redistributable code; review them before distribution.
- NAudio 2.2.1: `resources/service/NAudio-LICENSE.txt`.
- System.IO.Ports 10.0.0 and its package notices: `resources/service/System.IO.Ports-THIRD-PARTY-NOTICES.TXT`.

The npm lockfile records versions and license identifiers for third-party packages that publish license metadata. The two private AuraSync workspace packages have no declared project license. Build-only dependency licenses are recorded in the lockfile; the generated renderer license bundle covers dependencies extracted into the production UI bundle.

The splash screen requests Orbitron from Google Fonts when a network connection is available. Orbitron is identified by the [upstream Google Fonts license](https://github.com/google/fonts/blob/main/ofl/orbitron/OFL.txt) as Copyright 2018 The Orbitron Project Authors and licensed under SIL Open Font License 1.1, with "Orbitron" as a reserved font name. The font files are not bundled by this repository. The request is handled by Google and may be subject to Google's applicable terms and privacy practices.

The user-guide firmware example requires users to install [FastLED](https://github.com/FastLED/FastLED) separately. FastLED is MIT-licensed (Copyright 2013 FastLED); the library is not bundled with AuraSync. Users who redistribute FastLED must include its copyright and permission notice.

## Historical firmware-source note

Earlier repository revisions included an Arduino sample whose header and parser structure match a Wifsimster-attributed Adalight WS2812 sketch in [hiuri/FlexiLights](https://github.com/hiuri/FlexiLights). No license was found in that upstream repository during this review. The current user-guide sample has been independently rewritten, but that does not resolve the status of historical Git objects or establish permission for prior versions. Verify permission or obtain qualified advice before distributing repository history.
