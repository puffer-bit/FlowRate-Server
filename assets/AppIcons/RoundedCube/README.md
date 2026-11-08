# Selected FlowRate Server identity assets

The selected server identity: the original FlowRate FR headphones mark on a simple rounded cube, with accent color `#6C63FF`. The icon has no caption, cables, panels or indicators.

This kit exactly matches the client headphones identity kit's 68-file inventory: 12 SVGs (including the preview), 48 PNGs (including Android launchers and the preview), two ICOs, four Android XML files, this README and the builder. It is saved for future integration; application resources, packaging and the browser panel have not been connected to it. Earlier concepts are not included in this folder.

## Files and uses

| Asset | Use |
| --- | --- |
| `svg/app-icon.svg` | White rounded cube on a rounded purple tile |
| `svg/app-icon-square.svg` | Full square tile for consumers that apply their own mask |
| `svg/mark-purple.svg` | Transparent purple cube |
| `svg/mark-white.svg`, `svg/mark-dark.svg` | Transparent monochrome outlines for dark and light backgrounds |
| `svg/tray-purple.svg`, `svg/tray-white.svg`, `svg/tray-dark.svg` | Compact cube and heavier FR lettering for small sizes |
| `svg/favicon.svg` | Compact white cube on a purple browser tile |
| `svg/wordmark-on-light.svg`, `svg/wordmark-on-dark.svg` | Horizontal logo with outlined FlowRate lettering and SERVER below the name |
| `flowrate.ico` | Windows icon with 16, 24, 32, 48, 64, 128 and 256 px frames |
| `favicon.ico` | Browser favicon with 16, 32 and 48 px frames |
| `png/` | Raster exports matching the SVG artwork |
| `Android/` | Native launcher resources, supplied for parity with the client kit |
| `preview.svg`, `preview.png` | Comparison sheet on light and dark backgrounds |
| `build.py` | Self-contained local builder |

App PNG sizes: 16, 24, 32, 48, 64, 128, 256, 512 and 1024 px. Square app icon: 1024 px. Transparent marks: 64, 128, 256 and 512 px. Tray: 16, 20, 24, 32 and 48 px. Favicon: 16, 32 and 48 px. Horizontal logos: 1700 × 512 px.

Android resources include legacy launcher PNGs for mdpi through xxxhdpi, native adaptive foreground and background, and an inline monochrome vector for themed icons. They are not linked to any application project. The square app SVG is an ordinary square export, not an Android adaptive resource.

All SVGs use explicit colors and self-contained paths and shapes. Lettering is outlined. Every SVG is exported as absolute paths with explicit styles, without nested transforms or inherited colors. They require no installed fonts, linked images, scripts, masks or clipping paths. Preview backgrounds belong to the comparison sheet only.

## Rebuild

From the server repository root:

```sh
/usr/bin/python3 assets/AppIcons/RoundedCube/build.py
```

Requires PyGObject with Rsvg 2.0, Pycairo, Pillow and Noto Sans Bold for outlining the wordmark. The builder contains all artwork and does not depend on client files or temporary experiment folders.

Validation: SVG and Android XML parsing, successful rendering of every SVG with librsvg, PNG decoding, ICO frame sizes, and visual inspection of the preview. Pixel checks confirmed that the complete headphones mark stays inside the front face in both full-size and compact artwork; flattening the SVG paths preserves the rendered geometry. File paths were compared with the client kit and match exactly. Appearance in a running operating-system tray or Android launcher has not been checked.
