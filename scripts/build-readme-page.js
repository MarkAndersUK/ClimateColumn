// Wraps the converted README body in the same visual system as the companion model description,
// so the two read as one set rather than two unrelated pages.

const fs = require('fs');
const [, , bodyPath, outPath] = process.argv;
const { body, toc } = JSON.parse(fs.readFileSync(bodyPath, 'utf8'));

const nav = toc.map((t) =>
  `      <a class="lvl${t.level}" href="#${t.id}">${t.html.replace(/<[^>]+>/g, '')}</a>`).join('\n');

const css = `
  :root {
    --paper:      #fcfcfd;
    --raised:     #f4f5f8;
    --sunk:       #eef0f4;
    --ink:        #14171e;
    --ink-2:      #464e5d;
    --ink-3:      #7c8493;
    --rule:       #dfe3e9;
    --rule-soft:  #eaedf2;
    --warm:       #a55f13;
    --warm-soft:  #f4e6d4;
    --cold:       #2b6a8c;
    --cold-soft:  #dfeaf1;

    --serif: "Sitka Text", Georgia, "Bitstream Charter", Charter, serif;
    --sans:  ui-sans-serif, -apple-system, "Segoe UI Variable Text", "Segoe UI", Roboto, sans-serif;
    --mono:  ui-monospace, "Cascadia Mono", "Cascadia Code", Consolas, "SF Mono", monospace;
  }

  @media (prefers-color-scheme: dark) {
    :root:not([data-theme="light"]) {
      --paper: #11141a; --raised: #181c24; --sunk: #1e222b;
      --ink: #e7eaf0; --ink-2: #a8b1c0; --ink-3: #737c8c;
      --rule: #2a2f3a; --rule-soft: #21262f;
      --warm: #d9973f; --warm-soft: #332615;
      --cold: #67aacd; --cold-soft: #16242d;
    }
  }

  :root[data-theme="dark"] {
    --paper: #11141a; --raised: #181c24; --sunk: #1e222b;
    --ink: #e7eaf0; --ink-2: #a8b1c0; --ink-3: #737c8c;
    --rule: #2a2f3a; --rule-soft: #21262f;
    --warm: #d9973f; --warm-soft: #332615;
    --cold: #67aacd; --cold-soft: #16242d;
  }

  body {
    background: var(--paper); color: var(--ink);
    font-family: var(--sans); font-size: 16px; line-height: 1.65;
    -webkit-font-smoothing: antialiased;
  }

  /* A reference this long needs a way in, so the page is a two-column shell with a sticky
     contents rail. It collapses to a plain block above the article on narrow screens, where a
     sticky sidebar would eat the reading width. */
  .shell { max-width: 76rem; margin: 0 auto; padding: 3rem 1.5rem 6rem;
           display: grid; grid-template-columns: 15rem minmax(0, 1fr); gap: 3rem; }

  nav { position: sticky; top: 2rem; align-self: start; max-height: calc(100vh - 4rem);
        overflow-y: auto; border-left: 2px solid var(--rule-soft); padding-left: 1rem; }
  nav .navtitle { font-family: var(--mono); font-size: .68rem; letter-spacing: .13em;
                  text-transform: uppercase; color: var(--ink-3); margin-bottom: .9rem; }
  nav a { display: block; color: var(--ink-2); text-decoration: none; font-size: .84rem;
          line-height: 1.35; padding: .22rem 0; border-radius: 3px; }
  nav a:hover { color: var(--warm); }
  nav a.lvl3 { padding-left: .85rem; font-size: .79rem; color: var(--ink-3); }
  nav a:focus-visible, main a:focus-visible { outline: 2px solid var(--cold); outline-offset: 2px; }

  article { min-width: 0; }

  h1 { font-family: var(--serif); font-size: clamp(2rem, 4.5vw, 2.7rem); line-height: 1.1;
       font-weight: 600; letter-spacing: -.015em; text-wrap: balance; margin: 0 0 1.4rem; }
  h2 { font-family: var(--serif); font-size: 1.55rem; font-weight: 600; letter-spacing: -.01em;
       text-wrap: balance; margin: 3rem 0 .9rem; padding-top: .6rem;
       border-top: 1px solid var(--rule); }
  h3 { font-family: var(--serif); font-size: 1.2rem; font-weight: 600; margin: 2.1rem 0 .7rem; }
  h4 { font-family: var(--sans); font-size: .82rem; font-weight: 650; letter-spacing: .07em;
       text-transform: uppercase; color: var(--ink-3); margin: 1.7rem 0 .6rem; }

  p { margin: 0 0 1.05rem; max-width: 42rem; }
  ul { margin: 1rem 0; padding-left: 1.15rem; max-width: 42rem; }
  li { margin-bottom: .55rem; }
  li::marker { color: var(--warm); }
  strong { font-weight: 650; }
  a { color: var(--cold); text-underline-offset: 2px; }
  hr { border: 0; border-top: 1px solid var(--rule); margin: 2.5rem 0; }

  code { font-family: var(--mono); font-size: .86em; background: var(--sunk);
         padding: .1em .35em; border-radius: 3px; }
  pre { background: var(--sunk); border-left: 2px solid var(--rule); border-radius: 0 5px 5px 0;
        padding: .9rem 1.1rem; margin: 1.3rem 0; overflow-x: auto; }
  pre code { background: none; padding: 0; font-size: .82rem; line-height: 1.6; }

  /* Maths is set in the serif so it reads as notation rather than as prose, and sits on the
     baseline grid of the text around it. */
  .math { font-family: var(--serif); font-style: italic; white-space: nowrap; }
  .math sub, .math sup, .display-math sub, .display-math sup { font-style: normal; }
  .display-math {
    font-family: var(--serif); font-style: italic; font-size: 1.06rem;
    background: var(--sunk); border-left: 2px solid var(--warm); border-radius: 0 5px 5px 0;
    padding: .95rem 1.15rem; margin: 1.4rem 0; overflow-x: auto; max-width: 42rem;
  }
  sub, sup { font-size: .72em; line-height: 0; }

  .tablewrap { overflow-x: auto; margin: 1.5rem 0; }
  table { border-collapse: collapse; font-size: .87rem; min-width: 100%; }
  th, td { padding: .5rem .8rem; border-bottom: 1px solid var(--rule-soft); vertical-align: top; }
  thead th { font-size: .69rem; letter-spacing: .06em; text-transform: uppercase;
             color: var(--ink-3); font-weight: 650; border-bottom: 1px solid var(--rule);
             white-space: nowrap; }
  tbody tr:last-child td { border-bottom: none; }
  .a-right { text-align: right; font-variant-numeric: tabular-nums; }
  .a-center { text-align: center; }

  @media (max-width: 60rem) {
    .shell { grid-template-columns: minmax(0, 1fr); gap: 1.6rem; padding: 2rem 1.1rem 4rem; }
    nav { position: static; max-height: none; border-left: 0; border-bottom: 1px solid var(--rule);
          padding: 0 0 1.2rem; columns: 2; column-gap: 1.5rem; }
  }
  @media (prefers-reduced-motion: reduce) { * { animation: none !important; transition: none !important; } }
`;

const html = `<title>ClimateColumn Reference</title>
<style>${css}</style>

<div class="shell">
  <nav aria-label="Contents">
    <div class="navtitle">Contents</div>
${nav}
  </nav>

  <article>
${body}
  </article>
</div>
`;

fs.writeFileSync(outPath, html, 'utf8');
console.error('wrote ' + (html.length / 1024).toFixed(0) + ' KB');
