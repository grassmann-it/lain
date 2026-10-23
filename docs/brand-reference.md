# grassmann-it.de visual reference

Reference checked 2026-10-23. The desktop shell keeps the existing compact
Semi/Ursa controls and spacing, and adopts the website’s mint on near-black
color direction and its two display families.

## Evidence

The public homepage at <https://www.grassmann-it.de/> returned HTTP 200 with a
normal browser user agent and identified <https://grassmann-it.de/de/> as its
canonical page. Its deployed stylesheet was:

<https://grassmann-it.de/app/themes/tom-grassmann-it/public/build/assets/app-2ecumw6l.css>

SHA-256: `218babdcb7839c3e7e28369bfaece90592fbf90e0ded30cb5ac457f61b8fe410`.
The deployed CSS directly declares primary `#73d7ae`, black `#050706`,
white `#edf2ef`, gray `#89938e`, Oxanium and IBM Plex Sans Condensed.

The matching local WordPress theme source (`tom-grassmann-it`,
`resources/css/theme.css`, `fonts.css`, `app.css`) additionally supplies the
dark surface values `#08100d`, `#0c1511`, and `#10281f`. These values are present
in source rather than emitted as named variables in the deployed CSS. The
source uses IBM Plex Sans Condensed for h1/h2 and Oxanium for prose/navigation.

## Desktop mapping

| Role | Color | Origin |
| --- | --- | --- |
| Workspace | `#050706` | Website black |
| Surface | `#08100d` | Theme primary-dark |
| Raised/input | `#0c1511` | Theme primary-darker |
| Hover/selected tint | `#10281f` | Theme secondary |
| Text | `#edf2ef` | Website white |
| Secondary text | `#89938e` | Website gray |
| Accent | `#73d7ae` | Website primary |
| Divider | `#30493e` | Desktop adaptation |
| Input boundary | `#50695b` | Desktop adaptation for contrast |
| Accent hover/pressed | `#9be5c6` / `#5ebf98` | Desktop adaptation |
| Success | `#9bcf9c` | Separate semantic green |
| Warning / danger | `#d9af65` / `#ea8087` | Retained amber / rose semantics |

`Ui` brushes cover code-created controls; `Design/Workspace.axaml` covers
Semi control states, selected rows, popups, progress, focus and selection.
Component brush aliases are overridden as well as Semi palette aliases because
Semi resolves some palette aliases statically when loading its dictionaries.

Calculated WCAG contrast ratios: secondary text on the brightest background
is 4.93:1; normal text there is 13.79:1; primary button text is 11.60:1;
input boundaries against input backgrounds are 3.11:1. Success, warning and
danger text on the main surface are 10.82:1, 9.42:1 and 7.28:1 respectively.
Semantic labels and icons continue to convey status alongside color.

## Typography and redistribution

- IBM Plex Sans Condensed Regular/SemiBold: page and section headings.
  These are the original static font files from the matching website source.
- Oxanium Regular/SemiBold: brand mark (`Ui.BrandFont`) and compact navigation
  (`Ui.NavigationFont` and the navigation text style). The website ships a
  variable font; the desktop bundles original static upstream fonts to avoid
  variable-axis differences between Linux font renderers.
- Inter remains the body, table and form family at the existing compact sizes.
  This keeps account identifiers, numeric usage data and editable values legible
  while the brand and navigation carry the website typography.

Both added families permit redistribution under SIL Open Font License 1.1.
Their font metadata declares that license; each family directory contains its
copyright notice and `OFL.txt`, also copied into build/publish output. Assets are
embedded with `AvaloniaResource` and loaded through `avares://` font URIs.
There are no runtime font downloads or new package dependencies.

Upstream licenses:

- <https://raw.githubusercontent.com/google/fonts/main/ofl/ibmplexsanscondensed/OFL.txt>
- <https://raw.githubusercontent.com/google/fonts/main/ofl/oxanium/OFL.txt>

Oxanium static originals:

- <https://github.com/sevmeyer/oxanium/blob/master/fonts/ttf/Oxanium-Regular.ttf>
- <https://github.com/sevmeyer/oxanium/blob/master/fonts/ttf/Oxanium-SemiBold.ttf>

Asset SHA-256 checksums:

```text
b4473d48ef1372d308f75fd86cddaa318ba8202e28f64ae5576c2010bd221934  IBMPlexSansCondensed-Regular.ttf
e87f058254fcc38af9278cefb994f733a20ff550492af9a0a9291cdf4ba7a14a  IBMPlexSansCondensed-SemiBold.ttf
2b9c2a53ee0a8248ab901463838cc81075b410724046a09683b3cc91870fe52a  Oxanium-Regular.ttf
e2d77ec4ee67b0152166adf5d6393360550a012c2066e0d4589053e14a733cdc  Oxanium-SemiBold.ttf
```

Static verification: XML parses, no duplicate resource keys, all bundled font
metadata confirms OFL and static font tables, palette contrast calculations,
and `git diff --check`. Build and Xvfb visual verification belong to the
integrated desktop qualification run.
