import sharp from './node_modules/sharp/lib/index.js';
import { mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

const source = new URL('../art/textbook-408-transparent.png', import.meta.url);
const target = new URL('../game/Spire408/Art/', import.meta.url);
await mkdir(target, { recursive: true });
const image = sharp(fileURLToPath(source));
const metadata = await image.metadata();
if (!metadata.hasAlpha || (await image.stats()).isOpaque) throw new Error('Relic source must have real alpha transparency.');
await image.clone().resize(256, 256, { fit: 'contain' }).png().toFile(fileURLToPath(new URL('study_book.png', target)));
const icon = await image.clone().resize(85, 85, { fit: 'contain' }).ensureAlpha().raw().toBuffer();
await sharp(icon, { raw: { width: 85, height: 85, channels: 4 } }).png().toFile(fileURLToPath(new URL('study_book_icon.png', target)));
// The game tints the outline mask itself. Preserve the icon's alpha silhouette.
for (let i = 0; i < icon.length; i += 4) icon[i] = icon[i + 1] = icon[i + 2] = 255;
await sharp(icon, { raw: { width: 85, height: 85, channels: 4 } }).png().toFile(fileURLToPath(new URL('study_book_outline.png', target)));
console.log('Relic art: transparent 256 px detail and 85 px inventory/outline assets.');
const knowledge = sharp(fileURLToPath(new URL('../art/knowledge-resource-transparent.png', import.meta.url)));
if (!(await knowledge.metadata()).hasAlpha || (await knowledge.stats()).isOpaque) throw new Error('Knowledge resource must have real alpha transparency.');
await knowledge.trim().resize(256, 256, { fit: 'contain', background: '#00000000' }).png()
  .toFile(fileURLToPath(new URL('knowledge_resource.png', target)));
console.log('Knowledge resource: transparent 256 px HUD medallion.');
