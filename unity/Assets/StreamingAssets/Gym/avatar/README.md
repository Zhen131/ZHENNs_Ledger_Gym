# Avatar picture

The Play and Watch scenes show a small framed avatar standing on the newest candle. Put a picture in this folder to use it instead of the built-in placeholder.

- **File name:** `avatar.png`, `avatar.jpg` or `avatar.jpeg`. The scenes try them in that order and use the first one that decodes; with none of them here, the gray placeholder figure is shown.
- **Format:** PNG (transparency is kept) or JPEG.
- **Size:** a square picture of 256 × 256 pixels is a good choice; anything from 64 to 1024 pixels on a side works. A picture that is not square keeps its proportions inside the frame and is not stretched.
- A file that is not a valid PNG or JPEG is skipped with a warning in the Console, and the next name (or the placeholder) is used.
- The picture is read when the scene starts, so stop and press Play again after changing it.

Everything in `StreamingAssets/` is copied into every build, the training and evaluation players included; the picture plays no part in training or evaluation.

---

中文：把头像图放进这个文件夹，就会替换掉程序画的灰色占位小人。文件名用 `avatar.png`、`avatar.jpg` 或 `avatar.jpeg`（按这个顺序找，用第一个能打开的）；格式 PNG 或 JPEG；建议 256 × 256 像素的正方形图，不是正方形的图会按原比例缩进框里、不会被拉伸。文件坏了会在 Console 里打一条警告并改用下一个（或占位图）。换图以后要停下再按一次 Play 才会读新图。
