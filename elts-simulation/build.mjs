// Build a self-contained offline page; the editable scene uses the same theme.
import {readFile, writeFile} from 'node:fs/promises';
import {Script} from 'node:vm';
const source = await readFile(new URL('./elts-environment.html', import.meta.url), 'utf8');
const theme = await readFile(new URL('./theme.css', import.meta.url), 'utf8');
const script = source.match(/<script>([\s\S]*?)<\/script>/)?.[1];
if (!script) throw new Error('The visualization script is missing.');
new Script(script, {filename:'elts-environment.html'});
const themeLink = '<link rel="stylesheet" href="theme.css" data-standalone-theme>';
if (!source.includes(themeLink)) throw new Error('The scene theme link is missing.');
// Inline the theme at its original position to preserve the CSS cascade.
const page = source.replace(themeLink, `<style>${theme}</style>`);
const generated = `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>ELTS testing environment</title></head>
<body>${page}<p class="intro">Concept visualization based on the ELTS briefing. Dimensions and movement are examples. LED paths are symbolic; no glare effect, hardware behavior, or study outcome is predicted.</p></body></html>`;
const output = new URL('./index.html', import.meta.url);
if (process.argv.includes('--check')) {
  const current = await readFile(output, 'utf8');
  if (current.replaceAll('\r\n','\n') !== generated.replaceAll('\r\n','\n')) throw new Error('Rebuild the offline demo with node elts-simulation/build.mjs');
} else {
  await writeFile(output, generated, 'utf8');
}
