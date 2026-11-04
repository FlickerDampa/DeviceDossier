"""Turns an IT_Audit_*.xlsx produced by IT_Audit_Tool.ps1 into the JSON model used by the parity test."""
import json, sys, openpyxl

def fill(c):
    f = c.fill
    return (f.fgColor.rgb or '')[-6:].upper() if f and f.fill_type == 'solid' else ''

def main(src, dst):
    ws = openpyxl.load_workbook(src).worksheets[0]
    merged = {str(m) for m in ws.merged_cells.ranges}
    gen = ws.cell(2, 1).value
    out = {'generated': gen.split('Generated: ')[1].split('   Device:')[0], 'device': gen.split('Device: ')[1], 'sections': []}
    sec = block = None
    r = 4
    while r <= ws.max_row:
        a = ws.cell(r, 1)
        if a.value is None:
            block = None; r += 1; continue
        if fill(a) == '1F497D':
            num, title = a.value.split('. ', 1)
            sec = {'number': num, 'title': title, 'blocks': []}; out['sections'].append(sec); block = None
        elif f'A{r}:G{r}' in merged and fill(a) == '':
            block = {'caption': a.value, 'style': 'Italic' if a.font.i else 'Bold', 'kind': None, 'fields': [], 'headers': [], 'rows': []}
            sec['blocks'].append(block)
        elif fill(a) == 'BDD7EE':
            headers = []
            c = 1
            while ws.cell(r, c).value is not None and fill(ws.cell(r, c)) == 'BDD7EE':
                headers.append(ws.cell(r, c).value); c += 1
            if block is None or block['kind'] is not None:
                block = {'caption': None, 'style': 'None', 'kind': None, 'fields': [], 'headers': [], 'rows': []}; sec['blocks'].append(block)
            block['kind'] = 'Table'; block['headers'] = headers
        elif block is not None and block['kind'] == 'Table':
            block['rows'].append([ws.cell(r, c + 1).value for c in range(len(block['headers']))])
        else:
            if block is None or block['kind'] not in (None, 'KeyValue'):
                block = {'caption': None, 'style': 'None', 'kind': None, 'fields': [], 'headers': [], 'rows': []}; sec['blocks'].append(block)
            block['kind'] = 'KeyValue'; block['fields'].append([a.value, ws.cell(r, 2).value])
        r += 1
    json.dump(out, open(dst, 'w'), indent=1, default=str)

main(sys.argv[1], sys.argv[2])
