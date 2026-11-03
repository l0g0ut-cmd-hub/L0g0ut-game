"""
Мокап Mixamo (tools/mocap/mixamo_json, выгрузка tools/blender/mixamo_extract.py) → ключи ударов PoseTrack
→ Assets/_Project/Scripts/Weapon/MocapSwings.cs (сгенерировано, руками не править).

Каналы (PoseCh): руки (цуба относительно середины плеч), клинок (Euler Unity: pitch +вниз, yaw +вправо, roll; кромка −Y),
таз (вынос, опускание, поворот, наклон), грудь (поворот и наклон поверх таза), стопы (вынос передней/задней, пятка).
Время клипа растягивается по кусочкам так, что самая быстрая часть удара (контакт) попадает ровно в фазу Strike
игрового профиля: тайминги и баланс боя не меняются. Начало и конец плавно сходятся к стойке (ReadyPose).
Запуск: python3 tools/mocap_to_tracks.py
"""
import json, math, os
import numpy as np
ROOT = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(ROOT, 'mocap', 'mixamo_json')
OUT = os.path.join(ROOT, '..', 'Assets', '_Project', 'Scripts', 'Weapon', 'MocapSwings.cs')
READY = [0.13, -0.46, 0.40, -6, -10, 0, 0.0, 0.06, 35, 8, 12, 6, 0, 0, 0]
S = 1.72 / 1.82   # рост наших бойцов / X Bot
GUARD = 0.08      # цуба впереди правой кисти вдоль клинка, м (HeroRigDriver.gripFront)

# удар: клип, кадры [начало, контакт-начало, контакт-конец, конец проводки, конец], тайминги профиля, зеркально
SPEC = {
    'Overhead': ('great sword slash', [11, 17, 21, 24, 38], (0.36, 0.13, 0.15, 0.46), False),
    'SlashBack': ('great sword slash (3)', [0, 25, 30, 33, 55], (0.36, 0.14, 0.14, 0.44), False),
    'Slash': ('great sword slash (3)', [0, 25, 30, 33, 55], (0.36, 0.14, 0.14, 0.44), True),
    'LegSweep': ('great sword slash (5)', [0, 11, 16, 19, 43], (0.34, 0.15, 0.12, 0.48), False),
}

def Rx(t): c, s = math.cos(t), math.sin(t); return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])
def Ry(t): c, s = math.cos(t), math.sin(t); return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])
def Rz(t): c, s = math.cos(t), math.sin(t); return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])
def euler_to_m(p, y, r): return Ry(math.radians(y)) @ Rx(math.radians(p)) @ Rz(math.radians(r))   # Quaternion.Euler(p, y, r)
def m_to_euler(R):
    p = math.degrees(math.asin(max(-1, min(1, -R[1, 2]))))
    y = math.degrees(math.atan2(R[0, 2], R[2, 2])); r = math.degrees(math.atan2(R[1, 0], R[1, 1]))
    return p, y, r
def unwrap(v, ref): return v + 360 * round((ref - v) / 360)
def m_to_q(R):
    w = math.sqrt(max(0.0, 1 + R[0, 0] + R[1, 1] + R[2, 2])) / 2; x = math.sqrt(max(0.0, 1 + R[0, 0] - R[1, 1] - R[2, 2])) / 2
    y = math.sqrt(max(0.0, 1 - R[0, 0] + R[1, 1] - R[2, 2])) / 2; z = math.sqrt(max(0.0, 1 - R[0, 0] - R[1, 1] + R[2, 2])) / 2
    x = math.copysign(x, R[2, 1] - R[1, 2]); y = math.copysign(y, R[0, 2] - R[2, 0]); z = math.copysign(z, R[1, 0] - R[0, 1])
    q = np.array([w, x, y, z]); return q / np.linalg.norm(q)
def q_to_m(q):
    w, x, y, z = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
def slerp(a, b, t):
    d = a.dot(b)
    if d < 0: b = -b; d = -d
    if d > 0.9995: q = a + (b - a) * t; return q / np.linalg.norm(q)
    th = math.acos(d); return (math.sin((1 - t) * th) * a + math.sin(t * th) * b) / math.sin(th)
def continuous(R, prev):
    p, y, r = m_to_euler(R)
    cands = [(p, y, r), (180 - p, y + 180, r + 180)]
    cands = [(unwrap(c[0], prev[0]), unwrap(c[1], prev[1]), unwrap(c[2], prev[2])) for c in cands]
    return min(cands, key=lambda c: sum(abs(c[i] - prev[i]) for i in range(3)))

def channels(name):
    fr = json.load(open(os.path.join(SRC, name + '.json')))['frames']
    g = lambda j: np.array([f[j] for f in fr], dtype=float)
    n = len(fr)
    gR = (g('RightHand') + g('RightHandMiddle1')) / 2; gL = (g('LeftHand') + g('LeftHandMiddle1')) / 2
    a = gR - gL; a /= np.maximum(np.linalg.norm(a, axis=1, keepdims=True), 1e-5)
    guard = gR + a * GUARD; tip = guard + a * 0.85
    sh = (g('LeftArm') + g('RightArm')) / 2
    hands = (guard - sh) * S
    vel = np.gradient(tip, axis=0) * 30
    # кромка — по ходу клинка (где он быстр), иначе держим прошлую; в начале — вниз (как в стойке)
    e_prev = np.array([0, -1.0, 0]); E = []
    for k in range(n):
        v = vel[k] - a[k] * vel[k].dot(a[k])
        if np.linalg.norm(v) > 2.5: e = v / np.linalg.norm(v)
        else: e = e_prev - a[k] * e_prev.dot(a[k]); e /= max(np.linalg.norm(e), 1e-5)
        E.append(e); e_prev = e
    blade = []; prev = None
    for k in range(n):
        Z = a[k]; Y = -E[k]; Y = Y - Z * Y.dot(Z); Y /= np.linalg.norm(Y); X = np.cross(Y, Z)
        R = np.stack([X, Y, Z], axis=1)
        p, y, r = m_to_euler(R)
        cands = [(p, y, r), (180 - p, y + 180, r + 180)]
        if prev is not None:
            cands = [(unwrap(c[0], prev[0]), unwrap(c[1], prev[1]), unwrap(c[2], prev[2])) for c in cands]
            c = min(cands, key=lambda c: sum(abs(c[i] - prev[i]) for i in range(3)))
        else: c = cands[0]
        assert np.allclose(euler_to_m(*c), R, atol=1e-6)
        blade.append(c); prev = c
    blade = np.array(blade)
    def yaw_of(rv):   # поворот по «вправо»-вектору (x вправо, z вперёд): + — повернулся вправо
        fx, fz = -rv[:, 2], rv[:, 0]; return np.degrees(np.unwrap(np.arctan2(fx, fz)))
    def pitch_of(lo, hi):
        d = hi - lo; return np.degrees(np.arctan2(d[:, 2], d[:, 1]))
    hipsR = g('RightUpLeg') - g('LeftUpLeg'); chestR = g('RightArm') - g('LeftArm')
    hy = yaw_of(hipsR); cy = yaw_of(chestR) - hy
    hp = pitch_of(g('Hips'), g('Spine')); cp = pitch_of(g('Spine2'), g('Neck')) - hp
    hips = g('Hips')
    lf, rf = g('LeftFoot'), g('RightFoot')
    front, rear = (rf, lf) if rf[0, 2] >= lf[0, 2] else (lf, rf)
    out = np.zeros((n, 15))
    out[:, 0:3] = hands; out[:, 3:6] = blade
    out[:, 6] = np.clip((hips[:, 2] - hips[0, 2]) * S, -0.15, 0.4)
    out[:, 7] = READY[7] + np.clip((hips[0, 1] - hips[:, 1]) * S, -0.1, 0.45)
    out[:, 8] = READY[8] + (hy - hy[0]); out[:, 9] = READY[9] + (hp - hp[0])
    out[:, 10] = READY[10] + (cy - cy[0]); out[:, 11] = READY[11] + (cp - cp[0])
    out[:, 12] = np.clip((front[:, 2] - front[0, 2]) * S, -0.3, 0.45)
    out[:, 13] = np.clip((rear[:, 2] - rear[0, 2]) * S, -0.3, 0.45)
    out[:, 14] = np.clip((rear[:, 1] - rear[0, 1]) / 0.08, 0, 1)
    return out, np.linalg.norm(vel, axis=1)

def build(key):
    name, fk, (W, St, Fo, Re), mirror = SPEC[key]
    ch, speed = channels(name)
    f0, s0, s1, f1, f2 = fk
    T = [0.0, W, W + St, W + St + Fo, W + St + Fo + Re]
    keys = []
    for k in range(f0, f2 + 1):
        seg = 0 if k <= s0 else 1 if k <= s1 else 2 if k <= f1 else 3
        a, b = fk[seg], fk[seg + 1]
        t = T[seg] + (T[seg + 1] - T[seg]) * (k - a) / max(1, b - a)
        v = ch[k].copy()
        if mirror:
            v[0] = -v[0]; v[4] = -v[4]; v[5] = -v[5]
            v[8] = READY[8] - (v[8] - READY[8]); v[10] = READY[10] - (v[10] - READY[10])
        # к стойке у краёв: первые 40% замаха и последние 45% возврата; клинок — по кратчайшему повороту (кватернионы)
        w = min(1.0, t / (0.4 * W), (T[4] - t) / (0.45 * Re))
        w = max(0.0, w); w = w * w * (3 - 2 * w)
        r = np.array(READY, dtype=float)
        out = r + (v - r) * w
        Rm = slerp(m_to_q(euler_to_m(*READY[3:6])), m_to_q(euler_to_m(*v[3:6])), w)
        out[3:6] = q_to_m(Rm).tolist() and [0, 0, 0]
        keys.append((round(t, 4), out, q_to_m(Rm)))
    ded = {}
    for t, v, R in keys: ded[t] = (v, R)
    items = sorted(ded.items())
    keys = []; prev = tuple(READY[3:6])
    for i, (t, (v, R)) in enumerate(items):
        if i == 0: v = np.array(READY, dtype=float); R = euler_to_m(*READY[3:6])
        if i == len(items) - 1: v = np.array(READY, dtype=float); R = euler_to_m(*READY[3:6])
        c = continuous(R, prev); v = v.copy(); v[3:6] = c; prev = c
        assert np.allclose(euler_to_m(*c), R, atol=1e-5)
        keys.append((t, v))
    keys[0] = (0.0, keys[0][1])
    print(key, name, 'ключей', len(keys), 'длина', round(T[4], 2), 'макс. скорость острия', round(float(speed[s0:s1 + 1].max()), 1), 'м/с')
    return keys

lines = ['// Сгенерировано tools/mocap_to_tracks.py из мокапа Mixamo (Great Sword Pack) — руками не править.',
         'namespace BladePath', '{', '    public static class MocapSwings', '    {',
         '        /// <summary>Мокап-удары включены (false — прежние ключи, поставленные вручную).</summary>',
         '        public static bool Enabled = true;', '',
         '        /// <summary>Ключи удара из мокапа (время, 15 каналов PoseCh) или null, если для удара мокапа нет.</summary>',
         '        public static float[][] Keys(AttackType t)', '        {', '            switch (t)', '            {']
for key in SPEC:
    lines.append('                case AttackType.%s: return %s;' % (key, key))
lines += ['                default: return null;', '            }', '        }', '']
for key in SPEC:
    keys = build(key)
    lines.append('        static readonly float[][] %s =' % key)
    lines.append('        {')
    for t, v in keys:
        lines.append('            new[] { ' + ', '.join('%.4gf' % x for x in [t] + list(v)) + ' },')
    lines.append('        };')
lines += ['    }', '}', '']
open(OUT, 'w').write('\n'.join(lines))
print('записано', OUT)
