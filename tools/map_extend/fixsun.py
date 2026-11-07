import cv2, numpy as np
im = cv2.imread('stitched_1.jpg')
y0, y1, x0, x1 = 0, 200, 1900, 2300
crop = im[y0:y1, x0:x1].copy()
l = crop.astype(np.float32).mean(2)
yy, xx = np.mgrid[y0:y1, x0:x1]
cy, cx = np.unravel_index(cv2.GaussianBlur(l, (0, 0), 4).argmax(), l.shape); cy += y0; cx += x0
r = np.hypot(xx - cx, yy - cy)
mask = ((l > 175) & (r < 34)).astype(np.uint8) * 255
mask = cv2.dilate(mask, np.ones((5, 5), np.uint8), iterations=2)
print('sun', cx, cy, 'mask', int((mask > 0).sum()))
fix = cv2.inpaint(crop, mask, 12, cv2.INPAINT_TELEA).astype(np.float32)
blur = cv2.GaussianBlur(fix, (0, 0), 14)
w = (np.clip(1 - r / 75, 0, 1) ** 1.4)[..., None]
fix = fix * (1 - 0.6 * w) + blur * 0.6 * w
fix *= (1 - 0.1 * w)
im[y0:y1, x0:x1] = np.clip(fix, 0, 255).astype(np.uint8)
cv2.imwrite('stitched_1.jpg', im, [cv2.IMWRITE_JPEG_QUALITY, 92])
cv2.imwrite('sun_fixed.jpg', im[0:200, cx - 200:cx + 200])
