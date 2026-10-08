# Clipcy website

Run commands from this directory with Node.js 20 or newer:

- `npm run dev` serves the local website at http://localhost:4321.
- `npm run build` generates the static site in `dist/`.
- `npm run preview` serves the production build locally.

The homepage replaces the former feature cards and workflow section with five
carousel feature slides: search, arrow-key selection, pinning, numbered
selection, and plain-text paste. `src/components/FeatureSlideshow.astro` owns the
layout and illustrative demos; `src/content/feature-slides.mjs` contains their
copy for all six supported languages.

Demos loop while their slide is in view. They
stop when the page is hidden and display static examples for reduced motion.
Arrow buttons, progress links, Left/Right and Home/End keys, and touch swipes
switch features within a single page section.
Features animate into view and advance every six seconds, looping from the last
back to the first. Slides enter from right to left; the fifth-to-first transition
reverses to left to right. Playback pauses while hovering the demo or focusing carousel
controls, while offscreen, and while the page is hidden. Reduced motion disables
automatic playback and slide transitions.
Full-height clickable edges overlay the demo image with smoky gradients and
centered arrows. They appear on hover or keyboard focus;
they stay visible on touch screens. Without JavaScript, all five
slides remain visible and the progress links work. The old `#features` and `#workflow`
anchors still lead to the slideshow.

`npm run test` currently discovers no tests. Use the production build plus
browser checks of desktop/mobile layouts and demo states for website changes.
