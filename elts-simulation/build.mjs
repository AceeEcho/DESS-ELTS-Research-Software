// Build a portable page from the same source used by the in-conversation view.
import {readFile, writeFile} from 'node:fs/promises';
import {Script} from 'node:vm';
const source = await readFile(new URL('./elts-environment.html', import.meta.url),'utf8');
// Fail before producing a broken portable page if the editable script has a syntax error.
const script = source.match(/<script>([\s\S]*?)<\/script>/)?.[1];
if (!script) throw new Error('The visualization script is missing.');
new Script(script, {filename:'elts-environment.html'});
const theme = `
:root {color-scheme:dark; font:14px/1.5 system-ui,sans-serif; background:#101b29; color:#e3ecf5;}
* {box-sizing:border-box;} body {margin:0; padding:24px; max-width:1100px; margin-inline:auto;}
.viz-row,.viz-controls {display:flex; flex-wrap:wrap; align-items:center; gap:12px 20px;}
.viz-controls {align-items:end;} .form-label {display:flex;flex-direction:column;gap:5px;flex:1 1 210px;}
.btn,.form-select {font:inherit;color:inherit;background:#243545;border:1px solid #536575;border-radius:6px;padding:8px 12px;}
.btn {cursor:pointer;} .btn:hover {background:#34475a;} .form-range {width:100%;accent-color:#75b7f7;}
.form-check {display:inline-flex;align-items:center;gap:7px;} .form-check-input {accent-color:#75b7f7;width:16px;height:16px;}
.text-small {font-size:12px;} .text-muted {color:#aabbca;} .sr-only {position:absolute;width:1px;height:1px;padding:0;overflow:hidden;clip-path:inset(50%);}
strong {font-weight:500;} :focus-visible {outline:2px solid #75e2d7;outline-offset:3px;} .intro {color:#aabbca;margin:20px 0 0;max-width:80ch;}
@media(max-width:480px){body{padding:12px}.form-label{flex-basis:100%}.btn,.form-select{min-height:44px}}
`;
await writeFile(new URL('./index.html',import.meta.url),`<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>ELTS testing environment</title><style>${theme}</style></head>
<body>${source}<p class="intro">Concept visualization based on the ELTS briefing. Dimensions and movement are examples. LED paths are symbolic; no glare effect, hardware behavior, or study outcome is predicted. See README.md for assumptions and configuration.</p></body></html>`, 'utf8');
console.log('Built portable index.html');
