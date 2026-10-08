# E²-Day 品牌图标

名称：E²-Day。全称：Explore Everyday Day。寓意：探索每一天。

- `e2-day-logo.png`：高分辨率 RGBA 图标，外侧圆角区域透明；用于应用侧栏。
- `e2-day.ico`：同一原图的 Windows 图标容器，包含 16、24、32、48、64、128、256 像素七个尺寸；用于 exe、窗口、任务栏和桌面快捷方式。

按用户选择使用天空蓝背景，白色 E² 中的一条道路延伸向金色朝阳，表达持续探索和新的一天。

2026-10-07 使用内置 image_gen 生成功能创建 PNG。ICO 仅做格式转换与尺寸缩放，保留原图透明度和图案。

## 初版生成提示词

```text
Use case: logo-brand.
Asset type: final Windows desktop application icon, square high-resolution PNG with transparent outer corners, not a presentation mockup.
Primary request: Design one elegant, distinctive app logo for "E²-Day", full name "Explore Everyday Day", meaning explore every day. The actual icon should show only the large monogram "E²", never the full name or other small words.
Style/medium: exceptionally clean flat vector-like brand mark, refined bold geometric typography, crisp edges, friendly understated productivity app aesthetic.
Composition: a single large forest-green rounded-square tile fills about 92% of the square canvas with equal narrow transparent margins. An ivory/off-white bold capital E is the dominant central shape, large thick readable horizontal bars and vertical stem. A clearly legible ivory numeral 2, as a proper superscript at the E's upper right, makes the exact monogram E². Very subtly integrate a forward-reaching path into the E horizontal strokes; retain unmistakable E letter structure. A small warm golden rising sun disc behind or just beside the superscript adds an optimistic new-day / exploration cue; minimal elements. The E² monogram occupies most of the tile with generous but not excessive padding, readable at 32px.
Color palette: existing application's forest green #306F59, deeper green #214D3E, warm ivory #F6F7F3, restrained sunrise gold #E9BD62.
Constraints: only one icon, straight-on centered, square canvas. Actual transparent pixels outside the rounded-square tile. Flat color surfaces, no external shadow, no 3D, no gloss, no bevel, no photographic texture, no fine detail, no watermark, no lettering except E and superscript 2, no grid of variants, no surrounding white canvas. Icon must be ready to use directly as an app icon.
```

## 天空蓝调整提示词

```text
Use case: precise-object-edit / logo-brand.
Edit target: the supplied E²-Day application icon.
Primary request: change ONLY the forest-green background inside the rounded-square tile to a clear, pleasant sky blue, so the golden sun feels naturally set against a blue sky.
Color: medium bright sky blue approximately #489DDA, with at most the same extremely subtle tonal shading as the source. All areas currently forest-green, including the green negative-space curved path through the white E, should become the same sky-blue family. No green remains.
Invariants: preserve the exact large ivory-white capital E shape, the ivory-white superscript numeral 2 and its upper-right placement, the golden yellow sun below the 2, the winding path, the rounded-square silhouette, the margins, and the clean composition. Do not add clouds, rays, extra symbols, text or effects. Keep the E² lettering exactly legible. Preserve genuine transparency outside the rounded-square tile; no opaque canvas or external shadow. Deliver a single ready-to-use square PNG app icon, not a mockup.
```

## 透明边缘整理提示词

```text
Use case: precise-object-edit.
Input Image 1 is the edit target: a sky-blue E² application icon. Input Image 2 is a supporting reference for the clean rounded-square outline and transparent margins only; do NOT copy its green color.
Task: clean the target icon's outer transparency edge. Remove every stray cyan/teal speck, fringe, disconnected dot, and ragged pixel outside the rounded-square tile, especially at the bottom and sides. The silhouette must be an immaculate smooth antialiased rounded square with fully transparent space outside. Use the reference's clean outline, with the same narrow equal transparent margins.
Preserve exactly the target's sky-blue background, ivory E, superscript 2, golden sun, winding road, scale, positions, and interior design. Change only the outer edge cleanup. Do not redesign the icon, do not change the blue color, do not add any element, shadow, border, or halo. A single finished square PNG app icon, actual alpha transparency, clean enough for production Windows ICO conversion.
```
