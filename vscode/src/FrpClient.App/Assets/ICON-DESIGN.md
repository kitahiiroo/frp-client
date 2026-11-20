# 图标资源

- `frpclient.svg`：可编辑的几何母版。
- `frpclient.png`：512 × 512 透明 PNG。
- `frpclient.ico`：16、20、24、32、40、48、64、128、256 像素图层。
- 程序 EXE、窗口和托盘继续共用 `frpclient.ico`，无需新增运行时依赖。

设计方向使用内置 `image_gen` 工具生成，交付图标按简约几何轮廓重新绘制，使用纯色填充并为小尺寸导出超采样图层。没有使用 CLI/API 图像生成。

运行 `tools/export_app_icon.py` 可以从 SVG 母版重新生成 PNG、ICO 和明暗背景尺寸预览。此工具只处理图标资源，不会编译或启动客户端，需要 Pillow。

## 设计提示词

```text
Use case: logo-brand. Create a finished app icon for a Windows FRP port-forwarding client. This is a NEW replacement design; the previous dark thin arrow icon is only context, do not reproduce it. Primary goal: extremely clear and easy to recognize in a Windows system tray at 16x16 and 24x24 pixels. Function to communicate: a network tunnel / bidirectional port forwarding. Design a single compact geometric emblem: two bold parallel horizontal arrows, upper arrow pointing right, lower arrow pointing left, joined visually as a simple interlocking connection / tunnel symbol. Keep the two arrowheads unmistakable. Place the clean white emblem inside a vivid saturated azure-blue rounded-square tile, using the application's existing blue visual identity but much brighter and more visible than the old navy icon. Pure flat solid colors, generous line weights, balanced negative space, very few shapes. The tile should occupy about 94% of the square canvas, and the glyph about 76% of its width, with consistent padding. Rounded-square silhouette should have gently rounded corners, not a circle. It must remain legible on both light and dark Windows taskbars. Deliver one centered, straight-on production icon at high resolution, square composition, with genuinely transparent pixels outside the tile. NO text, NO letters, NO labels, NO mockup, NO presentation sheet, NO desktop scene, NO watermark, NO gradients, NO shadows, NO 3D, NO hairlines, NO complex circuitry, NO decorative border. Output the icon itself only.
```
