# Umbral's fonts

The three typefaces of the Umbra look (spec 007, research R4). Umbral serves
them itself, embedded in the assembly (`Umbral.fsproj`, `Views/Assets.fs`):
an identity provider's pages load nothing from another host, and an offline
instance still gets its type.

| File | Family | Weights | Version |
| --- | --- | --- | --- |
| `bricolage-grotesque-latin-800-v1001.woff2` | Bricolage Grotesque (96pt ExtraBold, static) | 800 | 1.001 |
| `schibsted-grotesk-latin-400-600-v1100.woff2` | Schibsted Grotesk (variable) | 400–600 | 1.100 |
| `spline-sans-mono-latin-400-500-v1004.woff2` | Spline Sans Mono (variable) | 400–500 | 1.004 |

All three are under the SIL Open Font License 1.1: `OFL-*.txt` here, and in
the image under `/app/licenses/fonts/`.

## Where they come from

The `latin` subset Google Fonts serves for
`https://fonts.googleapis.com/css2?family=Bricolage+Grotesque:wght@800&family=Schibsted+Grotesk:wght@400;500;600&family=Spline+Sans+Mono:wght@400;500`
(fetched with a current browser's User-Agent so it answers with woff2). The
unicode range is Google's `latin` one, repeated in the `@font-face` rules of
`../umbral.input.css`. The two variable files were cut down to the weights
Umbral uses, with fontTools in a throwaway virtual environment:

```python
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

font = instancer.instantiateVariableFont(TTFont("schibsted.woff2"), {"wght": (400, 600)})
font.flavor = "woff2"
font.save("schibsted-grotesk-latin-400-600-v1100.woff2")
```

(and `{"wght": (400, 500)}` for Spline Sans Mono). The licences are
`ofl/<family>/OFL.txt` in `github.com/google/fonts`.

## Changing a file

The stylesheet is compiled ahead of time, so its `url()`s cannot carry a
content hash, and fonts are served as immutable for a year. **A changed font
file gets a new file name** (bump the version in the name), and the
`@font-face` rule, `Umbral.fsproj` and `Views/Assets.fs` follow it. A test
checks that every `url()` in `umbral.css` names a served asset.
