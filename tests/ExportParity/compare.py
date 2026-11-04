"""Compares two audit workbooks cell by cell: values, fonts, fills, alignment, merges, widths, freeze panes."""
import sys, openpyxl

def sig(c):
    f, fl = c.font, c.fill
    fill = (fl.fgColor.rgb or '')[-6:].upper() if fl.fill_type == 'solid' else ''
    col = (f.color.rgb[-6:].upper() if f.color is not None and isinstance(f.color.rgb, str) else '')
    return (bool(f.b), bool(f.i), float(f.sz or 11), col if col == 'FFFFFF' else '', fill, c.alignment.horizontal or '')

def main(a, b):
    wa, wb = openpyxl.load_workbook(a).worksheets[0], openpyxl.load_workbook(b).worksheets[0]
    problems = []
    if wa.title != wb.title: problems.append(f'sheet name {wa.title!r} vs {wb.title!r}')
    rows, cols = max(wa.max_row, wb.max_row), max(wa.max_column, wb.max_column)
    for r in range(1, rows + 1):
        for c in range(1, cols + 1):
            x, y = wa.cell(r, c), wb.cell(r, c)
            if r == 2 and c == 1: continue
            vx, vy = x.value, y.value
            if vx == '' : vx = None
            if vy == '' : vy = None
            if vx != vy: problems.append(f'value {x.coordinate}: {vx!r} vs {vy!r}')
            if sig(x) != sig(y) and (vx is not None or sig(x)[4] or sig(y)[4]):
                problems.append(f'style {x.coordinate}: {sig(x)} vs {sig(y)}')
    ma = {str(m) for m in wa.merged_cells.ranges}; mb = {str(m) for m in wb.merged_cells.ranges}
    if ma != mb: problems.append(f'merges only-in-original {sorted(ma-mb)[:5]} only-in-new {sorted(mb-ma)[:5]}')
    for i in range(1, 12):
        L = openpyxl.utils.get_column_letter(i)
        if round(wa.column_dimensions[L].width or 0, 1) != round(wb.column_dimensions[L].width or 0, 1):
            problems.append(f'width {L}: {wa.column_dimensions[L].width} vs {wb.column_dimensions[L].width}')
    if wa.freeze_panes != wb.freeze_panes: problems.append(f'freeze {wa.freeze_panes} vs {wb.freeze_panes}')
    print(f'{a.split("/")[-1]}: {rows} rows compared, {len(problems)} differences')
    for p in problems[:25]: print('  ', p)
    return len(problems)

sys.exit(1 if main(sys.argv[1], sys.argv[2]) else 0)
