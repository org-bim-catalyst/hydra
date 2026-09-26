# Third-Party Notices

Components whose licence carries obligations beyond an ordinary NuGet/npm dependency. Keep this
file in step with the code that uses them.

---

## Supertonic 3 — Lucy's on-server voice (specs/070)

Supertonic is used in two parts, under two different licences.

### Code — MIT

`src/AskLucy.Infrastructure/Ai/Supertonic/SupertonicText.cs` and `SupertonicModel.cs` are ported
from Supertone's C# reference implementation, <https://github.com/supertone-inc/supertonic>.
Our fork is <https://github.com/bimcatalyst/supertonic>.

```
MIT License

Copyright (c) 2026 Supertone Inc.

Permission is hereby granted, free of charge, to any person obtaining a copy of this software
and associated documentation files (the "Software"), to deal in the Software without
restriction, including without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING
BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

### Model weights — OpenRAIL-M

The ONNX model and voice styles (`Supertone/supertonic-3` on Hugging Face, installed by
`scripts/download-supertonic.ps1` at a pinned revision) are **not** MIT. They are licensed under
Supertone's OpenRAIL-M licence, published with the model at
<https://huggingface.co/Supertone/supertonic-3>.

OpenRAIL-M allows commercial use and redistribution. It also attaches **use-based restrictions**
(paragraph 5 and Attachment A, items (a)–(m)) that anyone serving the model must pass on to its
users. The licence defines "Distribution" to include making the model available as a hosted
service, so serving Lucy's voice from our server counts. Upstream development having stopped
does not change this: the licence travels with the weights we host.

**What the licence requires of us, and where it is met:**

| Licence clause | Requirement | Where it's met |
| --- | --- | --- |
| §4.a, para 5 | Bind every user to the Attachment A restrictions as an enforceable provision | `/terms` (`features/terms/pages/TermsPage.tsx`) §4 reproduces (a)–(m) and applies them to all of Ask Lucy; public route, linked from the app footer, landing footer and account menu |
| §4.a | Tell users the model is subject to the restrictions | `/terms` §4 and §7 |
| §4.b | Give recipients a copy of the licence | `/terms` §7 links the model card, which carries the licence |
| Attachment A (e) | Disclose that generated content is machine-generated | `AI_VOICE_DISCLOSURE` (`features/chat/voice/aiVoiceDisclosure.ts`), shown in the Continuous voice panel and under Settings → Voice; `/terms` §3 requires users to label shared audio as AI-generated |
| §4.c, §4.d | Mark modified files; keep notices | We host the weights unmodified; this file keeps the notices |

**Before go-live:** a lawyer should review `/terms`. Its governing law, the legal entity name and
a contact address still need to be filled in.

---

## GroovyMp3 — MP3 encoding (specs/070)

`Mp3StreamEncoder` encodes Supertonic's PCM to MP3 with the `GroovyMp3` NuGet package
(<https://github.com/jongoochgithub/GroovyCodecs>). It is based on LAME and Jump3r and released
under the **GNU Lesser General Public License v3.0**.

We use the package unmodified, as a separately replaceable assembly (`GroovyMp3.dll`), which is
what the LGPL requires of a combined work. If that assembly is ever modified, merged into ours,
or statically linked, the LGPL's source-availability obligations apply to the modified library.

---

## Map and building data — site and solar analysis (specs/053, 075, 076)

Credited in section 8 of `/terms`. Deliberately not credited on screen, a decision taken
2026-09-26 while Ask Lucy is non-commercial. Revisit it before going commercial.

| Data | Used for | Licence / terms | Credit |
|---|---|---|---|
| Google Maps Static API | Basemap imagery; building outlines traced from the styled map | Google Maps Platform Terms | © Google |
| OpenStreetMap, via Overpass | Building outlines, storey counts, height tags | ODbL 1.0 | © OpenStreetMap contributors |
| Overture Maps buildings theme (public PMTiles) | Building outlines (OSM plus Microsoft and other machine-detected footprints) | ODbL 1.0 | Overture Maps Foundation; © OpenStreetMap contributors |
| Esri 3D buildings I3S scene layer | Measured roof heights | Esri terms of use; the account holder has reviewed them for non-commercial use | Source: Esri, Vantor |

ODbL is share-alike for *databases*. We only serve rendered results (shadows and outlines drawn
for one site), which the ODbL calls a Produced Work. That needs the credit above, not a
database release. If we ever export a merged building database, the share-alike terms apply to
it.
