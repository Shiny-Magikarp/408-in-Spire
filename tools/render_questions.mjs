import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import {fileURLToPath} from 'node:url';
import {mathjax} from 'mathjax-full/js/mathjax.js';
import {TeX} from 'mathjax-full/js/input/tex.js';
import {SVG} from 'mathjax-full/js/output/svg.js';
import {liteAdaptor} from 'mathjax-full/js/adaptors/liteAdaptor.js';
import {RegisterHTMLHandler} from 'mathjax-full/js/handlers/html.js';
import {AllPackages} from 'mathjax-full/js/input/tex/AllPackages.js';
import sharp from 'sharp';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const output=path.join(root,'game/Spire408/Rendered');
const source=path.join(root,'data');
await fs.mkdir(output,{recursive:true});
await fs.cp(source,path.join(root,'game/Spire408/Data'),{recursive:true,
  filter:name=>!['essay-upstream.json','essay-originals.json','CS408-ESSAYS-README.md'].includes(path.basename(name))});
const bank=JSON.parse(await fs.readFile(path.join(source,'questions.json'),'utf8'));
const fixes=JSON.parse(await fs.readFile(path.join(root,'tools/presentation-fixes.json'),'utf8'));
const quarantined=bank.questions.filter(q=>fixes[q.id]?.exclude_from_game).map(q=>({id:q.id,reason:fixes[q.id].exclude_from_game}));
bank.questions=bank.questions.filter(q=>!fixes[q.id]?.exclude_from_game);
await fs.writeFile(path.join(root,'game/Spire408/Data/questions.json'),JSON.stringify(bank));
const adaptor=liteAdaptor(); RegisterHTMLHandler(adaptor);
const document=mathjax.document('',{InputJax:new TeX({packages:AllPackages}),OutputJax:new SVG({fontCache:'none'})});
const cache=new Map();
const errors=[];
const escape=text=>text.replaceAll('[','[lb]');
function token(file,w,h){return `@@img|Spire408/Rendered/${file}|${Math.round(w)}|${Math.round(h)}@@`;}
async function formula(tex,display,id){
  tex=tex.trim();
  const key=crypto.createHash('sha256').update('v1:'+tex).digest('hex').slice(0,24);
  const file=key+'.png';
  try{
    let info=cache.get(key);
    if(!info){
      try {info=await sharp(path.join(output,file)).metadata();}
      catch {
        const node=document.convert(tex,{display,em:22,ex:11,containerWidth:1000});
        let markup=adaptor.outerHTML(node);
        if(markup.includes('data-mml-node="merror"')) throw new Error('MathJax rejected expression');
        markup=markup.substring(markup.indexOf('<svg'),markup.lastIndexOf('</svg>')+6);
        markup=markup.replaceAll('currentColor','#e3e8ef');
        // SVG ex units are tied to a 22px font, unlike librsvg's default 12px.
        markup=markup.replace(/(width|height)="([\d.]+)ex"/g,(_,a,n)=>`${a}="${Number(n)*11}px"`);
        await sharp(Buffer.from(markup),{density:144}).png().toFile(path.join(output,file));
        info=await sharp(path.join(output,file)).metadata();
      }
      cache.set(key,info);
    }
    const w=info.width/2, h=info.height/2;
    const scale=Math.min(1,1000/w);
    return token(file,w*scale,h*scale);
  }catch(error){errors.push({id,tex,error:String(error)});return escape(tex);}
}
async function diagram(ref,id){
  const relative=decodeURIComponent(ref.trim().replace(/^<|>$/g,''));
  const full=path.resolve(source,relative);
  if(!full.startsWith(source+path.sep)) throw new Error('asset outside data directory');
  const file='diagram-'+crypto.createHash('sha256').update(relative).digest('hex').slice(0,20)+'.png';
  const info=await sharp(full).png().toFile(path.join(output,file));
  const scale=Math.min(1.5,960/info.width,480/info.height);
  return '\n'+token(file,info.width*scale,info.height*scale)+'\n';
}
async function render(text,id){
  // The source sometimes embeds Mermaid source as an alternative to the same diagram image.
  if(/!\[|<img/i.test(text)) text=text.replace(/<details\b[^>]*>[\s\S]*?<\/details>/gi, block=>/```mermaid/i.test(block)?'':block);
  const fragments=[];
  // Protect diagrams and formulas before escaping any upstream BBCode-looking text.
  const pattern=/!\[[^\]]*\]\(\s*(<[^>]+>|[^\s)]+)(?:\s+"[^"]*")?\s*\)|<img\b[^>]*src=["']([^"']+)["'][^>]*>|\$\$([\s\S]*?)\$\$|(?<!\\)\$([^$\n]+?)\$/gi;
  let start=0;
  for(const match of text.matchAll(pattern)){
    fragments.push(escape(text.slice(start,match.index)));
    fragments.push(match[1]||match[2]?await diagram(match[1]||match[2],id):await formula(match[3]??match[4],!!match[3],id));
    start=match.index+match[0].length;
  }
  fragments.push(escape(text.slice(start)));
  return fragments.join('')
    .replace(/\\([*_`])/g,'$1')
    .replace(/```[^\n]*\n([\s\S]*?)```/g,'[code]$1[/code]')
    .replace(/\*\*([^*]+)\*\*/g,'[b]$1[/b]')
    .replace(/`([^`]+)`/g,'[code]$1[/code]')
    .replace(/^#{1,6}\s+(.+)$/gm,'[b]$1[/b]')
    .replace(/<br\s*\/?\s*>/gi,'\n')
    .replace(/&lt;/g,'<').replace(/&gt;/g,'>').replace(/&amp;/g,'&');
}
const presentation={};
let count=0;
for(const q of bank.questions){
  const entry={};
  for(const [field,original] of Object.entries({stem:q.stem,...q.options,explanation:q.explanation})) {
    let text=original;
    const fix=fixes[q.id]?.[field];
    if(fix){
      if(!text.includes(fix.before)) throw new Error(`Stale presentation fix: ${q.id}/${field}`);
      text=text.replace(fix.before,fix.after);
    }
    entry[field]=await render(text,q.id);
  }
  presentation[q.id]=entry;
  if(++count%400===0) console.log(`Rendered ${count}/${bank.questions.length}`);
}
await fs.writeFile(path.join(output,'presentation.json'),JSON.stringify(presentation));
const bankSha256=crypto.createHash('sha256').update(await fs.readFile(path.join(root,'game/Spire408/Data/questions.json'))).digest('hex');
const presentationSha256=crypto.createHash('sha256').update(await fs.readFile(path.join(output,'presentation.json'))).digest('hex');
await fs.writeFile(path.join(root,'reports/render-report.json'),JSON.stringify({questions:count,formulaCount:cache.size,bank_sha256:bankSha256,presentation_sha256:presentationSha256,quarantined,errors},null,2));
console.log(JSON.stringify({questions:count,formulaCount:cache.size,errors:errors.length}));
if(errors.length) process.exitCode=2;
