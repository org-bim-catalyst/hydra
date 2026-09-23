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
(for example, no impersonating a real person without consent, and no deceiving people about
whether audio is synthetic) that anyone serving the model must pass on to its users.

**Go-live blockers.** Both must be done before Supertonic becomes Lucy's voice in production:

1. **Terms of Service pass-through.** Ask Lucy's Terms of Service must include the licence's
   use restrictions, or reference them in a way that binds end users.
2. **AI-audio disclosure.** Users must be able to tell that Lucy's voice is synthesised.

ElevenLabs, the seeded primary voice, does not depend on either item. Supertonic can therefore
be installed and auditioned from **Admin → Voice** before they are done.

---

## GroovyMp3 — MP3 encoding (specs/070)

`Mp3StreamEncoder` encodes Supertonic's PCM to MP3 with the `GroovyMp3` NuGet package
(<https://github.com/jongoochgithub/GroovyCodecs>). It is based on LAME and Jump3r and released
under the **GNU Lesser General Public License v3.0**.

We use the package unmodified, as a separately replaceable assembly (`GroovyMp3.dll`), which is
what the LGPL requires of a combined work. If that assembly is ever modified, merged into ours,
or statically linked, the LGPL's source-availability obligations apply to the modified library.
