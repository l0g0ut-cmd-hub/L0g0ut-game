"""Грубая проверка CS0136/CS0128: одно имя локальной переменной объявлено во вложенном и объемлющем блоке одного метода."""
import re, sys
TYPES = r'(?:var|int|float|bool|string|double|short|long|byte|char|[A-Z][A-Za-z0-9_]*(?:\.[A-Z][A-Za-z0-9_]*)*(?:<[^<>;()]*>)?(?:\[\])*)'
DECL = re.compile(r'(?<![\w.])' + TYPES + r'\s+([a-z_][A-Za-z0-9_]*)\s*(?=[=;,)]|\bin\b)')
def strip(src):
    src = re.sub(r'//[^\n]*', '', src)
    src = re.sub(r'/\*.*?\*/', '', src, flags=re.S)
    src = re.sub(r'@?"(?:\\.|[^"\\])*"', '""', src)
    src = re.sub(r"'(?:\\.|[^'\\])'", "''", src)
    return src
def check(path):
    s = strip(open(path, encoding='utf-8').read())
    # блоки: стек {id, names}; метод — блок на глубине класса+1 (грубо: глубина 3 при namespace, 2 без)
    stack = []; out = []
    i = 0; depth = 0
    blocks = []  # (start, end, depth)
    st = []
    for k, ch in enumerate(s):
        if ch == '{': st.append(k)
        elif ch == '}' and st:
            a = st.pop(); blocks.append((a, k, len(st)))
    # объявления с позицией
    decls = []
    for m in DECL.finditer(s):
        decls.append((m.start(1), m.group(1)))
        # «int w = 1, h = 2;» — остальные имена через запятую на том же уровне скобок
        lvl = 0; k = m.end(1)
        while k < len(s):
            ch = s[k]
            if ch in '([{': lvl += 1
            elif ch in ')]}':
                if lvl == 0: break
                lvl -= 1
            elif ch == ';' and lvl == 0: break
            elif ch == ',' and lvl == 0:
                mm = re.match(r',\s*([a-z_][A-Za-z0-9_]*)\s*(?==)', s[k:])
                if mm: decls.append((k + mm.start(1), mm.group(1)))
            k += 1
    # параметры лямбд и for(;;) уже ловятся (int i = ...). Для каждого объявления — самый внутренний блок
    def inner(pos):
        best = None
        for a, b, d in blocks:
            if a < pos < b and (best is None or a > best[0]): best = (a, b, d)
        return best
    # заголовки for/foreach/using/catch: переменная живёт только в своём операторе (теле цикла)
    headers = []
    for m in re.finditer(r'\b(for|foreach|using|catch)\s*\(', s):
        a = m.end() - 1; lvl = 0; e = a
        for e in range(a, len(s)):
            if s[e] == '(': lvl += 1
            elif s[e] == ')':
                lvl -= 1
                if lvl == 0: break
        j = e + 1
        while j < len(s) and s[j] in ' \t\r\n': j += 1
        body = None
        if j < len(s) and s[j] == '{':
            for b in blocks:
                if b[0] == j: body = b; break
        if body is None:   # тело без скобок — до ';'
            end = s.find(';', j); body = (a, end if end > 0 else j, 99)
        headers.append((a, e, body))
    info = []
    for pos, name in decls:
        blk = None
        for a, e, body in headers:
            if a < pos < e: blk = (body[0] - 1, body[1], body[2] if body[2] != 99 else 99); break
        if blk is None: blk = inner(pos)
        if blk is None: continue
        # for/foreach-переменная живёт в теле цикла: считаем её блоком следующий '{' после ')' если есть
        info.append((pos, name, blk))
    # метод: самый внешний блок с глубиной >= 2, внутри которого объявление (поле класса — глубина класса, отсекаем)
    for i1, (p1, n1, b1) in enumerate(info):
        for p2, n2, b2 in info[i1 + 1:]:
            if n1 != n2 or b1 == b2: continue
            # b2 вложен в b1 или наоборот, и оба на глубине метода (>=2)
            if b1[2] < 2 or b2[2] < 2: continue
            if (b1[0] < b2[0] and b2[1] < b1[1]) or (b2[0] < b1[0] and b1[1] < b2[1]):
                line = s.count('\n', 0, p2) + 1
                out.append(f'{path}:{line}: {n1}')
    return out
res = []
for p in sys.argv[1:]: res += check(p)
print('\n'.join(sorted(set(res))) if res else 'clean')
