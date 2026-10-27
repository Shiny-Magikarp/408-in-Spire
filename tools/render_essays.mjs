import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const bank=JSON.parse(await fs.readFile(path.join(root,'game/Spire408/Data/essays.json'),'utf8'));
function render(text){
  // Upstream replaces figures with self-contained text and structured data.
  // Fail rather than silently discard a future figure or LaTeX dependency.
  if(/!\[|<img|\$\$|\\\[/.test(text)) throw new Error('Unsupported new essay figure/math markup');
  return text.replaceAll('[','[lb]')
    .replace(/```[^\n]*\n([\s\S]*?)```/g,'[code]$1[/code]')
    .replace(/\*\*([^*]+)\*\*/g,'[b]$1[/b]')
    .replace(/`([^`]+)`/g,'[code]$1[/code]')
    .replace(/^#{1,6}\s+(.+)$/gm,'[b]$1[/b]');
}
const presentation={};
for(const q of bank.questions){
  presentation[q.id]=Object.fromEntries(['stem','reference_answer','explanation'].map(field=>[field,render(q[field]??'')]));
}
await fs.mkdir(path.join(root,'game/Spire408/Rendered'),{recursive:true});
await fs.writeFile(path.join(root,'game/Spire408/Rendered/essay-presentation.json'),JSON.stringify(presentation));
console.log(`Rendered ${bank.questions.length} essays with preserved code and complete references.`);
