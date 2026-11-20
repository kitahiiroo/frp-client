"""将简约几何图标母版导出为透明 PNG、多尺寸 Windows ICO 和托盘预览。"""

import json
import xml.etree.ElementTree as ET
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


WORKSPACE = Path(__file__).resolve().parents[1]
ASSETS = WORKSPACE / "src" / "FrpClient.App" / "Assets"
ICON_SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)


def render_icon(size: int) -> Image.Image:
    """以超采样绘制 SVG 中的圆角矩形和三角箭头，保持小图标边缘清晰。"""
    root = ET.parse(ASSETS / "frpclient.svg").getroot()
    sample_size = size * (8 if size <= 256 else 4)
    scale = sample_size / 64
    canvas = Image.new("RGBA", (sample_size, sample_size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(canvas)

    for element in root:
        tag = element.tag.rsplit("}", 1)[-1]
        if tag in {"title", "desc"}:
            continue
        fill = element.attrib["fill"]
        if tag == "rect":
            x, y, width, height = (
                float(element.attrib[key]) * scale
                for key in ("x", "y", "width", "height")
            )
            radius = float(element.attrib.get("rx", "0")) * scale
            draw.rounded_rectangle((x, y, x + width, y + height), radius, fill=fill)
        elif tag == "polygon":
            points = [
                tuple(float(value) * scale for value in pair.split(","))
                for pair in element.attrib["points"].split()
            ]
            draw.polygon(points, fill=fill)
        else:
            raise ValueError(f"图标母版包含尚未支持的图形：{tag}")

    image = canvas.resize((size, size), Image.Resampling.LANCZOS)
    # 清除缩小滤镜在完全透明区域产生的极淡像素，避免托盘角落留下色点。
    alpha = image.getchannel("A").point([0 if value <= 3 else value for value in range(256)])
    image.putalpha(alpha)
    return image


def load_preview_font(size: int) -> ImageFont.ImageFont:
    """优先使用 Windows 中文字体，让预览标签能正确显示。"""
    font_path = Path("C:/Windows/Fonts/msyh.ttc")
    if font_path.exists():
        return ImageFont.truetype(str(font_path), size)
    return ImageFont.load_default(size=size)


def create_preview(frames: dict[int, Image.Image]) -> Path:
    """将实际尺寸图标放到明暗背景中，方便检查托盘显示效果。"""
    preview = Image.new("RGB", (720, 320), "#EEF2F7")
    draw = ImageDraw.Draw(preview)
    title_font = load_preview_font(20)
    label_font = load_preview_font(14)
    draw.text((24, 15), "托盘尺寸预览 · 图标按实际像素显示", font=title_font, fill="#172033")

    sizes = (16, 20, 24, 32, 40, 48)
    for row, (background, foreground, label) in enumerate(
        (("#FFFFFF", "#253047", "浅色任务栏"), ("#202124", "#E8EAED", "深色任务栏"))
    ):
        top = 58 + row * 128
        draw.rounded_rectangle((16, top, 704, top + 116), 12, fill=background)
        draw.text((32, top + 13), label, font=label_font, fill=foreground)
        for index, size in enumerate(sizes):
            center_x = 194 + index * 86
            icon = frames[size]
            preview.paste(icon, (center_x - size // 2, top + 61 - size // 2), icon)
            draw.text((center_x, top + 90), f"{size}px", font=label_font, fill=foreground, anchor="mt")

    path = WORKSPACE / "publish" / "icon-preview" / "frpclient-tray-preview.png"
    path.parent.mkdir(parents=True, exist_ok=True)
    preview.save(path, optimize=True)
    return path


def export_icons() -> None:
    """更新现有 PNG、ICO 资源并验证每个 ICO 尺寸和透明通道。"""
    master = render_icon(512)
    frames = {size: render_icon(size) for size in ICON_SIZES}
    png_path = ASSETS / "frpclient.png"
    ico_path = ASSETS / "frpclient.ico"
    master.save(png_path, optimize=True)
    master.save(
        ico_path,
        format="ICO",
        sizes=[(size, size) for size in ICON_SIZES],
        append_images=list(frames.values()),
    )

    with Image.open(ico_path) as icon:
        expected_sizes = {(size, size) for size in ICON_SIZES}
        if icon.info["sizes"] != expected_sizes:
            raise ValueError("ICO 的尺寸集合不完整。")
        for size in expected_sizes:
            frame = icon.ico.getimage(size).convert("RGBA")
            if frame.getpixel((0, 0))[3] != 0 or frame.getchannel("A").getextrema() != (0, 255):
                raise ValueError(f"ICO 透明通道异常：{size}")

    preview_path = create_preview(frames)
    print(json.dumps({
        "png": str(png_path),
        "ico": str(ico_path),
        "ico_sizes": ICON_SIZES,
        "preview": str(preview_path),
        "png_bytes": png_path.stat().st_size,
        "ico_bytes": ico_path.stat().st_size,
    }, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    export_icons()
