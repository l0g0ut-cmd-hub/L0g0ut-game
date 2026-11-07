import cv2, numpy as np, sys
OLD='bladepath/Assets/_Project/Resources/UI/map_%d.jpg'
def run(k):
    old = cv2.imread(OLD % k); new = cv2.imread('ext/new_%d.png' % k)
    H, W = old.shape[:2]
    # 1) выравнивание: признаки ORB/SIFT в правой полосе старой и левой полосе новой → подобие (сдвиг+масштаб+поворот)
    sift = cv2.SIFT_create(6000)
    mo = np.zeros(old.shape[:2], np.uint8); mo[:, W - 560:] = 255
    mn = np.zeros(new.shape[:2], np.uint8); mn[:, :620] = 255
    ko, do = sift.detectAndCompute(cv2.cvtColor(old, cv2.COLOR_BGR2GRAY), mo)
    kn, dn = sift.detectAndCompute(cv2.cvtColor(new, cv2.COLOR_BGR2GRAY), mn)
    m = cv2.BFMatcher().knnMatch(dn, do, k=2)
    good = [a for a, b in m if a.distance < 0.75 * b.distance]
    src = np.float32([kn[g.queryIdx].pt for g in good]); dst = np.float32([ko[g.trainIdx].pt for g in good])
    M, inl = cv2.estimateAffinePartial2D(src, dst, method=cv2.RANSAC, ransacReprojThreshold=4.0, maxIters=5000)
    s = np.hypot(M[0, 0], M[0, 1]); ang = np.degrees(np.arctan2(M[1, 0], M[0, 0]))
    print('map', k, 'matches', len(good), 'inliers', int(inl.sum()), 'scale %.4f rot %.3f tx %.1f ty %.1f' % (s, ang, M[0, 2], M[1, 2]))
    # 2) новая картинка в координатах старой (холст шире)
    TW = int(np.ceil(M[0, 2] + 1536 * s)) + 2
    TW = max(TW, W)
    warped = cv2.warpAffine(new, M, (TW, H), flags=cv2.INTER_LANCZOS4, borderMode=cv2.BORDER_CONSTANT, borderValue=0)
    valid = cv2.warpAffine(np.full(new.shape[:2], 255, np.uint8), M, (TW, H), flags=cv2.INTER_NEAREST) > 0
    # 3) шов: в полосе перехлёста — путь минимальной разницы (динамика сверху вниз), вокруг шва — мягкий переход
    x0 = int(np.ceil(M[0, 2])) + 30; x1 = W - 20
    canvas = np.zeros((H, TW, 3), np.float32); canvas[:, :W] = old
    diff = np.abs(old[:, x0:x1].astype(np.float32) - warped[:, x0:x1].astype(np.float32)).sum(2)
    diff = cv2.GaussianBlur(diff, (0, 0), 3)
    # штраф: держать шов подальше от краёв полосы
    w = x1 - x0; pen = np.abs(np.arange(w) - w / 2) / (w / 2); diff += pen[None, :] * diff.mean() * 0.6
    cost = diff.copy(); back = np.zeros_like(cost, np.int32)
    for y in range(1, H):
        prev = cost[y - 1]
        l = np.r_[np.inf, prev[:-1]]; r = np.r_[prev[1:], np.inf]
        st = np.vstack([l, prev, r]); a = st.argmin(0)
        cost[y] += st[a, np.arange(w)]; back[y] = np.arange(w) + a - 1
    seam = np.zeros(H, np.int32); seam[-1] = int(cost[-1].argmin())
    for y in range(H - 1, 0, -1): seam[y - 1] = back[y, seam[y]]
    seam = cv2.GaussianBlur(seam.astype(np.float32).reshape(-1, 1), (1, 0), 6).ravel() + x0
    xs = np.arange(TW)[None, :]
    feather = 22.0
    alpha = np.clip((xs - seam[:, None]) / feather + 0.5, 0, 1)   # 0 — старая, 1 — новая
    alpha[:, W:] = 1.0
    alpha = alpha * valid
    out = canvas * (1 - alpha[..., None]) + warped.astype(np.float32) * alpha[..., None]
    # справа, где новой нет (если масштаб < 1) — обрезать
    cols = valid.mean(0) > 0.98; right = W + int(np.argmin(cols[W:])) if not cols[W:].all() else TW
    out = out[:, :right]
    cv2.imwrite('ext/stitched_%d.jpg' % k, out.astype(np.uint8), [cv2.IMWRITE_JPEG_QUALITY, 92])
    # контроль: шов красной линией
    dbg = out.copy()
    for y in range(0, H, 2): dbg[y, int(seam[y])] = (0, 0, 255)
    cv2.imwrite('ext/seam_%d.jpg' % k, cv2.resize(dbg[:, int(seam.min()) - 300:int(seam.max()) + 300], None, fx=0.5, fy=0.5).astype(np.uint8))
    print('  out', out.shape[1], 'x', H, 'seam x', int(seam.min()), '..', int(seam.max()))
for k in [int(a) for a in sys.argv[1:]] or range(3): run(k)
