# Clipcy website

Run commands from this directory with Node.js 20 or newer:

- `npm run dev` serves the local website at http://localhost:4321.
- `npm run build` generates the static site in `dist/`.
- `npm run preview` serves the production build locally.

The homepage replaces the former feature cards and workflow section with five
scroll-driven feature slides: search, arrow-key selection, pinning, numbered
selection, and plain-text paste. `src/components/FeatureSlideshow.astro` owns the
layout and illustrative demos; `src/content/feature-slides.mjs` contains their
copy for all six supported languages.

Demos loop while their slide is in view. They
stop when the page is hidden and display static examples for reduced motion.
The progress links work without JavaScript. The old `#features` and `#workflow`
anchors still lead to the slideshow.

`npm run test` currently discovers no tests. Use the production build plus
browser checks of desktop/mobile layouts and demo states for website changes.
