// Makes the two PNG icons of the link preview from public/favicon.svg.
//
// A chat or social app does not draw an SVG icon, so the site also offers the
// favicon as PNG: 32 px for a link preview and a browser that wants one, 180 px
// for a phone's home screen. Run it by hand after changing the favicon, and
// commit the result; it is not part of the build. sharp is here because Astro
// installs it (an optional dependency of its own), so nothing is added for it.
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import sharp from 'sharp';

const PUBLIC = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', 'public');
const VIEWBOX = 56; // the favicon's viewBox is 56 units wide

// The home-screen icon is a full square in the tile's colour: a phone rounds
// the corners itself and paints any transparency black.
const TILE = '#0b0b14';

for (const [size, name, square] of [
  [32, 'favicon-32.png', false],
  [180, 'apple-touch-icon.png', true],
]) {
  // Rasterise at four times the target, then scale down, for clean edges.
  let image = sharp(path.join(PUBLIC, 'favicon.svg'), { density: (72 * size * 4) / VIEWBOX }).resize(size, size);
  if (square) image = image.flatten({ background: TILE });
  await image.png().toFile(path.join(PUBLIC, name));
  console.log(`preview-icons: public/${name}, ${size} x ${size}`);
}
