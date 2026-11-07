# Сёгунат: новая картинка после совмещения (масштаб 1,125, сдвиг 5 px) не доходит до верхнего края — верхние ~9 строк чёрные.
# Заполняем их ближайшей строкой неба, с лёгким размытием, чтобы не было полосы.
import cv2, numpy as np
im = cv2.imread('ext/stitched_2.jpg')
H, W = im.shape[:2]
for x in range(W):
    col = im[:16, x]
    dark = col.max(1) < 18
    if not dark[:6].any(): continue
    first = int(np.argmax(~dark)) if (~dark).any() else 0
    first = min(first + 2, 14)
    im[:first, x] = im[first, x]
band = im[:24].copy(); im[:24] = cv2.GaussianBlur(band, (0, 0), 2.5)
cv2.imwrite('ext/stitched_2.jpg', im, [cv2.IMWRITE_JPEG_QUALITY, 92])
print((im[:12, 1300:].max(2) < 12).sum())
