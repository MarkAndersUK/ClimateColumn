// Converts the ClimateColumn README to a self-contained HTML artifact.
//
// The LaTeX must render without a maths library: the artifact CSP blocks external scripts and
// inlining KaTeX would dwarf the document. Scripts become <sup>/<sub> rather than Unicode, because
// only a handful of characters have Unicode superscript forms and the rest would silently degrade.
//
// Deliberately not a general Markdown or LaTeX implementation. It handles what this one document
// contains and reports anything it does not recognise rather than emitting it silently.
//
// No placeholder mechanism: the README contains no raw < or > at all (verified), so escaping can
// happen first and every later substitution simply inserts tags. An earlier version held code and
// maths aside behind numeric markers and restored them afterwards, which went wrong twice - first
// matching bare digits in the prose, then losing the markers to an escape that collapsed. Removing
// the mechanism removed both bugs.

const fs = require('fs');
const [, , inPath, outPath] = process.argv;
const src = fs.readFileSync(inPath, 'utf8');
const unmatched = new Set();

const SYMBOLS = {
  varepsilon: 'ε', epsilon: 'ε', sigma: 'σ', tau: 'τ', rho: 'ρ',
  pi: 'π', beta: 'β', mu: 'µ', nu: 'ν', gamma: 'γ', Gamma: 'Γ',
  Delta: 'Δ', delta: 'δ', lambda: 'λ', Phi: 'Φ', phi: 'φ',
  theta: 'θ', alpha: 'α', omega: 'ω', Omega: 'Ω', zeta: 'ζ',
  infty: '∞', partial: '∂', sum: 'Σ', int: '∫', pm: '±', mp: '∓',
  times: '×', cdot: '·', approx: '≈', sim: '∼', simeq: '≃',
  equiv: '≡', ll: '≪', gg: '≫', le: '≤', leq: '≤', ge: '≥',
  geq: '≥', neq: '≠', to: '→', rightarrow: '→', leftarrow: '←',
  uparrow: '↑', downarrow: '↓', Rightarrow: '⇒', propto: '∝',
  ldots: '…', dots: '…', prime: '′', langle: '⟨', rangle: '⟩',
  ln: 'ln', log: 'log', exp: 'exp', min: 'min', max: 'max',
};

function latex(tex) {
  let s = tex;

  // Presentation-only commands, gone before anything structural runs.
  s = s.replace(/\\(?:left|right|bigl|bigr|Bigl|Bigr|big|Big)\s*(?=[(){}[\]|.])/g, '');
  s = s.replace(/\\[{}]/g, (m) => m[1]);
  s = s.replace(/\\(?:qquad|quad)/g, ' ');
  s = s.replace(/\\[,;:!]/g, ' ');
  s = s.replace(/\\([%&#_$])/g, '$1');
  s = s.replace(/\\ /g, ' ');

  const wrap = (x) => (/^[^+\-*/\s]+$/.test(x) ? x : '(' + x + ')');

  // One loop to stability, because the rules feed each other and the nesting runs both ways. The
  // order inside the loop is not arbitrary - two orderings were tried and were wrong:
  //
  //   * Flattening a text run straight to its contents breaks subscripts. tau_\text{dry} would
  //     become tau_dry, and the single-character rule would then take only the d, stranding "ry".
  //     Rewriting to a bare group keeps it whole for the script rule.
  //
  //   * Stripping redundant groups before fractions destroys them. A fraction's second argument is
  //     preceded by a closing brace, so the strip rule matches it and removes the braces before the
  //     fraction rule ever sees a well-formed pair.
  for (let pass = 0; pass < 16; pass++) {
    const before = s;

    // Radicals keep an explicit radicand: a bare radical over a stripped group would read as
    // covering only its first term.
    s = s.replace(/\\sqrt\s*\{([^{}]*)\}/g, '√($1)');

    s = s.replace(/\\(?:mathrm|mathbf|mathit|text|operatorname)\s*\{([^{}]*)\}/g, '{$1}');

    s = s.replace(/\^\{([^{}]*)\}/g, (_, x) => '<sup>' + x + '</sup>')
         .replace(/_\{([^{}]*)\}/g, (_, x) => '<sub>' + x + '</sub>')
         .replace(/\^([A-Za-z0-9+\-'])/g, '<sup>$1</sup>')
         .replace(/_([A-Za-z0-9+\-'])/g, '<sub>$1</sub>');

    s = s.replace(/\\(?:d|t)?frac\s*\{([^{}]*)\}\s*\{([^{}]*)\}/g,
      (_, a, b) => wrap(a) + '/' + wrap(b));

    s = s.replace(/(^|[^\^_A-Za-z])\{([^{}]*)\}/g, '$1$2');

    if (s === before) break;
  }

  for (const name of Object.keys(SYMBOLS).sort((a, b) => b.length - a.length)) {
    s = s.replace(new RegExp('\\\\' + name + '(?![A-Za-z])', 'g'), SYMBOLS[name]);
  }

  s = s.replace(/\\([A-Za-z]+)/g, (m, name) => { unmatched.add(name); return name; });
  s = s.replace(/[{}]/g, '');
  return s.replace(/\s+/g, ' ').trim();
}

const esc = (t) => t.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

function inline(text) {
  let s = esc(text);
  s = s.replace(/`([^`]+)`/g, (_, c) => '<code>' + c + '</code>');
  s = s.replace(/\$([^$]+)\$/g, (_, m) => '<span class="math">' + latex(m) + '</span>');
  s = s.replace(/\[([^\]]+)\]\(([^)]+)\)/g, (_, t, u) => '<a href="' + u + '">' + t + '</a>');
  s = s.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
  s = s.replace(/(^|[\s(])\*([^*\s][^*]*)\*/g, '$1<em>$2</em>');
  s = s.replace(/---/g, '—');
  return s;
}

const lines = src.split(/\r?\n/);
const out = [];
const toc = [];
let i = 0;

const slug = (t) => t.toLowerCase().replace(/<[^>]+>/g, '').replace(/[^a-z0-9 ]/g, '')
  .trim().replace(/\s+/g, '-');

while (i < lines.length) {
  const line = lines[i];

  if (/^```/.test(line)) {
    const body = [];
    i++;
    while (i < lines.length && !/^```/.test(lines[i])) body.push(lines[i++]);
    i++;
    out.push('<pre><code>' + esc(body.join('\n')) + '</code></pre>');
    continue;
  }

  if (/^\$\$/.test(line)) {
    const body = [];
    const single = /^\$\$.*\$\$\s*$/.test(line);
    body.push(line.replace(/^\$\$/, '').replace(/\$\$\s*$/, ''));
    if (!single) {
      i++;
      while (i < lines.length && !/\$\$/.test(lines[i])) body.push(lines[i++]);
      if (i < lines.length) body.push(lines[i].replace(/\$\$.*$/, ''));
    }
    i++;
    out.push('<div class="display-math">' + latex(esc(body.join(' '))) + '</div>');
    continue;
  }

  if (/^\|/.test(line) && /^\|[\s:|-]+\|?\s*$/.test(lines[i + 1] || '')) {
    const cells = (r) => r.replace(/^\||\|$/g, '').split('|').map((c) => c.trim());
    const head = cells(line);
    const align = cells(lines[i + 1]).map((a) =>
      a.startsWith(':') && a.endsWith(':') ? 'center' : a.endsWith(':') ? 'right' : 'left');
    i += 2;
    const rows = [];
    while (i < lines.length && /^\|/.test(lines[i])) rows.push(cells(lines[i++]));

    let t = '<div class="tablewrap"><table><thead><tr>';
    head.forEach((h, k) => { t += '<th class="a-' + (align[k] || 'left') + '">' + inline(h) + '</th>'; });
    t += '</tr></thead><tbody>';
    for (const r of rows) {
      t += '<tr>';
      r.forEach((c, k) => { t += '<td class="a-' + (align[k] || 'left') + '">' + inline(c) + '</td>'; });
      t += '</tr>';
    }
    out.push(t + '</tbody></table></div>');
    continue;
  }

  const h = line.match(/^(#{1,4})\s+(.*)$/);
  if (h) {
    const level = h[1].length;
    const id = slug(h[2]);
    if (level >= 2 && level <= 3) toc.push({ level, html: inline(h[2]), id });
    out.push('<h' + level + ' id="' + id + '">' + inline(h[2]) + '</h' + level + '>');
    i++;
    continue;
  }

  if (/^---+\s*$/.test(line)) { out.push('<hr>'); i++; continue; }

  if (/^[-*]\s+/.test(line)) {
    const items = [];
    while (i < lines.length && (/^[-*]\s+/.test(lines[i]) || /^\s{2,}\S/.test(lines[i]))) {
      if (/^[-*]\s+/.test(lines[i])) items.push(lines[i].replace(/^[-*]\s+/, ''));
      else items[items.length - 1] += ' ' + lines[i].trim();
      i++;
    }
    out.push('<ul>' + items.map((t) => '<li>' + inline(t) + '</li>').join('') + '</ul>');
    continue;
  }

  if (line.trim() === '') { i++; continue; }

  const para = [];
  while (i < lines.length && lines[i].trim() !== ''
         && !/^(#{1,4}\s|```|\||---+\s*$|\$\$|[-*]\s)/.test(lines[i])) {
    para.push(lines[i++]);
  }
  out.push('<p>' + inline(para.join(' ')) + '</p>');
}

if (unmatched.size) {
  console.error('UNKNOWN LATEX: ' + [...unmatched].join(', '));
  process.exitCode = 2;
}

fs.writeFileSync(outPath, JSON.stringify({ body: out.join('\n'), toc }), 'utf8');
console.error('ok: ' + out.length + ' blocks, ' + toc.length + ' toc entries');
